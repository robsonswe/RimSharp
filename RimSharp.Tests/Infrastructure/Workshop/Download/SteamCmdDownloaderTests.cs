using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Infrastructure.Workshop.Core;
using RimSharp.Infrastructure.Workshop.Download;
using RimSharp.Infrastructure.Workshop.Download.Execution;
using RimSharp.Infrastructure.Workshop.Download.Models;
using RimSharp.Infrastructure.Workshop.Download.Parsing;
using RimSharp.Infrastructure.Workshop.Download.Parsing.Models;
using RimSharp.Infrastructure.Workshop.Download.Processing;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Workshop.Download
{
    public class SteamCmdDownloaderTests : IDisposable
    {
        private readonly ISteamCmdPathService _mockPathService;
        private readonly ISteamCmdInstaller _mockInstaller;
        private readonly IDialogService _mockDialogService;
        private readonly ILoggerService _mockLogger;
        private readonly IPathService _mockGamePathService;
        private readonly ISteamCmdScriptGenerator _mockScriptGenerator;
        private readonly ISteamCmdProcessRunner _mockProcessRunner;
        private readonly ISteamCmdLogParser _mockLogParser;
        private readonly IDownloadedItemProcessor _mockItemProcessor;
        private readonly SteamCmdDownloader _downloader;
        private readonly string _testTempDir;

        public SteamCmdDownloaderTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();
            _mockPathService = Substitute.For<ISteamCmdPathService>();
            _mockInstaller = Substitute.For<ISteamCmdInstaller>();
            _mockDialogService = Substitute.For<IDialogService>();
            _mockLogger = Substitute.For<ILoggerService>();
            _mockGamePathService = Substitute.For<IPathService>();
            _mockScriptGenerator = Substitute.For<ISteamCmdScriptGenerator>();
            _mockProcessRunner = Substitute.For<ISteamCmdProcessRunner>();
            _mockLogParser = Substitute.For<ISteamCmdLogParser>();
            _mockItemProcessor = Substitute.For<IDownloadedItemProcessor>();
            _mockItemProcessor.GetLogMessages().Returns(new List<string>());

            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_Downloader_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);

            _downloader = new SteamCmdDownloader(
                _mockPathService, _mockInstaller, _mockDialogService, _mockLogger, _mockGamePathService,
                _mockScriptGenerator, _mockProcessRunner, _mockLogParser, _mockItemProcessor);

            // Default setup for success
            string dummyExe = Path.Combine(_testTempDir, "steamcmd.exe");
            File.WriteAllText(dummyExe, "dummy");

            _mockInstaller.CheckSetupAsync().Returns(true);
            _mockPathService.SteamCmdExePath.Returns(dummyExe);
            _mockPathService.SteamCmdInstallPath.Returns(_testTempDir);
            _mockPathService.SteamCmdSteamAppsPath.Returns(_testTempDir);
            _mockGamePathService.GetModsPath().Returns(Path.Combine(_testTempDir, "Mods"));
            Directory.CreateDirectory(_mockGamePathService.GetModsPath());
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        [Fact]
        public async Task DownloadModsAsync_WhenEverythingSucceeds_ShouldReturnOverallSuccess()
        {
            // Arrange
            var items = new List<DownloadItem> { new DownloadItem { SteamId = "123", Name = "Mod 1" } };
            
            _mockLogParser.ParseSteamCmdSessionLogsAsync(Arg.Any<SteamCmdLogFilePaths>(), Arg.Any<ISet<string>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                .Returns(new SteamCmdSessionLogParseResult
                {
                    WorkshopItemResults = { ["123"] = (true, DateTime.Now, null) }
                });

            _mockItemProcessor.ProcessItemAsync(Arg.Any<DownloadItem>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((true, "Processed"));

            // Act
            var result = await _downloader.DownloadModsAsync(items, true);

            // Assert
            result.OverallSuccess.Should().BeTrue();
            result.SucceededItems.Should().HaveCount(1);
            result.FailedItems.Should().BeEmpty();
        }

        [Fact]
        public async Task DownloadModsAsync_WhenDownloadFailsAfterRetries_ShouldReturnOverallFailure()
        {
            // Arrange
            var items = new List<DownloadItem> { new DownloadItem { SteamId = "123", Name = "Mod 1" } };
            
            // Log parser returns failure for all attempts
            _mockLogParser.ParseSteamCmdSessionLogsAsync(Arg.Any<SteamCmdLogFilePaths>(), Arg.Any<ISet<string>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                .Returns(new SteamCmdSessionLogParseResult
                {
                    WorkshopItemResults = { ["123"] = (false, DateTime.Now, "Timeout") }
                });

            // Act
            var result = await _downloader.DownloadModsAsync(items, true);

            // Assert
            result.OverallSuccess.Should().BeFalse();
            result.FailedItems.Should().HaveCount(1);
            result.FailedItems[0].Item.SteamId.Should().Be("123");
            result.FailedItems[0].Reason.Should().Contain("Timeout");
        }
    }
}
