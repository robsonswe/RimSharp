using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
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
        public async Task CheckForAndApplyUpdatesAsync_WhenFilesMissing_ShouldDownloadAllIntoMappedDestinations()
        {

            var rulesJson = @"{ ""rules"": [] }";
            var replacementsJson = @"{ ""mods"": {} }";
            var dbJson = @"{ ""mods"": {} }";
            var manifestJson = BuildManifest(rulesJson, replacementsJson, dbJson);

            _httpHandler.OnRequest = req =>
            {
                var uri = req.RequestUri?.ToString() ?? "";
                if (uri.EndsWith("manifest.json"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson) };
                if (uri.EndsWith("db/rules.json"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(rulesJson) };
                if (uri.EndsWith("db/replacements.json"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(replacementsJson) };
                if (uri.EndsWith("db/db.json"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(dbJson) };
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            };

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient);
            var progress = Substitute.For<IProgress<DataUpdateProgress>>();

            await service.CheckForAndApplyUpdatesAsync(progress, CancellationToken.None);

            var rulesPath = Path.Combine(_testTempDir, "Data", "Rules", "RimSharp", "rules.json");
            var replacementsPath = Path.Combine(_testTempDir, "Data", "Replacements", "RimSharp", "replacements.json");
            var dbPath = Path.Combine(_testTempDir, "Data", "ModInfo", "RimSharp", "db.json");

            File.Exists(rulesPath).Should().BeTrue();
            File.ReadAllText(rulesPath).Should().Be(rulesJson);
            File.Exists(replacementsPath).Should().BeTrue();
            File.ReadAllText(replacementsPath).Should().Be(replacementsJson);
            File.Exists(dbPath).Should().BeTrue();
            File.ReadAllText(dbPath).Should().Be(dbJson);

            // The local manifest is abolished: it must never be written anywhere.
            Directory.Exists(Path.Combine(_testTempDir, "Data", "db")).Should().BeFalse();
        }

        [Fact]
        public async Task CheckForAndApplyUpdatesAsync_WhenAllHashesMatch_ShouldNotDownloadAnything()
        {

            var rulesJson = @"{ ""rules"": [] }";
            var replacementsJson = @"{ ""mods"": {} }";
            var dbJson = @"{ ""mods"": {} }";
            var manifestJson = BuildManifest(rulesJson, replacementsJson, dbJson);

            // Pre-write the three destinations with content matching the manifest SHAs.
            WriteCuratedFile("Rules", rulesJson);
            WriteCuratedFile("Replacements", replacementsJson);
            WriteCuratedFile("ModInfo", dbJson);

            var requestedUrls = new List<string>();
            _httpHandler.OnRequest = req =>
            {
                var uri = req.RequestUri?.ToString() ?? "";
                requestedUrls.Add(uri);
                if (uri.EndsWith("manifest.json"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson) };
                // Any curated payload request would be a bug: everything matches.
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            };

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient);
            var progress = Substitute.For<IProgress<DataUpdateProgress>>();

            await service.CheckForAndApplyUpdatesAsync(progress, CancellationToken.None);

            File.ReadAllText(CuratedPath("Rules")).Should().Be(rulesJson);
            File.ReadAllText(CuratedPath("Replacements")).Should().Be(replacementsJson);
            File.ReadAllText(CuratedPath("ModInfo")).Should().Be(dbJson);
            requestedUrls.Should().NotContain(u => u.EndsWith("db/rules.json"));
            requestedUrls.Should().NotContain(u => u.EndsWith("db/replacements.json"));
            requestedUrls.Should().NotContain(u => u.EndsWith("db/db.json"));
        }

        [Fact]
        public async Task CheckForAndApplyUpdatesAsync_WhenOneHashMismatches_ShouldDownloadOnlyThatFile()
        {

            var rulesJson = @"{ ""rules"": [] }";
            var replacementsJson = @"{ ""mods"": {} }";
            var staleReplacementsJson = @"{ ""mods"": { ""stale"": {} } }";
            var dbJson = @"{ ""mods"": {} }";
            var manifestJson = BuildManifest(rulesJson, replacementsJson, dbJson);

            // rules and db already match; replacements holds stale content.
            WriteCuratedFile("Rules", rulesJson);
            WriteCuratedFile("ModInfo", dbJson);
            WriteCuratedFile("Replacements", staleReplacementsJson);

            var requestedUrls = new List<string>();
            _httpHandler.OnRequest = req =>
            {
                var uri = req.RequestUri?.ToString() ?? "";
                requestedUrls.Add(uri);
                if (uri.EndsWith("manifest.json"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson) };
                if (uri.EndsWith("db/replacements.json"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(replacementsJson) };
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            };

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient);
            var progress = Substitute.For<IProgress<DataUpdateProgress>>();

            await service.CheckForAndApplyUpdatesAsync(progress, CancellationToken.None);

            File.ReadAllText(CuratedPath("Replacements")).Should().Be(replacementsJson);
            File.ReadAllText(CuratedPath("Rules")).Should().Be(rulesJson);
            File.ReadAllText(CuratedPath("ModInfo")).Should().Be(dbJson);
            requestedUrls.Should().Contain(u => u.EndsWith("db/replacements.json"));
            requestedUrls.Should().NotContain(u => u.EndsWith("db/rules.json"));
            requestedUrls.Should().NotContain(u => u.EndsWith("db/db.json"));
        }

        [Fact]
        public async Task CheckForAndApplyUpdatesAsync_WhenManifestHasUnknownKey_ShouldSkipIt()
        {

            var rulesJson = @"{ ""rules"": [] }";
            var manifestJson = @"{
                ""version"": ""1.0"",
                ""files"": {
                    ""rules"": { ""path"": ""db/rules.json"", ""sha"": """ + Sha256Hex(rulesJson) + @""" },
                    ""futureThing"": { ""path"": ""db/future.json"", ""sha"": ""deadbeef"" }
                }
            }";

            var requestedUrls = new List<string>();
            _httpHandler.OnRequest = req =>
            {
                var uri = req.RequestUri?.ToString() ?? "";
                requestedUrls.Add(uri);
                if (uri.EndsWith("manifest.json"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson) };
                if (uri.EndsWith("db/rules.json"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(rulesJson) };
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            };

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient);
            var progress = Substitute.For<IProgress<DataUpdateProgress>>();

            // An unmapped entry must be skipped, not fatal.
            await service.CheckForAndApplyUpdatesAsync(progress, CancellationToken.None);

            File.ReadAllText(CuratedPath("Rules")).Should().Be(rulesJson);
            requestedUrls.Should().NotContain(u => u.EndsWith("db/future.json"));
            _mockLogger.Received().LogWarning(
                Arg.Is<string>(s => s != null && s.Contains("Unknown curated manifest entry")),
                Arg.Any<string>());
        }

        [Fact]
        public void Constructor_WhenCuratedDataMissingAndBundledExists_ShouldSeedFromBundledPlaceholders()
        {

            var bundledRoot = Path.Combine(_testTempDir, "bundled");
            WriteBundledFile(bundledRoot, "ModInfo", @"{ ""mods"": {} }");
            WriteBundledFile(bundledRoot, "Rules", @"{ ""rules"": [] }");
            WriteBundledFile(bundledRoot, "Replacements", @"{ ""mods"": {} }");

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient, bundledRoot);

            File.Exists(CuratedPath("Rules")).Should().BeTrue();
            File.ReadAllText(CuratedPath("Rules")).Should().Be(@"{ ""rules"": [] }");
            File.Exists(CuratedPath("Replacements")).Should().BeTrue();
            File.Exists(CuratedPath("ModInfo")).Should().BeTrue();
        }

        [Fact]
        public void Constructor_WhenCuratedDataExists_ShouldNotOverwriteFromBundled()
        {

            var bundledRoot = Path.Combine(_testTempDir, "bundled");
            WriteBundledFile(bundledRoot, "Rules", @"{ ""rules"": [] }");

            WriteCuratedFile("Rules", @"{ ""rules"": [""existing""] }");

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient, bundledRoot);

            File.ReadAllText(CuratedPath("Rules")).Should().Be(@"{ ""rules"": [""existing""] }");
        }

        [Fact]
        public void Constructor_WhenBundledMissing_ShouldNotSeedAnything()
        {

            var missingBundled = Path.Combine(_testTempDir, "does-not-exist");

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient, missingBundled);

            File.Exists(CuratedPath("Rules")).Should().BeFalse();
            File.Exists(CuratedPath("Replacements")).Should().BeFalse();
            File.Exists(CuratedPath("ModInfo")).Should().BeFalse();
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
        public async Task CheckForAndApplyUpdatesAsync_WhenCuratedFilesUpToDate_ShouldStillSyncUseThisInstead()
        {
            var manifestJson = @"{ ""version"": ""1.0"", ""files"": {} }";

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

        private static string Sha256Hex(string content)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        }

        /// <summary>Builds a remote manifest listing all three curated files.</summary>
        private static string BuildManifest(string rulesJson, string replacementsJson, string dbJson)
        {
            return @"{
                ""version"": ""1.0"",
                ""files"": {
                    ""rules"": { ""path"": ""db/rules.json"", ""sha"": """ + Sha256Hex(rulesJson) + @""" },
                    ""replacements"": { ""path"": ""db/replacements.json"", ""sha"": """ + Sha256Hex(replacementsJson) + @""" },
                    ""dictionary"": { ""path"": ""db/db.json"", ""sha"": """ + Sha256Hex(dbJson) + @""" }
                }
            }";
        }

        /// <summary>Runtime destination of a curated file under the test data root.</summary>
        private string CuratedPath(string category)
        {
            var fileName = category == "Rules" ? "rules.json" : category == "Replacements" ? "replacements.json" : "db.json";
            return Path.Combine(_testTempDir, "Data", category, "RimSharp", fileName);
        }

        private void WriteCuratedFile(string category, string content)
        {
            var path = CuratedPath(category);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        /// <summary>Writes a bundled placeholder mirroring the runtime layout.</summary>
        private void WriteBundledFile(string bundledRoot, string category, string content)
        {
            var fileName = category == "Rules" ? "rules.json" : category == "Replacements" ? "replacements.json" : "db.json";
            var path = Path.Combine(bundledRoot, "Data", category, "RimSharp", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
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

