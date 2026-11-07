using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Services.Workshop;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Features.WorkshopDownloader.Services
{
    /// <summary>
    /// The checker is a thin adapter over the consolidated WorkshopUpdateService:
    /// only updated mods the app owns (Type.Tracked) are enqueued; Steam-client
    /// mods are reported but never re-downloaded via SteamCMD; failures map onto
    /// the downloader's UpdateCheckResult.
    /// </summary>
    public class WorkshopUpdateCheckerServiceTests
    {
        private readonly IWorkshopUpdateService _mockCentralService;
        private readonly IDownloadQueueService _mockDownloadQueueService;
        private readonly WorkshopUpdateCheckerService _service;

        public WorkshopUpdateCheckerServiceTests()
        {
            _mockCentralService = Substitute.For<IWorkshopUpdateService>();
            _mockDownloadQueueService = Substitute.For<IDownloadQueueService>();
            _service = new WorkshopUpdateCheckerService(_mockCentralService, _mockDownloadQueueService);
        }

        [Fact]
        public async Task CheckForUpdatesAsync_TrackedModUpdate_ShouldAddToQueue()
        {
            var central = CentralResult(candidates: new[]
            {
                Candidate("123", ModType.Tracked, WorkshopUpdateDecision.UpdateAvailable, "Test Mod", fileSize: 2048)
            });
            _mockCentralService.CheckForUpdatesAsync(default!, null, TestContext.Current.CancellationToken)
                .ReturnsForAnyArgs(Task.FromResult(central));

            var result = await _service.CheckForUpdatesAsync(new[] { NewMod("123", ModType.Tracked) }, cancellationToken: TestContext.Current.CancellationToken);

            result.ModsChecked.Should().Be(1);
            result.UpdatesFound.Should().Be(1);
            result.ErrorsEncountered.Should().Be(0);
            _mockDownloadQueueService.Received(1).AddToQueue(
                Arg.Is<ModInfoDto>(m =>
                    m.SteamId == "123" &&
                    m.Name == "Test Mod" &&
                    m.FileSize == 2048 &&
                    !string.IsNullOrEmpty(m.StandardDate)));
        }

        [Fact]
        public async Task CheckForUpdatesAsync_SteamClientModUpdate_ShouldNeverBeQueued()
        {
            var central = CentralResult(candidates: new[]
            {
                Candidate("456", ModType.Workshop, WorkshopUpdateDecision.UpdateAvailable, "Client Mod")
            });
            _mockCentralService.CheckForUpdatesAsync(default!, null, TestContext.Current.CancellationToken)
                .ReturnsForAnyArgs(Task.FromResult(central));

            var result = await _service.CheckForUpdatesAsync(new[] { NewMod("456", ModType.Workshop) }, cancellationToken: TestContext.Current.CancellationToken);

            result.UpdatesFound.Should().Be(0);
            _mockDownloadQueueService.DidNotReceive().AddToQueue(Arg.Any<ModInfoDto>());
        }

        [Fact]
        public async Task CheckForUpdatesAsync_NoUpdateDecision_ShouldNotAddToQueue()
        {
            var central = CentralResult(candidates: new[]
            {
                Candidate("123", ModType.Tracked, WorkshopUpdateDecision.UpToDate)
            });
            _mockCentralService.CheckForUpdatesAsync(default!, null, TestContext.Current.CancellationToken)
                .ReturnsForAnyArgs(Task.FromResult(central));

            var result = await _service.CheckForUpdatesAsync(new[] { NewMod("123", ModType.Tracked) }, cancellationToken: TestContext.Current.CancellationToken);

            result.UpdatesFound.Should().Be(0);
            _mockDownloadQueueService.DidNotReceive().AddToQueue(Arg.Any<ModInfoDto>());
        }

        [Fact]
        public async Task CheckForUpdatesAsyncCentralFailures_ShouldMapIntoErrors()
        {
            var central = CentralResult(candidates: Array.Empty<WorkshopUpdateCandidate>());
            central.FailedModIds.Add("999");
            central.ErrorMessages.Add("No details returned for 'Gone' (999).");
            _mockCentralService.CheckForUpdatesAsync(default!, null, TestContext.Current.CancellationToken)
                .ReturnsForAnyArgs(Task.FromResult(central));

            var result = await _service.CheckForUpdatesAsync(new[] { NewMod("999", ModType.Tracked) }, cancellationToken: TestContext.Current.CancellationToken);

            result.ErrorsEncountered.Should().Be(1);
            result.ErrorMessages.Should().Contain(m => m.Contains("999"));
        }

        [Fact]
        public async Task CheckForUpdatesAsync_NullMods_ShouldReturnEmptyResultWithoutCentralCall()
        {
            var result = await _service.CheckForUpdatesAsync(null!, null, TestContext.Current.CancellationToken);

            result.ModsChecked.Should().Be(0);
            result.UpdatesFound.Should().Be(0);
            _mockCentralService.DidNotReceiveWithAnyArgs();
        }

        [Fact]
        public async Task CheckForUpdatesAsync_ProgressShouldBeForwarded()
        {
            var central = CentralResult(candidates: Array.Empty<WorkshopUpdateCandidate>());
            central.ModsChecked = 1;
            _mockCentralService.CheckForUpdatesAsync(default!, null, TestContext.Current.CancellationToken)
                .ReturnsForAnyArgs(info =>
                {
                    info.ArgAt<IProgress<(int current, int total, string message)>>(1)
                        ?.Report((1, 1, "Mod 123"));
                    return Task.FromResult(central);
                });

            var reports = new List<(int current, int total, string modName)>();
            var progress = new Progress<(int current, int total, string modName)>(r =>
            {
                lock (reports) { reports.Add(r); }
            });

            await _service.CheckForUpdatesAsync(new[] { NewMod("123", ModType.Tracked) }, progress, TestContext.Current.CancellationToken);
            await Task.Delay(50, TestContext.Current.CancellationToken); // Progress<T> callbacks are async

            reports.Should().Contain(r =>
                r.current == 1 && r.total == 1 && r.modName == "Mod 123");
        }

        // ------------------------------------------------------------------ //

        private static ModItem NewMod(string steamId, ModType type) =>
            new() { Name = $"Mod {steamId}", SteamId = steamId, ModType = type };

        private static WorkshopUpdateResult CentralResult(WorkshopUpdateCandidate[] candidates)
        {
            var result = new WorkshopUpdateResult { Status = WorkshopUpdateStatus.Success };
            result.ModsChecked = candidates.Length;
            result.UpdatesAvailable = candidates.Count(c => c.Decision == WorkshopUpdateDecision.UpdateAvailable);
            result.Updates.AddRange(candidates);
            return result;
        }

        private static WorkshopUpdateCandidate Candidate(
            string steamId, ModType type, WorkshopUpdateDecision decision,
            string? title = null, long fileSize = 0)
        {
            return new WorkshopUpdateCandidate
            {
                Mod = NewMod(steamId, type),
                Details = new WorkshopModDetails
                {
                    PublishedFileId = steamId,
                    Result = 1,
                    ConsumerAppId = 294100,
                    Title = title,
                    TimeUpdated = 1700000000,
                    FileSize = fileSize,
                    Tags = new List<WorkshopTag> { new() { Tag = "1.6" } }
                },
                ExternalTimeUpdated = 1700000000,
                InternalTime = 1699999400,
                Decision = decision
            };
        }
    }
}