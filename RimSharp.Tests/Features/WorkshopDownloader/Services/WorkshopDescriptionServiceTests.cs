using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.WorkshopDownloader.Services
{
    public class WorkshopDescriptionServiceTests : IDisposable
    {
        private readonly ISteamApiClient _mockSteamApiClient;
        private readonly IHttpClientFactory _mockHttpClientFactory;
        private readonly ILoggerService _mockLogger;
        private readonly WorkshopDescriptionService _service;

        public WorkshopDescriptionServiceTests()
        {
            _mockSteamApiClient = Substitute.For<ISteamApiClient>();
            _mockHttpClientFactory = Substitute.For<IHttpClientFactory>();
            _mockLogger = Substitute.For<ILoggerService>();
            _service = new WorkshopDescriptionService(_mockSteamApiClient, _mockHttpClientFactory, _mockLogger);
        }

        public void Dispose()
        {
            // Temp folders are cleaned per-test; nothing global to tear down.
        }

        // ---- Focused unit tests over the tag helpers -------------------------

        [Fact]
        public void ExtractWorkshopTagValuesFromHtml_ParsesRequiredTagHrefParams()
        {
            // Live-page shape: URL-encoded brackets, params joined with &.
            var html = @"<div class=""rightDetailsBlock"">
                <a href=""https://steamcommunity.com/workshop/browse/?appid=294100&browsesort=trend&requiredtags%5B%5D=Translation"">Translation</a>
                <a href=""https://steamcommunity.com/workshop/browse/?appid=294100&browsesort=trend&requiredtags%5B%5D=Scenario"">Scenario</a>
                <a href=""https://steamcommunity.com/workshop/browse/?appid=294100&browsesort=trend&requiredtags%5B%5D=1.6"">1.6</a>
            </div>";

            var values = WorkshopDescriptionService.ExtractWorkshopTagValuesFromHtml(html).ToList();

            values.Should().BeEquivalentTo(new[] { "Translation", "Scenario", "1.6" }, o => o.WithStrictOrdering());
        }

        [Fact]
        public void ExtractWorkshopTagValuesFromHtml_HandlesUnescapedBracketsAndAmpEntity()
        {
            // Unescaped [] form and an & separator between params.
            var html = @"<a href=""?requiredtags[]=Translation&browsesort=trend"">Translation</a>";

            var values = WorkshopDescriptionService.ExtractWorkshopTagValuesFromHtml(html).ToList();

            values.Should().BeEquivalentTo(new[] { "Translation" });
        }

        [Fact]
        public void ExtractWorkshopTagValuesFromHtml_UrlDecodesCaptures()
        {
            var html = @"<a href=""?requiredtags%5B%5D=Scenario"">Scenario</a>";

            var values = WorkshopDescriptionService.ExtractWorkshopTagValuesFromHtml(html).ToList();

            values.Should().BeEquivalentTo(new[] { "Scenario" });
        }

        [Fact]
        public void ExtractWorkshopTagValuesFromHtml_EmptyHtml_YieldsNothing()
        {
            WorkshopDescriptionService.ExtractWorkshopTagValuesFromHtml(string.Empty).Should().BeEmpty();
            WorkshopDescriptionService.ExtractWorkshopTagValuesFromHtml("<div>no tags here</div>").Should().BeEmpty();
        }

        [Fact]
        public void FilterRetainedWorkshopTags_KeepsOnlyTranslationAndScenario_CaseInsensitive()
        {
            var input = new[] { "Translation", "1.6", "Scenario", "translation", "Mod", "1.5", "scenario" };

            var result = WorkshopDescriptionService.FilterRetainedWorkshopTags(input);

            // First-seen casing preserved; case-insensitive dupes and version tags dropped.
            result.Should().BeEquivalentTo(new[] { "Translation", "Scenario" }, o => o.WithStrictOrdering());
        }

        [Fact]
        public void FilterRetainedWorkshopTags_NullOrEmpty_ReturnsEmptyList()
        {
            WorkshopDescriptionService.FilterRetainedWorkshopTags(null).Should().BeEmpty();
            WorkshopDescriptionService.FilterRetainedWorkshopTags(Array.Empty<string>()).Should().BeEmpty();
        }

        // ---- Integration: tags flow through EnrichModAsync into RimSharp.json -

        [Fact]
        public async Task EnrichModAsync_ApiPath_PersistsTranslationAndScenarioTags()
        {
            const string steamId = "2557451735";
            var modFolder = CreateTempModFolder();
            try
            {
                var apiResponse = new SteamApiResponse
                {
                    Response = new SteamApiResponseDetails
                    {
                        PublishedFileDetails = new List<SteamPublishedFileDetails>
                        {
                            new SteamPublishedFileDetails
                            {
                                Result = 1,
                                Title = "My Translation Mod",
                                Description = "A sufficiently long description for the API extractor to accept.",
                                Tags = new List<SteamTag>
                                {
                                    new SteamTag { Tag = "Translation" },
                                    new SteamTag { Tag = "1.6" },
                                    new SteamTag { Tag = "Scenario" }
                                }
                            }
                        }
                    }
                };
                _mockSteamApiClient.GetFileDetailsAsync(steamId, Arg.Any<CancellationToken>()).Returns(apiResponse);

                var result = await _service.EnrichModAsync(steamId, modFolder, CancellationToken.None);

                result.Should().NotBeNull();
                var record = ReadRecord(modFolder);
                record.Tags.Should().BeEquivalentTo(new[] { "Translation", "Scenario" }, o => o.WithStrictOrdering());
                record.Tags.Should().NotContain("1.6");
            }
            finally
            {
                SafeDelete(modFolder);
            }
        }

        [Fact]
        public async Task EnrichModAsync_HtmlPath_PersistsTranslationAndScenarioTags_IgnoresVersionTags()
        {
            const string steamId = "2557451735";
            var modFolder = CreateTempModFolder();
            try
            {
                // API turns up empty → HTML fallback runs.
                _mockSteamApiClient.GetFileDetailsAsync(steamId, Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<SteamApiResponse?>(null));

                var html = @"<html><body>
                    <div class=""workshopItemTitle"">My Translation Mod</div>
                    <div class=""rightDetailsBlock"">
                        <a href=""https://steamcommunity.com/workshop/browse/?appid=294100&browsesort=trend&requiredtags%5B%5D=Translation"">Translation</a>
                        <a href=""https://steamcommunity.com/workshop/browse/?appid=294100&browsesort=trend&requiredtags%5B%5D=Scenario"">Scenario</a>
                        <a href=""https://steamcommunity.com/workshop/browse/?appid=294100&browsesort=trend&requiredtags%5B%5D=1.6"">1.6</a>
                    </div>
                    <div id=""highlightContent"">This is a long workshop description that easily exceeds the forty character minimum required by the HTML extractor.</div>
                    </body></html>";

                var httpClient = new HttpClient(new StubHttpMessageHandler(html));
                _mockHttpClientFactory.CreateClient("WorkshopHtml").Returns(httpClient);

                var result = await _service.EnrichModAsync(steamId, modFolder, CancellationToken.None);

                result.Should().NotBeNull();
                var record = ReadRecord(modFolder);
                record.Tags.Should().BeEquivalentTo(new[] { "Translation", "Scenario" }, o => o.WithStrictOrdering());
                record.Tags.Should().NotContain("1.6");
            }
            finally
            {
                SafeDelete(modFolder);
            }
        }

        [Fact]
        public async Task SaveCacheAsync_WhenNoTagsProvided_PersistsEmptyTagList()
        {
            const string steamId = "2557451735";
            var modFolder = CreateTempModFolder();
            try
            {
                var written = await _service.SaveCacheAsync(steamId, modFolder, "some description", "browser", "Mod Name", CancellationToken.None);

                written.Should().BeTrue();
                ReadRecord(modFolder).Tags.Should().BeEmpty();
            }
            finally
            {
                SafeDelete(modFolder);
            }
        }

        // ---- helpers ---------------------------------------------------------

        private static string CreateTempModFolder()
        {
            var dir = Path.Combine(Path.GetTempPath(), "RimSharp_WdsTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void SafeDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch
            {
                // best-effort cleanup
            }
        }

        private static WorkshopDescriptionRecord ReadRecord(string modFolder)
        {
            var path = Path.Combine(modFolder, WorkshopDescriptionCache.FileName);
            File.Exists(path).Should().BeTrue();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<WorkshopDescriptionRecord>(json)!;
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly string _content;
            public StubHttpMessageHandler(string content) => _content = content;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_content, System.Text.Encoding.UTF8, "text/html")
                };
                return Task.FromResult(response);
            }
        }
    }
}