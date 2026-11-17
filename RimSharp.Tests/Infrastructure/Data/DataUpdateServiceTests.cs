using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Data;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Data
{
    public class DataUpdateServiceTests : IDisposable
    {
        private readonly ILoggerService _mockLogger;
        private readonly string _testTempDir;
        private readonly MockHttpMessageHandler _httpHandler;
        private readonly HttpClient _httpClient;

        public DataUpdateServiceTests()
        {
            _mockLogger = Substitute.For<ILoggerService>();
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharp_DataUpdateTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);

            _httpHandler = new MockHttpMessageHandler { OnRequest = req => new HttpResponseMessage(HttpStatusCode.NotFound) };
            _httpClient = new HttpClient(_httpHandler);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        [Fact]
        public async Task CheckForAndApplyUpdatesAsync_WhenNoLocalManifest_ShouldDownloadAll()
        {

            var manifestJson = @"{ ""version"": ""1.0"", ""files"": { ""rules"": { ""path"": ""rules.json"" } } }";
            var rulesJson = @"{ ""rules"": [] }";

            _httpHandler.OnRequest = req =>
            {
                var uri = req.RequestUri?.ToString() ?? "";
                if (uri.EndsWith("manifest.json"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson) };
                if (uri.EndsWith("rules.json"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(rulesJson) };
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            };

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient);
            var progress = Substitute.For<IProgress<DataUpdateProgress>>();

            await service.CheckForAndApplyUpdatesAsync(progress, CancellationToken.None);

            File.Exists(Path.Combine(_testTempDir, "Data", "db", "manifest.json")).Should().BeTrue();
            File.Exists(Path.Combine(_testTempDir, "Data", "db", "rules.json")).Should().BeTrue();
            File.ReadAllText(Path.Combine(_testTempDir, "Data", "db", "rules.json")).Should().Be(rulesJson);
        }

        [Fact]
        public async Task CheckForAndApplyUpdatesAsync_WhenManifestMatches_ShouldSkipDownload()
        {

            var manifestJson = @"{ ""version"": ""1.0"", ""files"": {} }";
            
            // Setup local manifest
            var localDbDir = Path.Combine(_testTempDir, "Data", "db");
            Directory.CreateDirectory(localDbDir);
            File.WriteAllText(Path.Combine(localDbDir, "manifest.json"), manifestJson);

            _httpHandler.OnRequest = req => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson) };

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient);
            var progress = Substitute.For<IProgress<DataUpdateProgress>>();

            await service.CheckForAndApplyUpdatesAsync(progress, CancellationToken.None);

            _mockLogger.Received(1).LogInfo(Arg.Is<string>(s => s != null && s.Contains("up to date")), Arg.Any<string>());
        }

        [Fact]
        public void Constructor_WhenRulesDbMissingAndBundledExists_ShouldSeedFromBundledPlaceholders()
        {

            var bundledDir = Path.Combine(_testTempDir, "bundled", "Data", "db");
            Directory.CreateDirectory(bundledDir);
            File.WriteAllText(Path.Combine(bundledDir, "manifest.json"), "{}");
            File.WriteAllText(Path.Combine(bundledDir, "rules.json"), @"{ ""rules"": [] }");

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient, bundledDir);

            var seededDir = Path.Combine(_testTempDir, "Data", "db");
            Directory.Exists(seededDir).Should().BeTrue();
            File.Exists(Path.Combine(seededDir, "manifest.json")).Should().BeTrue();
            File.Exists(Path.Combine(seededDir, "rules.json")).Should().BeTrue();
            File.ReadAllText(Path.Combine(seededDir, "rules.json")).Should().Be(@"{ ""rules"": [] }");
        }

        [Fact]
        public void Constructor_WhenRulesDbAlreadyPopulated_ShouldNotOverwriteFromBundled()
        {

            var bundledDir = Path.Combine(_testTempDir, "bundled", "Data", "db");
            Directory.CreateDirectory(bundledDir);
            File.WriteAllText(Path.Combine(bundledDir, "rules.json"), @"{ ""rules"": [] }");

            var existingDbDir = Path.Combine(_testTempDir, "Data", "db");
            Directory.CreateDirectory(existingDbDir);
            File.WriteAllText(Path.Combine(existingDbDir, "rules.json"), @"{ ""rules"": [""existing""] }");

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient, bundledDir);

            File.ReadAllText(Path.Combine(existingDbDir, "rules.json")).Should().Be(@"{ ""rules"": [""existing""] }");
        }

        [Fact]
        public void Constructor_WhenBundledMissing_ShouldStillCreateEmptyRulesDb()
        {

            var missingBundled = Path.Combine(_testTempDir, "does-not-exist", "Data", "db");

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient, missingBundled);

            Directory.Exists(Path.Combine(_testTempDir, "Data", "db")).Should().BeTrue();
        }

        [Fact]
        public async Task CheckForAndApplyUpdatesAsync_ShouldSyncUseThisInsteadGzipAndStoreEtag()
        {
            var manifestJson = @"{ ""version"": ""1.0"", ""files"": {} }";
            var replacementsJson = @"{ ""rules"": [ { ""oldWorkshopId"": ""123"" } ] }";
            var compressed = Gzip(replacementsJson);

            _httpHandler.OnRequest = req =>
            {
                if (req.RequestUri!.ToString().EndsWith("manifest.json"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(manifestJson)
                    };
                }
                if (req.RequestUri!.ToString().EndsWith("replacements.json.gz"))
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(compressed)
                    };
                    response.Headers.ETag = EntityTagHeaderValue.Parse("\"abc123\"");
                    return response;
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            };

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient);
            var progress = Substitute.For<IProgress<DataUpdateProgress>>();

            await service.CheckForAndApplyUpdatesAsync(progress, CancellationToken.None);

            var snapshotPath = Path.Combine(_testTempDir, "Data", "Replacements", "UseThisInstead", "replacements.json");
            var etagPath = snapshotPath + ".etag";

            File.Exists(snapshotPath).Should().BeTrue();
            File.ReadAllText(snapshotPath).Should().Be(replacementsJson);
            File.Exists(etagPath).Should().BeTrue();
            File.ReadAllText(etagPath).Should().Contain("abc123");
        }

        [Fact]
        public async Task CheckForAndApplyUpdatesAsync_WhenUseThisInsteadEtagMatches_ShouldNotRewriteSnapshot()
        {
            var manifestJson = @"{ ""version"": ""1.0"", ""files"": {} }";
            var replacementsJson = @"{ ""rules"": [ { ""oldWorkshopId"": ""123"" } ] }";
            var compressed = Gzip(replacementsJson);

            var snapshotDir = Path.Combine(_testTempDir, "Data", "Replacements", "UseThisInstead");
            Directory.CreateDirectory(snapshotDir);
            File.WriteAllText(Path.Combine(snapshotDir, "replacements.json"), "previous-content");
            File.WriteAllText(Path.Combine(snapshotDir, "replacements.json") + ".etag", "\"abc123\"");

            _httpHandler.OnRequest = req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.EndsWith("manifest.json"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(manifestJson)
                    };
                }
                if (url.EndsWith("replacements.json.gz"))
                {
                    // Conditional request honored: unchanged source answers 304.
                    if (req.Headers.IfNoneMatch.Count > 0)
                    {
                        return new HttpResponseMessage(HttpStatusCode.NotModified);
                    }
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(compressed)
                    };
                    response.Headers.ETag = EntityTagHeaderValue.Parse("\"abc123\"");
                    return response;
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            };

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient);
            var progress = Substitute.For<IProgress<DataUpdateProgress>>();

            await service.CheckForAndApplyUpdatesAsync(progress, CancellationToken.None);

            File.ReadAllText(Path.Combine(snapshotDir, "replacements.json")).Should().Be("previous-content");
        }

        [Fact]
        public async Task CheckForAndApplyUpdatesAsync_WhenManifestUnchanged_ShouldStillSyncUseThisInstead()
        {
            var manifestJson = @"{ ""version"": ""1.0"", ""files"": {} }";

            // Local manifest identical to remote -> the curated flow short-circuits.
            var localDbDir = Path.Combine(_testTempDir, "Data", "db");
            Directory.CreateDirectory(localDbDir);
            File.WriteAllText(Path.Combine(localDbDir, "manifest.json"), manifestJson);

            var replacementsJson = @"{ ""rules"": [ { ""oldWorkshopId"": ""123"" } ] }";
            var compressed = Gzip(replacementsJson);

            _httpHandler.OnRequest = req =>
            {
                if (req.RequestUri!.ToString().EndsWith("replacements.json.gz"))
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(compressed)
                    };
                    response.Headers.ETag = EntityTagHeaderValue.Parse("\"abc123\"");
                    return response;
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson) };
            };

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient);
            var progress = Substitute.For<IProgress<DataUpdateProgress>>();

            await service.CheckForAndApplyUpdatesAsync(progress, CancellationToken.None);

            var snapshotPath = Path.Combine(_testTempDir, "Data", "Replacements", "UseThisInstead", "replacements.json");
            File.Exists(snapshotPath).Should().BeTrue("the external sync must run even when the curated manifest is unchanged");
            File.ReadAllText(snapshotPath).Should().Be(replacementsJson);
        }

        [Fact]
        public async Task CheckForAndApplyUpdatesAsync_WhenCuratedSyncFails_ShouldStillSyncUseThisInstead()
        {
            // Curated endpoint unreachable (e.g. private repository) -> the
            // curated flow throws, but external community sources must still sync.
            var replacementsJson = @"{ ""rules"": [ { ""oldWorkshopId"": ""123"" } ] }";
            var compressed = Gzip(replacementsJson);

            _httpHandler.OnRequest = req =>
            {
                if (req.RequestUri!.ToString().EndsWith("replacements.json.gz"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(compressed)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            };

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient);
            var progress = Substitute.For<IProgress<DataUpdateProgress>>();

            Func<Task> act = () => service.CheckForAndApplyUpdatesAsync(progress, CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>();
            var snapshotPath = Path.Combine(_testTempDir, "Data", "Replacements", "UseThisInstead", "replacements.json");
            File.Exists(snapshotPath).Should().BeTrue("a curated failure must not skip the external sync");
            File.ReadAllText(snapshotPath).Should().Be(replacementsJson);
        }

        [Fact]
        public async Task CheckForAndApplyUpdatesAsync_ShouldSyncNoVersionWarningFromDiscoveredTree()
        {
            var manifestJson = @"{ ""version"": ""1.0"", ""files"": {} }";
            var treeJson = @"{ ""tree"": [
                { ""path"": ""1.4"", ""type"": ""tree"" },
                { ""path"": ""1.6"", ""type"": ""tree"" },
                { ""path"": ""About"", ""type"": ""tree"" },
                { ""path"": ""README.md"", ""type"": ""blob"" }
            ] }";

            _httpHandler.OnRequest = req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.EndsWith("trees/main"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(treeJson) };
                }
                if (url.Contains("NoVersionWarning/main/1.4/"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<ModIdsToFix><li>mod.a</li></ModIdsToFix>") };
                }
                if (url.Contains("NoVersionWarning/main/1.6/"))
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<ModIdsToFix><li>mod.b</li></ModIdsToFix>") };
                    response.Headers.ETag = EntityTagHeaderValue.Parse("\"nvw16\"");
                    return response;
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson) };
            };

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient);
            var progress = Substitute.For<IProgress<DataUpdateProgress>>();

            await service.CheckForAndApplyUpdatesAsync(progress, CancellationToken.None);

            var dir = Path.Combine(_testTempDir, "Data", "Versions", "NoVersionWarning");
            File.Exists(Path.Combine(dir, "1.4", "ModIdsToFix.xml")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "1.6", "ModIdsToFix.xml")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "1.6", "ModIdsToFix.xml") + ".etag").Should().BeTrue();
            // Non-version entries in the tree must not become fetches.
            File.Exists(Path.Combine(dir, "About")).Should().BeFalse();
        }

        [Fact]
        public async Task CheckForAndApplyUpdatesAsync_WhenTreeDiscoveryFails_ShouldUseFallbackVersions()
        {
            var manifestJson = @"{ ""version"": ""1.0"", ""files"": {} }";
            var fetchedVersions = new List<string>();

            _httpHandler.OnRequest = req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.EndsWith("trees/main"))
                {
                    // Discovery unreachable (e.g. rate limited).
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }
                var match = System.Text.RegularExpressions.Regex.Match(url, @"NoVersionWarning/main/(\d+\.\d+)/");
                if (match.Success)
                {
                    fetchedVersions.Add(match.Groups[1].Value);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<ModIdsToFix></ModIdsToFix>") };
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson) };
            };

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient);
            var progress = Substitute.For<IProgress<DataUpdateProgress>>();

            await service.CheckForAndApplyUpdatesAsync(progress, CancellationToken.None);

            var dir = Path.Combine(_testTempDir, "Data", "Versions", "NoVersionWarning");
            File.Exists(Path.Combine(dir, "1.5", "ModIdsToFix.xml")).Should().BeTrue("the fallback list must still fetch when discovery fails");
            fetchedVersions.Should().Contain(new[] { "1.3", "1.4", "1.5", "1.6", "1.7" });
        }

        [Fact]
        public async Task CheckForAndApplyUpdatesAsync_GameAndModNewerThanSeed_ShouldExtendOfflineCandidates()
        {
            // Game on 1.9, installed NoVersionWarning mod already has 1.8:
            // the offline candidate list must reach both, seed never truncates.
            var gameDir = Path.Combine(_testTempDir, "game");
            Directory.CreateDirectory(gameDir);
            File.WriteAllText(Path.Combine(gameDir, "Version.txt"), "1.9.100 rev1");

            var modsDir = Path.Combine(_testTempDir, "mods");
            Directory.CreateDirectory(Path.Combine(modsDir, "2599504692", "1.8"));

            var gameInstance = new GameInstance { GameFolder = gameDir, LocalFolder = modsDir };

            var manifestJson = @"{ ""version"": ""1.0"", ""files"": {} }";
            _httpHandler.OnRequest = req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.EndsWith("trees/main"))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }
                if (url.Contains("NoVersionWarning/main/1.9/"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<ModIdsToFix><li>mod.game19</li></ModIdsToFix>") };
                }
                if (url.Contains("NoVersionWarning/main/1.8/"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<ModIdsToFix><li>mod.mod18</li></ModIdsToFix>") };
                }
                // 1.3-1.7 not served: must be logged and skipped, not fatal.
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson) };
            };

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient, gameInstance);
            var progress = Substitute.For<IProgress<DataUpdateProgress>>();

            await service.CheckForAndApplyUpdatesAsync(progress, CancellationToken.None);

            var dir = Path.Combine(_testTempDir, "Data", "Versions", "NoVersionWarning");
            File.Exists(Path.Combine(dir, "1.9", "ModIdsToFix.xml")).Should().BeTrue("a game major above the seed must extend the candidates");
            File.Exists(Path.Combine(dir, "1.8", "ModIdsToFix.xml")).Should().BeTrue("the installed mod's version dirs must extend the candidates");
        }

        private static byte[] Gzip(string content)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
            using (var writer = new StreamWriter(gzip))
            {
                writer.Write(content);
            }
            return output.ToArray();
        }

        private class MockHttpMessageHandler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage>? OnRequest { get; set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (OnRequest == null) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                return Task.FromResult(OnRequest(request));
            }
        }
    }
}

