using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Data;
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
            // Arrange
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

            // Act
            await service.CheckForAndApplyUpdatesAsync(progress, CancellationToken.None);

            // Assert
            File.Exists(Path.Combine(_testTempDir, "Rules", "db", "manifest.json")).Should().BeTrue();
            File.Exists(Path.Combine(_testTempDir, "Rules", "db", "rules.json")).Should().BeTrue();
            File.ReadAllText(Path.Combine(_testTempDir, "Rules", "db", "rules.json")).Should().Be(rulesJson);
        }

        [Fact]
        public async Task CheckForAndApplyUpdatesAsync_WhenManifestMatches_ShouldSkipDownload()
        {
            // Arrange
            var manifestJson = @"{ ""version"": ""1.0"", ""files"": {} }";
            
            // Setup local manifest
            var localDbDir = Path.Combine(_testTempDir, "Rules", "db");
            Directory.CreateDirectory(localDbDir);
            File.WriteAllText(Path.Combine(localDbDir, "manifest.json"), manifestJson);

            _httpHandler.OnRequest = req => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson) };

            var service = new DataUpdateService(_mockLogger, _testTempDir, _httpClient);
            var progress = Substitute.For<IProgress<DataUpdateProgress>>();

            // Act
            await service.CheckForAndApplyUpdatesAsync(progress, CancellationToken.None);

            // Assert
            _mockLogger.Received(1).LogInfo(Arg.Is<string>(s => s != null && s.Contains("up to date")), Arg.Any<string>());
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
