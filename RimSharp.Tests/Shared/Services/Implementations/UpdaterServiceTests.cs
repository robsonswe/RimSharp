using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;
using Xunit;

namespace RimSharp.Tests.Shared.Services.Implementations
{
    public class UpdaterServiceTests
    {
        private readonly IHttpClientFactory _mockHttpClientFactory;
        private readonly IPathService _mockPathService;
        private readonly ILoggerService _mockLogger;
        private readonly MockHttpMessageHandler _handler;

        public UpdaterServiceTests()
        {
            _mockHttpClientFactory = Substitute.For<IHttpClientFactory>();
            _mockPathService = Substitute.For<IPathService>();
            _mockLogger = Substitute.For<ILoggerService>();
            _handler = new MockHttpMessageHandler();
            
            var client = new HttpClient(_handler);
            _mockHttpClientFactory.CreateClient("GOG_Updater").Returns(client);
        }

        [Theory]
        [InlineData("1.5.1234 rev99", 1, 5, 1234, 0)]
        [InlineData("1.4.3529", 1, 4, 3529, 0)]
        [InlineData("1.5", 1, 5, 0, 0)]
        public async Task ParseVersion_ThroughUpdateCheck_ShouldWork(string currentVersion, int major, int minor, int build, int revision)
        {
             // Arrange
            _mockPathService.GetGameVersion().Returns(currentVersion);
            
            // We want to see if it correctly compares against a slightly higher version
            var higherVersion = new Version(major, minor, build, revision + 1).ToString();

            var jsonResponse = $@"{{
                ""items"": [
                    {{ ""version_name"": ""{higherVersion}"", ""public"": true }}
                ]
            }}";
            _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(jsonResponse) };

            var service = new UpdaterService(_mockHttpClientFactory, _mockPathService, _mockLogger);

            // Act
            var (isUpdateAvailable, latestVersion) = await service.CheckForUpdateAsync();

            // Assert
            isUpdateAvailable.Should().BeTrue();
            latestVersion.Should().Be(higherVersion);
        }

        [Fact]
        public async Task CheckForUpdateAsync_WhenUpdateAvailable_ShouldReturnTrue()
        {
            // Arrange
            _mockPathService.GetGameVersion().Returns("1.4.3529");
            
            var jsonResponse = @"{
                ""items"": [
                    { ""version_name"": ""1.5.1234"", ""public"": true },
                    { ""version_name"": ""1.4.3529"", ""public"": true }
                ]
            }";
            _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(jsonResponse) };

            var service = new UpdaterService(_mockHttpClientFactory, _mockPathService, _mockLogger);

            // Act
            var (isUpdateAvailable, latestVersion) = await service.CheckForUpdateAsync();

            // Assert
            isUpdateAvailable.Should().BeTrue();
            latestVersion.Should().Be("1.5.1234");
        }

        [Fact]
        public async Task CheckForUpdateAsync_WhenUpToDate_ShouldReturnFalse()
        {
            // Arrange
            _mockPathService.GetGameVersion().Returns("1.5.1234");
            
            var jsonResponse = @"{
                ""items"": [
                    { ""version_name"": ""1.5.1234"", ""public"": true }
                ]
            }";
            _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(jsonResponse) };

            var service = new UpdaterService(_mockHttpClientFactory, _mockPathService, _mockLogger);

            // Act
            var (isUpdateAvailable, latestVersion) = await service.CheckForUpdateAsync();

            // Assert
            isUpdateAvailable.Should().BeFalse();
            latestVersion.Should().BeNull();
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
