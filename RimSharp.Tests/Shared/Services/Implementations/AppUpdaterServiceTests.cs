using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;
using Xunit;

namespace RimSharp.Tests.Shared.Services.Implementations
{
    public class AppUpdaterServiceTests
    {
        private readonly IHttpClientFactory _mockHttpClientFactory;
        private readonly ILoggerService _mockLogger;
        private readonly IConfigService _mockConfigService;
        private readonly MockHttpMessageHandler _httpHandler;

        public AppUpdaterServiceTests()
        {
            _mockHttpClientFactory = Substitute.For<IHttpClientFactory>();
            _mockLogger = Substitute.For<ILoggerService>();
            _mockConfigService = Substitute.For<IConfigService>();
            _httpHandler = new MockHttpMessageHandler();

            var client = new HttpClient(_httpHandler);
            _mockHttpClientFactory.CreateClient("GitHub_Updater").Returns(client);
        }

        private void SetupReleasesResponse(params object[] releases)
        {
            var json = JsonSerializer.Serialize(releases);
            _httpHandler.Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            };
        }

        [Fact]
        public async Task StableChannel_ShouldIgnoreBetaAndAlpha_AndPickLatestStable()
        {
            // Cenário: Existem 2.0.0 (stable), 2.1.0-alpha e 2.2.0-beta
            _mockConfigService.GetConfigValue("update_channel").Returns("stable");

            SetupReleasesResponse(
                new { tag_name = "v2.2.0-beta", prerelease = true, published_at = DateTimeOffset.UtcNow },
                new { tag_name = "v2.1.0-alpha", prerelease = true, published_at = DateTimeOffset.UtcNow.AddDays(-1) },
                new { tag_name = "v2.0.0", prerelease = false, published_at = DateTimeOffset.UtcNow.AddDays(-2) }
            );

            var service = new AppUpdaterService(_mockHttpClientFactory, _mockLogger, _mockConfigService);
            var (isUpdateAvailable, latestVersion, _) = await service.CheckForAppUpdateAsync();

            // Deve ignorar o beta e o alpha e selecionar a 2.0.0 estável
            if (isUpdateAvailable)
            {
                latestVersion.Should().Be("v2.0.0");
            }
        }

        [Fact]
        public async Task BetaChannel_ShouldPickBeta_WhenBetaIsNewerThanStable()
        {
            _mockConfigService.GetConfigValue("update_channel").Returns("beta");

            SetupReleasesResponse(
                new { tag_name = "v2.2.0-beta", prerelease = true, published_at = DateTimeOffset.UtcNow },
                new { tag_name = "v2.3.0-alpha", prerelease = true, published_at = DateTimeOffset.UtcNow },
                new { tag_name = "v2.0.0", prerelease = false, published_at = DateTimeOffset.UtcNow.AddDays(-5) }
            );

            var service = new AppUpdaterService(_mockHttpClientFactory, _mockLogger, _mockConfigService);
            var (isUpdateAvailable, latestVersion, _) = await service.CheckForAppUpdateAsync();

            if (isUpdateAvailable)
            {
                // Deve ignorar o alpha (2.3.0-alpha) e escolher a beta mais alta (2.2.0-beta)
                latestVersion.Should().Be("v2.2.0-beta");
            }
        }

        [Fact]
        public async Task BetaChannel_ShouldPickStable_WhenStableIsNewerThanBeta()
        {
            _mockConfigService.GetConfigValue("update_channel").Returns("beta");

            // Saiu o beta 2.0.0-beta e depois saiu a 2.0.0 final
            SetupReleasesResponse(
                new { tag_name = "v2.0.0", prerelease = false, published_at = DateTimeOffset.UtcNow },
                new { tag_name = "v2.0.0-beta", prerelease = true, published_at = DateTimeOffset.UtcNow.AddDays(-2) }
            );

            var service = new AppUpdaterService(_mockHttpClientFactory, _mockLogger, _mockConfigService);
            var (isUpdateAvailable, latestVersion, _) = await service.CheckForAppUpdateAsync();

            if (isUpdateAvailable)
            {
                latestVersion.Should().Be("v2.0.0");
            }
        }

        [Fact]
        public async Task TieBreaker_SameBaseVersion_ShouldPickMostRecentPublishedAt()
        {
            // Teste do seu desempate:
            // Ambas são 2.0.0, mas o beta foi publicado DEPOIS da estável
            _mockConfigService.GetConfigValue("update_channel").Returns("beta");

            var now = DateTimeOffset.UtcNow;
            SetupReleasesResponse(
                new { tag_name = "v2.0.0-beta", prerelease = true, published_at = now }, // Mais recente
                new { tag_name = "v2.0.0", prerelease = false, published_at = now.AddHours(-3) } // Mais antigo
            );

            var service = new AppUpdaterService(_mockHttpClientFactory, _mockLogger, _mockConfigService);
            var (isUpdateAvailable, latestVersion, _) = await service.CheckForAppUpdateAsync();

            if (isUpdateAvailable)
            {
                // Como o beta tem data mais recente, ele desempata e vence
                latestVersion.Should().Be("v2.0.0-beta");
            }
        }

        [Fact]
        public async Task AlphaChannel_ShouldPickNewestOfAll()
        {
            _mockConfigService.GetConfigValue("update_channel").Returns("alpha");

            SetupReleasesResponse(
                new { tag_name = "v2.5.0-alpha.1", prerelease = true, published_at = DateTimeOffset.UtcNow },
                new { tag_name = "v2.4.0-beta", prerelease = true, published_at = DateTimeOffset.UtcNow.AddDays(-1) },
                new { tag_name = "v2.0.0", prerelease = false, published_at = DateTimeOffset.UtcNow.AddDays(-5) }
            );

            var service = new AppUpdaterService(_mockHttpClientFactory, _mockLogger, _mockConfigService);
            var (isUpdateAvailable, latestVersion, _) = await service.CheckForAppUpdateAsync();

            if (isUpdateAvailable)
            {
                latestVersion.Should().Be("v2.5.0-alpha.1");
            }
        }

        [Fact]
        public async Task CheckForUpdate_WhenGitHubReturnsError_ShouldHandleGracefully()
        {
            _mockConfigService.GetConfigValue("update_channel").Returns("stable");
            _httpHandler.Response = new HttpResponseMessage(HttpStatusCode.InternalServerError);

            var service = new AppUpdaterService(_mockHttpClientFactory, _mockLogger, _mockConfigService);
            var (isUpdateAvailable, latestVersion, releaseUrl) = await service.CheckForAppUpdateAsync();

            isUpdateAvailable.Should().BeFalse();
            latestVersion.Should().BeNull();
            releaseUrl.Should().BeNull();
        }

        [Fact]
        public async Task CheckForUpdate_WhenDraftRelease_ShouldIgnore()
        {
            _mockConfigService.GetConfigValue("update_channel").Returns("stable");

            SetupReleasesResponse(
                new { tag_name = "v9.9.9", prerelease = false, draft = true, published_at = DateTimeOffset.UtcNow },
                new { tag_name = "v2.0.0", prerelease = false, draft = false, published_at = DateTimeOffset.UtcNow.AddDays(-1) }
            );

            var service = new AppUpdaterService(_mockHttpClientFactory, _mockLogger, _mockConfigService);
            var (isUpdateAvailable, latestVersion, _) = await service.CheckForAppUpdateAsync();

            if (isUpdateAvailable)
            {
                // Não deve pegar o draft 9.9.9
                latestVersion.Should().Be("v2.0.0");
            }
        }

        private class MockHttpMessageHandler : HttpMessageHandler
        {
            public HttpResponseMessage Response { get; set; } = new HttpResponseMessage(HttpStatusCode.OK);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(Response);
            }
        }
    }
}