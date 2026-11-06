using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Services.Workshop;
using Xunit;

namespace RimSharp.Tests.Services.Workshop
{
    /// <summary>
    /// Tests for the consolidated WorkshopUpdateService check path: the decision
    /// contract (external = API time_updated; internal = max(ACF, timestamp.txt)),
    /// clock-skew tolerance, and failure mapping. Hits the real SteamWorkshopApiClient
    /// with a stubbed HttpMessageHandler — no network involved.
    /// </summary>
    public class WorkshopUpdateServiceTests
    {
        private readonly ILoggerService _logger = Substitute.For<ILoggerService>();

        // 18/02/2026 12:00:00 UTC (the timestamp.txt wall-clock the app's own
        // downloader historically wrote, parsed back as UTC)
        private const string LocalDate = "18/02/2026 12:00:00";

        private WorkshopUpdateService CreateService(StubSteamHandler handler) =>
            new(
                new GameInstance(),
                _logger,
                new SteamWorkshopApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }));

        [Fact]
        public async Task CheckForUpdatesAsync_TimestampOnly_ValuedByTimestampComparison()
        {
            // External = 1h after the local record 18/02/2026 12:00:00 UTC
            var handler = new StubSteamHandler(RecentDetailsJson(ExternalFor(localHour: 13)));
            var service = CreateService(handler);
            var mod = new ModItem { Name = "T", SteamId = "123", ModType = ModType.Tracked, UpdateDate = LocalDate };

            var result = await service.CheckForUpdatesAsync(new[] { mod });

            result.Status.Should().Be(WorkshopUpdateStatus.Success);
            result.UpdatesAvailable.Should().Be(1);
            result.Updates.Should().ContainSingle(c => c.SteamIdEquals("123"));
            result.Updates[0].Decision.Should().Be(WorkshopUpdateDecision.UpdateAvailable);
            result.Updates[0].InternalTime.Should().Be(ExternalFor(localHour: 13) - 3600);
        }

        [Fact]
        public async Task CheckForUpdatesAsync_AcfNewerThanTimestamp_AcfValueWinsAsInternal()
        {
            // ACF record is 2h later than timestamp.txt → the max() is the ACF stamp; the
            // API reports content 1h after THAT → still an update, but relative to ACF time.
            var acfTime = ExternalFor(localHour: 14);
            var handler = new StubSteamHandler(RecentDetailsJson(ExternalFor(localHour: 15)));
            var service = CreateService(handler);
            var mod = new ModItem
            {
                Name = "T", SteamId = "123", ModType = ModType.Tracked,
                UpdateDate = LocalDate, // 12:00 — older than the ACF record
                AcfTimeUpdated = acfTime
            };

            var result = await service.CheckForUpdatesAsync(new[] { mod });

            result.UpdatesAvailable.Should().Be(1);
            result.Updates[0].InternalTime.Should().Be(acfTime);
            result.Updates[0].Decision.Should().Be(WorkshopUpdateDecision.UpdateAvailable);
        }

        [Fact]
        public async Task CheckForUpdatesAsync_SmallSkew_TreatedAsUpToDate()
        {
            // API 60s ahead of the local record — historical 2-minute tolerance
            var handler = new StubSteamHandler(RecentDetailsJson(ExternalFor(localHour: 12, plusSeconds: 60)));
            var service = CreateService(handler);
            var mod = new ModItem { Name = "T", SteamId = "123", ModType = ModType.Tracked, UpdateDate = LocalDate };

            var result = await service.CheckForUpdatesAsync(new[] { mod });

            result.UpdatesAvailable.Should().Be(0);
            result.Updates[0].Decision.Should().Be(WorkshopUpdateDecision.UpToDate);
        }

        [Fact]
        public async Task CheckForUpdatesAsync_NoInternalData_IsInitialSyncPossible()
        {
            var handler = new StubSteamHandler(RecentDetailsJson(ExternalFor(localHour: 13)));
            var service = CreateService(handler);
            var mod = new ModItem { Name = "T", SteamId = "123", ModType = ModType.Tracked };

            var result = await service.CheckForUpdatesAsync(new[] { mod });

            result.UpdatesAvailable.Should().Be(0); // Initial sync is NOT an "update available"
            result.Updates[0].Decision.Should().Be(WorkshopUpdateDecision.InitialSyncPossible);
        }

        [Fact]
        public async Task CheckForUpdatesAsync_InternalNewerOrEqual_IsUpToDate()
        {
            // ACF is 1h ahead of API time (rollback protection)
            var handler = new StubSteamHandler(RecentDetailsJson(ExternalFor(localHour: 13)));
            var service = CreateService(handler);
            var mod = new ModItem
            {
                Name = "T", SteamId = "123", ModType = ModType.Tracked,
                AcfTimeUpdated = ExternalFor(localHour: 14)
            };

            var result = await service.CheckForUpdatesAsync(new[] { mod });

            result.UpdatesAvailable.Should().Be(0);
            result.Updates[0].Decision.Should().Be(WorkshopUpdateDecision.UpToDate);
        }

        [Fact]
        public async Task CheckForUpdatesAsync_ResourceNotFound_MapsToFailure()
        {
            var handler = new StubSteamHandler(NotFoundDetailsJson(resultCode: 15, steamId: "123"));
            var service = CreateService(handler);
            var mod = new ModItem { Name = "T", SteamId = "123", ModType = ModType.Tracked, UpdateDate = LocalDate };

            var result = await service.CheckForUpdatesAsync(new[] { mod });

            result.Status.Should().Be(WorkshopUpdateStatus.Failed);
            result.FailedModIds.Should().Contain("123");
            result.ErrorMessages.Should().Contain(m => m.Contains("not found"));
            result.Updates.Should().BeEmpty();
        }

        [Fact]
        public async Task CheckForUpdatesAsync_DetailMissingFromResponse_MapsToFailure()
        {
            // API response for an empty detail list — id resolved but nothing returned
            var handler = new StubSteamHandler("{\"response\":{\"result\":1,\"publishedfiledetails\":[]}}");
            var service = CreateService(handler);
            var mod = new ModItem { Name = "T", SteamId = "123", ModType = ModType.Tracked, UpdateDate = LocalDate };

            var result = await service.CheckForUpdatesAsync(new[] { mod });

            result.Status.Should().Be(WorkshopUpdateStatus.Failed);
            result.FailedModIds.Should().Contain("123");
            result.ErrorMessages.Should().Contain(m => m.Contains("No details returned"));
            result.Updates.Should().BeEmpty();
        }

        [Fact]
        public async Task CheckForUpdatesAsync_NonRimWorldAppId_MapsToFailure()
        {
            var handler = new StubSteamHandler(RecentDetailsJson(ExternalFor(localHour: 13), consumerAppId: 105600));
            var service = CreateService(handler);
            var mod = new ModItem { Name = "T", SteamId = "123", ModType = ModType.Tracked, UpdateDate = LocalDate };

            var result = await service.CheckForUpdatesAsync(new[] { mod });

            result.FailedModIds.Should().Contain("123");
            result.ErrorMessages.Should().Contain(m => m.Contains("105600"));
            result.Updates.Should().BeEmpty();
        }

        [Fact]
        public async Task CheckForUpdatesAsync_NoEligibleMods_ReturnsNoWorkshopMods()
        {
            var handler = new StubSteamHandler(RecentDetailsJson(ExternalFor(localHour: 13)));
            var service = CreateService(handler);
            var localMod = new ModItem { Name = "Untracked", ModType = ModType.Untracked };

            var result = await service.CheckForUpdatesAsync(new[] { localMod });

            result.Status.Should().Be(WorkshopUpdateStatus.NoWorkshopMods);
            result.ModsChecked.Should().Be(0);
            handler.PostedBodies.Should().BeEmpty(); // no request should have been sent
        }

        [Fact]
        public async Task CheckForUpdatesAsync_ReportsEachModBeingChecked()
        {
            // Two real detail records so both mods resolve without errors.
            var twoDetailsJson =
                "{\"response\":{\"result\":1,\"publishedfiledetails\":[" +
                "{\"publishedfileid\":\"111\",\"result\":1,\"consumer_appid\":294100,\"title\":\"First Mod\",\"time_updated\":1,\"file_size\":1,\"tags\":[]}," +
                "{\"publishedfileid\":\"222\",\"result\":1,\"consumer_appid\":294100,\"title\":\"Second Mod\",\"time_updated\":2,\"file_size\":4,\"tags\":[]}]}}";
            var handler = new StubSteamHandler(twoDetailsJson);
            var service = CreateService(handler);
            var reports = new List<(int current, int total, string modName)>();
            var progress = new Progress<(int current, int total, string modName)>(reports.Add);
            var first = new ModItem { Name = "First Mod", SteamId = "111", ModType = ModType.Tracked };
            var second = new ModItem { Name = "Second Mod", SteamId = "222", ModType = ModType.Tracked };

            var result = await service.CheckForUpdatesAsync(new[] { first, second }, progress);
            await Task.Delay(50); // Progress<T> callbacks are asynchronous

            result.Status.Should().Be(WorkshopUpdateStatus.Success);
            reports.Should().HaveCount(2);
            reports[0].current.Should().Be(1);
            reports[0].total.Should().Be(2);
            reports[0].modName.Should().Be("First Mod");
            reports[1].current.Should().Be(2);
            reports[1].modName.Should().Be("Second Mod");
        }

        // ------------------------------------------------------------------ //

        private static long ExternalFor(int localHour, int plusSeconds = 0)
        {
            var wall = new DateTimeOffset(2026, 2, 18, localHour, 0, 0, TimeSpan.Zero);
            return wall.ToUnixTimeSeconds() + plusSeconds;
        }

        private static string RecentDetailsJson(long timeUpdated, long consumerAppId = 294100) =>
            // The response shape the ISteamRemoteStorage/GetPublishedFileDetails endpoint returns.
            "{\"response\":{\"result\":1,\"publishedfiledetails\":[" +
            $"{{\"publishedfileid\":\"123\",\"result\":1,\"consumer_appid\":{consumerAppId}," +
            $"\"title\":\"Test Mod\",\"time_updated\":{timeUpdated},\"file_size\":1024," +
            "\"tags\":[{\"tag\":\"1.6\"},{\"tag\":\"Mod\"}]}]}}";

        private static string NotFoundDetailsJson(int resultCode, string steamId) =>
            "{\"response\":{\"result\":1,\"publishedfiledetails\":[" +
            $"{{\"publishedfileid\":\"{steamId}\",\"result\":{resultCode}," +
            "\"title\":\"\",\"time_updated\":0,\"file_size\":0}]}}";

        /// <summary>Stubs the Steam HTTP endpoint: returns one canned JSON response.</summary>
        private sealed class StubSteamHandler : HttpMessageHandler
        {
            private readonly string _responseJson;

            public StubSteamHandler(string responseJson)
            {
                _responseJson = responseJson;
            }

            public List<string> PostedBodies { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Content != null)
                {
                    PostedBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
                };
            }
        }
    }

    internal static class WorkshopUpdateCandidateTestExtensions
    {
        public static bool SteamIdEquals(this WorkshopUpdateCandidate candidate, string steamId) =>
            candidate.Mod.SteamId == steamId;
    }
}
