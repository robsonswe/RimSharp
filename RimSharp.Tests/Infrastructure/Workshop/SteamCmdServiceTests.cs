using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Workshop;
using RimSharp.Infrastructure.Workshop.Core;
using RimSharp.Infrastructure.Workshop.Download;
using RimSharp.Infrastructure.Workshop.Download.Models;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Workshop
{
    public class SteamCmdServiceTests
    {
        private readonly ISteamCmdPathService _mockPathService;
        private readonly ISteamCmdInstaller _mockInstaller;
        private readonly ISteamCmdDownloader _mockDownloader;
        private readonly ISteamCmdFileSystem _mockFileSystem;
        private readonly SteamCmdService _service;

        public SteamCmdServiceTests()
        {
            _mockPathService = Substitute.For<ISteamCmdPathService>();
            _mockInstaller = Substitute.For<ISteamCmdInstaller>();
            _mockDownloader = Substitute.For<ISteamCmdDownloader>();
            _mockFileSystem = Substitute.For<ISteamCmdFileSystem>();

            _service = new SteamCmdService(_mockPathService, _mockInstaller, _mockDownloader, _mockFileSystem);
        }

        [Fact]
        public async Task CheckSetupAsync_WhenStateChanges_ShouldRaiseEvent()
        {
            // Arrange
            _mockInstaller.CheckSetupAsync().Returns(true);
            bool? eventResult = null;
            _service.SetupStateChanged += (s, e) => eventResult = e;

            // Act
            var result = await _service.CheckSetupAsync();

            // Assert
            result.Should().BeTrue();
            _service.IsSetupComplete.Should().BeTrue();
            eventResult.Should().BeTrue();
        }

        [Fact]
        public async Task CheckSetupAsync_WhenStateDoesNotChange_ShouldNotRaiseEvent()
        {
            // Arrange
            _mockInstaller.CheckSetupAsync().Returns(false); // Initial state is false
            int eventCount = 0;
            _service.SetupStateChanged += (s, e) => eventCount++;

            // Act
            await _service.CheckSetupAsync();

            // Assert
            eventCount.Should().Be(0);
        }

        [Fact]
        public async Task SetupAsync_ShouldCallInstallerAndRefreshStatus()
        {
            // Arrange
            _mockInstaller.SetupAsync(null, Arg.Any<CancellationToken>()).Returns(true);
            _mockInstaller.CheckSetupAsync().Returns(true);

            // Act
            var result = await _service.SetupAsync();

            // Assert
            result.Should().BeTrue();
            await _mockInstaller.Received(1).SetupAsync(null, Arg.Any<CancellationToken>());
            await _mockInstaller.Received(1).CheckSetupAsync();
            _service.IsSetupComplete.Should().BeTrue();
        }

        [Fact]
        public void GetSteamCmdPrefixPath_ShouldCallPathService()
        {
            // Arrange
            _mockPathService.GetSteamCmdPrefixPath().Returns(@"C:\SteamCMD");

            // Act
            var result = _service.GetSteamCmdPrefixPath();

            // Assert
            result.Should().Be(@"C:\SteamCMD");
            _mockPathService.Received(1).GetSteamCmdPrefixPath();
        }

        [Fact]
        public async Task DownloadModsAsync_ShouldDelegateToDownloader()
        {
            // Arrange
            var items = new List<RimSharp.Features.WorkshopDownloader.Models.DownloadItem>();
            var expectedResult = new SteamCmdDownloadResult();
            _mockDownloader.DownloadModsAsync(items, true, Arg.Any<CancellationToken>()).Returns(expectedResult);

            // Act
            var result = await _service.DownloadModsAsync(items, true);

            // Assert
            result.Should().Be(expectedResult);
            await _mockDownloader.Received(1).DownloadModsAsync(items, true, Arg.Any<CancellationToken>());
        }
    }
}
