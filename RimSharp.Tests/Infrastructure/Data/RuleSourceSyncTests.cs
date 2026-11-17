using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Data;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Data
{
    /// <summary>
    /// Rule source registry sync: conditional GET (ETag/304), per-source
    /// failure isolation, localFile sources never fetched, and the curated
    /// RimSharpDB entry never fetched by the registry sync.
    /// </summary>
    public class RuleSourceSyncTests : IDisposable
    {
        private readonly ILoggerService _mockLogger;
        private readonly string _testTempDir;
        private readonly MockHttpMessageHandler _httpHandler;
        private readonly HttpClient _httpClient;
        private readonly string _configPath;
        private readonly ModRuleSourceRegistry _registry;

        public RuleSourceSyncTests()
        {
            _mockLogger = Substitute.For<ILoggerService>();
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharp_RuleSourceSync_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);

            _httpHandler = new MockHttpMessageHandler { OnRequest = _ => new HttpResponseMessage(HttpStatusCode.NotFound) };
            _httpClient = new HttpClient(_httpHandler);

            _configPath = Path.Combine(_testTempDir, "config.cfg");
            var configService = new ConfigService(_configPath);
            _registry = new ModRuleSourceRegistry(configService);
            _registry.ReplaceAll(new[]
            {
                new ModRuleSourceRecord
                {
                    Id = ModRuleSourceRegistry.CuratedSourceId,
                    Name = "Curated",
                    Type = ModRuleSourceType.RepoFile,
                    Enabled = true
                },
                new ModRuleSourceRecord
                {
                    Id = ModRuleSourceRegistry.CommunitySourceId,
                    Name = "Community",
                    Type = ModRuleSourceType.RepoFile,
                    Url = "https://example.invalid/communityRules.json",
                    Enabled = true
                },
                new ModRuleSourceRecord
                {
                    Id = "second-source",
                    Name = "Second",
                    Type = ModRuleSourceType.RepoFile,
                    Url = "https://example.invalid/secondRules.json",
                    Enabled = true
                },
                new ModRuleSourceRecord
                {
                    Id = "local-source",
                    Name = "Local",
                    Type = ModRuleSourceType.LocalFile,
                    LocalPath = Path.Combine(_testTempDir, "local.json"),
                    Enabled = true
                }
            });
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, recursive: true);
            }
        }

        private DataUpdateService CreateService() =>
            new(_mockLogger, _testTempDir, _httpClient, _testTempDir, ruleSourceRegistry: _registry);

        private string CachePath(string sourceId, string fileName) =>
            Path.Combine(_testTempDir, "Data", "Rules", sourceId, fileName);

        [Fact]
        public async Task SyncRuleSourcesAsync_WhenServerReturns200_ShouldWriteCacheAndEtag()
        {
            var communityJson = @"{ ""timestamp"": 1789239946, ""rules"": { ""mod.a"": {} } }";

            _httpHandler.OnRequest = req =>
            {
                if (req.RequestUri!.ToString().EndsWith("communityRules.json"))
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(communityJson)
                    };
                    response.Headers.ETag = EntityTagHeaderValue.Parse("\"comm1\"");
                    return response;
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            };

            await CreateService().SyncRuleSourcesAsync(null, CancellationToken.None);

            var cachePath = CachePath(ModRuleSourceRegistry.CommunitySourceId, "communityRules.json");
            File.Exists(cachePath).Should().BeTrue();
            File.ReadAllText(cachePath).Should().Be(communityJson);
            File.Exists(cachePath + ".etag").Should().BeTrue();
            File.ReadAllText(cachePath + ".etag").Should().Contain("comm1");
        }

        [Fact]
        public async Task SyncRuleSourcesAsync_WhenEtagMatches_ShouldNotRewriteCache()
        {
            var cachePath = CachePath(ModRuleSourceRegistry.CommunitySourceId, "communityRules.json");
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllText(cachePath, "previous-content");
            File.WriteAllText(cachePath + ".etag", "\"comm1\"");

            _httpHandler.OnRequest = req =>
            {
                if (req.RequestUri!.ToString().EndsWith("communityRules.json"))
                {
                    // Conditional request honored: unchanged source answers 304.
                    if (req.Headers.IfNoneMatch.Count > 0)
                    {
                        return new HttpResponseMessage(HttpStatusCode.NotModified);
                    }
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("new-content")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            };

            await CreateService().SyncRuleSourcesAsync(null, CancellationToken.None);

            File.ReadAllText(cachePath).Should().Be("previous-content");
        }

        [Fact]
        public async Task SyncRuleSourcesAsync_ShouldIsolatePerSourceFailures()
        {
            _httpHandler.OnRequest = req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.EndsWith("communityRules.json"))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound); // dead source
                }
                if (url.EndsWith("secondRules.json"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{ ""rules"": {} }")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            };

            await CreateService().SyncRuleSourcesAsync(null, CancellationToken.None);

            File.Exists(CachePath("second-source", "secondRules.json")).Should().BeTrue();
            File.Exists(CachePath(ModRuleSourceRegistry.CommunitySourceId, "communityRules.json")).Should().BeFalse();
        }

        [Fact]
        public async Task SyncRuleSourcesAsync_ShouldNeverFetchTheCuratedSource()
        {
            var requestedUrls = new List<string>();
            _httpHandler.OnRequest = req =>
            {
                requestedUrls.Add(req.RequestUri!.ToString());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""rules"": {} }")
                };
            };

            await CreateService().SyncRuleSourcesAsync(null, CancellationToken.None);

            // The curated file is owned by the SHA-gated sync; the registry sync
            // must not request it (nor write to its folder).
            requestedUrls.Should().NotContain(u => u.Contains("RimSharp", StringComparison.OrdinalIgnoreCase));
            File.Exists(CachePath(ModRuleSourceRegistry.CuratedSourceId, "rules.json")).Should().BeFalse();
        }

        [Fact]
        public async Task SyncRuleSourcesAsync_ShouldNotFetchLocalFileSources()
        {
            var requestedUrls = new List<string>();
            _httpHandler.OnRequest = req =>
            {
                requestedUrls.Add(req.RequestUri!.ToString());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""rules"": {} }")
                };
            };

            await CreateService().SyncRuleSourcesAsync(null, CancellationToken.None);

            requestedUrls.Should().NotContain(u => u.Contains("local", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task CheckForAndApplyUpdatesAsync_ShouldSyncRuleSourcesFromTheRegistry()
        {
            var manifestJson = @"{ ""version"": ""1.0"", ""files"": {} }";
            var communityJson = @"{ ""timestamp"": 1789239946, ""rules"": { ""mod.a"": {} } }";

            _httpHandler.OnRequest = req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.EndsWith("manifest.json"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson) };
                }
                if (url.EndsWith("communityRules.json"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(communityJson) };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            };

            var progress = Substitute.For<IProgress<DataUpdateProgress>>();
            await CreateService().CheckForAndApplyUpdatesAsync(progress, CancellationToken.None);

            File.ReadAllText(CachePath(ModRuleSourceRegistry.CommunitySourceId, "communityRules.json"))
                .Should().Be(communityJson);
        }

        [Fact]
        public async Task RefreshRuleSourceAsync_ShouldFetchOnlyTheRequestedSource()
        {
            var requestedUrls = new List<string>();
            _httpHandler.OnRequest = req =>
            {
                requestedUrls.Add(req.RequestUri!.ToString());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""rules"": {} }")
                };
            };

            await CreateService().RefreshRuleSourceAsync("second-source", CancellationToken.None);

            requestedUrls.Should().Contain(u => u.EndsWith("secondRules.json"));
            requestedUrls.Should().NotContain(u => u.EndsWith("communityRules.json"));
        }

        [Fact]
        public async Task RefreshAllRuleSourcesAsync_ShouldFetchEveryEnabledRepoFileSource()
        {
            var requestedUrls = new List<string>();
            _httpHandler.OnRequest = req =>
            {
                requestedUrls.Add(req.RequestUri!.ToString());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""rules"": {} }")
                };
            };

            await CreateService().RefreshAllRuleSourcesAsync(CancellationToken.None);

            requestedUrls.Should().Contain(u => u.EndsWith("communityRules.json"));
            requestedUrls.Should().Contain(u => u.EndsWith("secondRules.json"));
        }

        [Fact]
        public async Task RefreshRuleSourceAsync_WhenSourceFails_ShouldThrowIsolatedError()
        {
            _httpHandler.OnRequest = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

            Func<Task> act = () => CreateService().RefreshRuleSourceAsync(
                ModRuleSourceRegistry.CommunitySourceId, CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>();
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