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
        private readonly SteamCmdPlatformInfo _platformInfo = new();

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
            _mockPathService.SteamCmdExePath.Returns(Path.Combine(_testTempDir, "Install", _platformInfo.SteamCmdExeName));
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

            var _platformInfo = new PrerequisiteSatisfiedPlatformInfo();

            // Create a dummy archive in memory, matching the format the
            // current platform's SteamCmdPlatformInfo expects (zip on
            // Windows, tar.gz elsewhere).
            var dummyArchiveStream = new MemoryStream();
            if (_platformInfo.IsArchiveZip)
            {
                using (var archive = new ZipArchive(dummyArchiveStream, ZipArchiveMode.Create, true))
                using (var entryStream = archive.CreateEntry(_platformInfo.SteamCmdExeName).Open())
                using (var writer = new StreamWriter(entryStream))
                {
                    writer.Write("dummy exe content");
                }
            }
            else
            {
                using (var tarStream = new MemoryStream())
                {
                    using (var archive = new System.Formats.Tar.TarWriter(tarStream, System.Formats.Tar.TarEntryFormat.Ustar, leaveOpen: true))
                    {
                        var entry = new System.Formats.Tar.UstarTarEntry(System.Formats.Tar.TarEntryType.RegularFile, _platformInfo.SteamCmdExeName);
                        using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("dummy exe content"));
                        entry.DataStream = content;
                        archive.WriteEntry(entry);
                    }
                    tarStream.Position = 0;
                    using (var gzipStream = new System.IO.Compression.GZipStream(dummyArchiveStream, System.IO.Compression.CompressionLevel.NoCompression, leaveOpen: true))
                    {
                        tarStream.CopyTo(gzipStream);
                    }
                }
                dummyArchiveStream.Position = 0;
            }
            dummyArchiveStream.Position = 0;

            _httpHandler.OnRequest = req => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(dummyArchiveStream) };

            var installer = new SteamCmdInstaller(_mockPathService, _mockGamePathService, _mockDialogService, _mockHttpClientFactory, _platformInfo);

            var dialogErrors = new System.Collections.Generic.List<string>();
            _mockDialogService.When(d => d.ShowError(Arg.Any<string>(), Arg.Any<string>()))
                .Do(ci => dialogErrors.Add($"{ci.ArgAt<string>(0)}: {ci.ArgAt<string>(1)}"));

            var progressMessages = new System.Collections.Generic.List<string>();
            var result = await installer.SetupAsync(new Progress<string>(m => progressMessages.Add(m)));

            if (_platformInfo.IsSupported)
            {
                result.Should().BeTrue(string.Join(" | ", progressMessages) + " ||| " + string.Join(" | ", dialogErrors));
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

            var installer = new SteamCmdInstaller(_mockPathService, _mockGamePathService, _mockDialogService, _mockHttpClientFactory, new PrerequisiteSatisfiedPlatformInfo());

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

        /// <summary>
        /// Platform info that reports runtime prerequisites as met, so the
        /// installer flow can be exercised on hosts where the real SteamCMD
        /// runtime libraries are not installed.
        /// </summary>
        private class PrerequisiteSatisfiedPlatformInfo : SteamCmdPlatformInfo
        {
            public override string? GetRuntimePrerequisiteProblem(Func<string, bool>? fileExists = null) => null;
        }
    }
}


