using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Workshop.Core;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Workshop.Core
{
    public class SteamCmdInstallerTests : IDisposable
    {
        private readonly ISteamCmdPathService _mockPathService;
        private readonly IPathService _mockGamePathService;
        private readonly IDialogService _mockDialogService;
        private readonly IHttpClientFactory _mockHttpClientFactory;
        private readonly string _testTempDir;
        private readonly MockHttpMessageHandler _httpHandler;

        public SteamCmdInstallerTests()
        {
            _mockPathService = Substitute.For<ISteamCmdPathService>();
            _mockGamePathService = Substitute.For<IPathService>();
            _mockDialogService = Substitute.For<IDialogService>();
            _mockHttpClientFactory = Substitute.For<IHttpClientFactory>();

            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharp_SteamCmdTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);

            _httpHandler = new MockHttpMessageHandler();
            _mockHttpClientFactory.CreateClient().Returns(new HttpClient(_httpHandler));

            _mockPathService.SteamCmdInstallPath.Returns(Path.Combine(_testTempDir, "Install"));
            _mockPathService.SteamCmdSteamAppsPath.Returns(Path.Combine(_testTempDir, "SteamApps"));
            _mockPathService.SteamCmdExePath.Returns(Path.Combine(_testTempDir, "Install", "steamcmd.exe"));
            _mockPathService.SteamCmdWorkshopContentPath.Returns(Path.Combine(_testTempDir, "Content"));
            _mockPathService.SteamCmdPrefixPath.Returns(_testTempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        [Fact]
        public async Task SetupAsync_WhenSuccessful_ShouldDownloadAndExtract()
        {

            _mockGamePathService.GetModsPath().Returns(@"C:\Mods");

            // Create a dummy ZIP in memory
            var dummyZipStream = new MemoryStream();
            using (var archive = new ZipArchive(dummyZipStream, ZipArchiveMode.Create, true))
            {
                var entry = archive.CreateEntry("steamcmd.exe");
                using (var entryStream = entry.Open())
                using (var writer = new StreamWriter(entryStream))
                {
                    writer.Write("dummy exe content");
                }
            }
            dummyZipStream.Position = 0;

            _httpHandler.OnRequest = req => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(dummyZipStream) };

            var platformInfo = new SteamCmdPlatformInfo();

var installer = new SteamCmdInstaller(_mockPathService, _mockGamePathService, _mockDialogService, _mockHttpClientFactory, platformInfo);

            var result = await installer.SetupAsync();

            if (platformInfo.IsSupported)
            {
                result.Should().BeTrue();
                Directory.Exists(_mockPathService.SteamCmdInstallPath).Should().BeTrue();
                File.Exists(_mockPathService.SteamCmdExePath).Should().BeTrue();
            }
            else
            {
                result.Should().BeFalse();
            }
        }

        [Fact]
        public async Task CheckSetupAsync_WhenExeExists_ShouldReturnTrue()
        {

            var exePath = _mockPathService.SteamCmdExePath;
            if (exePath == null) throw new InvalidOperationException("ExePath is null");
            var dir = Path.GetDirectoryName(exePath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(exePath, "fake exe");

            var installer = new SteamCmdInstaller(_mockPathService, _mockGamePathService, _mockDialogService, _mockHttpClientFactory, new SteamCmdPlatformInfo());

            var result = await installer.CheckSetupAsync();

            result.Should().BeTrue();
        }

        private class MockHttpMessageHandler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage> OnRequest { get; set; } = null!;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(OnRequest(request));
            }
        }
    }
}


