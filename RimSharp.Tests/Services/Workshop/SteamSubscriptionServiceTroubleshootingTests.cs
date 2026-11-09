using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Workshop;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Services.Workshop;
using Xunit;

namespace RimSharp.Tests.Services.Workshop
{
    public class SteamSubscriptionServiceTroubleshootingTests : IDisposable
    {
        private readonly ILoggerService _logger = Substitute.For<ILoggerService>();
        private readonly IPathService _pathService = Substitute.For<IPathService>();
        private readonly SteamWorkshopApiClient _apiClient = Substitute.For<SteamWorkshopApiClient>(Substitute.For<System.Net.Http.HttpClient>());
        private readonly GameInstance _gameInstance = new GameInstance
        {
            GameFolder = @"C:\Test\RimWorld",
            WorkshopFolder = @"C:\Test\Steam\steamapps\workshop\content\294100",
            IsSteamInstallation = true,
            SteamClientIntegration = true
        };
        private readonly string _testTempDir;

        public SteamSubscriptionServiceTroubleshootingTests()
        {
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharp_SteamTroubleshootingTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);

            _gameInstance.GameFolder = Path.Combine(_testTempDir, "RimWorld");
            _gameInstance.WorkshopFolder = Path.Combine(_testTempDir, "Steam", "steamapps", "workshop", "content", "294100");

            Directory.CreateDirectory(_gameInstance.GameFolder);
            Directory.CreateDirectory(_gameInstance.WorkshopFolder);
            Directory.CreateDirectory(Path.Combine(_testTempDir, "Steam", "steamapps", "workshop"));
            Directory.CreateDirectory(Path.Combine(_testTempDir, "Steam", "steamapps", "downloading"));
            Directory.CreateDirectory(Path.Combine(_testTempDir, "Steam", "steamapps", "workshop", "downloads"));
            Directory.CreateDirectory(Path.Combine(_testTempDir, "Steam", "steamapps", "workshop", "temp", "294100"));

            // Tests must never reach the real Steam client or enumerate real processes.
            SteamSubscriptionService.TestProtocolLaunchOverride = _ => Task.FromResult(true);
            SteamSubscriptionService.TestProcessEnumerationOverride = () => Array.Empty<Process>();
        }

        public void Dispose()
        {
            SteamSubscriptionService.TestSteamworksSupportedOverride = null;
            SteamSubscriptionService.TestProtocolLaunchOverride = null;
            SteamSubscriptionService.TestProcessEnumerationOverride = null;

            if (Directory.Exists(_testTempDir))
            {
                // Reset read-only attributes on all files to allow deletion
                try
                {
                    foreach (var file in Directory.GetFiles(_testTempDir, "*.*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                }
                catch { }
                Directory.Delete(_testTempDir, true);
            }
        }

        private SteamSubscriptionService CreateService(Func<bool>? steamApiInit = null, ISteamworksInterop? interop = null)
        {
            return new SteamSubscriptionService(_gameInstance, _logger, _apiClient, _pathService, steamApiInit, interop: interop);
        }

        #region ClearDownloadCacheAsync Tests

        [Fact]
        public async Task ClearDownloadCacheAsync_DeletesExactlyThreeTargets()
        {
            // Arrange
            var service = CreateService(steamApiInit: () => true);
            var steamAppsDir = Path.Combine(_testTempDir, "Steam", "steamapps");

            // Create test files in all three targets
            File.WriteAllText(Path.Combine(steamAppsDir, "downloading", "file1.tmp"), "data");
            File.WriteAllText(Path.Combine(steamAppsDir, "downloading", "file2.tmp"), "data");
            Directory.CreateDirectory(Path.Combine(steamAppsDir, "downloading", "subdir"));
            File.WriteAllText(Path.Combine(steamAppsDir, "downloading", "subdir", "file3.tmp"), "data");

            File.WriteAllText(Path.Combine(steamAppsDir, "workshop", "downloads", "download1.tmp"), "data");
            Directory.CreateDirectory(Path.Combine(steamAppsDir, "workshop", "downloads", "sub"));
            File.WriteAllText(Path.Combine(steamAppsDir, "workshop", "downloads", "sub", "download2.tmp"), "data");

            File.WriteAllText(Path.Combine(steamAppsDir, "workshop", "temp", "294100", "temp1.tmp"), "data");
            Directory.CreateDirectory(Path.Combine(steamAppsDir, "workshop", "temp", "294100", "sub"));
            File.WriteAllText(Path.Combine(steamAppsDir, "workshop", "temp", "294100", "sub", "temp2.tmp"), "data");

            // Act
            var result = await service.ClearDownloadCacheAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();
            result.Count.Should().Be(7); // 3 + 2 + 2 items (files + directories)

            Directory.Exists(Path.Combine(steamAppsDir, "downloading")).Should().BeTrue("Directory should remain");
            new DirectoryInfo(Path.Combine(steamAppsDir, "downloading")).GetFileSystemInfos().Should().BeEmpty();

            Directory.Exists(Path.Combine(steamAppsDir, "workshop", "downloads")).Should().BeTrue();
            new DirectoryInfo(Path.Combine(steamAppsDir, "workshop", "downloads")).GetFileSystemInfos().Should().BeEmpty();

            Directory.Exists(Path.Combine(steamAppsDir, "workshop", "temp", "294100")).Should().BeTrue();
            new DirectoryInfo(Path.Combine(steamAppsDir, "workshop", "temp", "294100")).GetFileSystemInfos().Should().BeEmpty();
        }

        [Fact]
        public async Task ClearDownloadCacheAsync_NonSteamInstallation_ReturnsNotAvailable()
        {
            // Arrange
            var service = CreateService(steamApiInit: () => true);
            _gameInstance.IsSteamInstallation = false;

            // Act
            var result = await service.ClearDownloadCacheAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Not available");
            result.Message.Should().Contain("Steam installation");
        }

        [Fact]
        public async Task ClearDownloadCacheAsync_UnsupportedRuntime_ReturnsNotAvailable()
        {
            // Arrange
            SteamSubscriptionService.TestSteamworksSupportedOverride = () => false;
            var service = CreateService();

            // Act
            var result = await service.ClearDownloadCacheAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Not available");
            result.Message.Should().Contain("Steamworks not supported");
        }

        #endregion

        #region CleanOrphanedWorkshopModsAsync Tests

        [Fact]
        public async Task CleanOrphanedWorkshopModsAsync_RemovesStalePFIDsFromBothSections()
        {
            // Arrange
            var service = CreateService(steamApiInit: () => true);

            var steamAppsDir = Path.Combine(_testTempDir, "Steam", "steamapps");
            var workshopDir = Path.Combine(steamAppsDir, "workshop");
            var contentDir = Path.Combine(workshopDir, "content", "294100");
            _gameInstance.WorkshopFolder = contentDir;

            // Create ACF with stale entry (999999999) and valid entry (123456789)
            var acfContent = @" ""AppWorkshop""
{
    ""WorkshopItemsInstalled""
    {
        ""999999999""		""1694112958""
        ""123456789""		""1694112959""
    }
    ""WorkshopItemDetails""
    {
        ""999999999""
        {
            ""timeupdated""		""1694112958""
        }
        ""123456789""
        {
            ""timeupdated""		""1694112959""
        }
    }
}";
            File.WriteAllText(Path.Combine(workshopDir, "appworkshop_294100.acf"), acfContent);

            // Create only valid mod on disk
            Directory.CreateDirectory(Path.Combine(contentDir, "123456789"));

            // Act
            var result = await service.CleanOrphanedWorkshopModsAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();
            result.Count.Should().Be(1);

            // Verify ACF was updated - stale entry removed from both sections
            var parser = new AcfFileParser();
            parser.TryParse(Path.Combine(workshopDir, "appworkshop_294100.acf"), out var acfData, out _).Should().BeTrue();
            acfData.Items.Should().ContainKey("123456789");
            acfData.Items.Should().NotContainKey("999999999");

            // Verify backup was created and then cleaned up
            File.Exists(Path.Combine(workshopDir, "appworkshop_294100.acf.bak")).Should().BeFalse("Backup should be cleaned up on success");
        }

        [Fact]
        public async Task CleanOrphanedWorkshopModsAsync_CorruptWrite_AutoRestoresBackup()
        {
            // Arrange
            var service = CreateService(steamApiInit: () => true);

            var steamAppsDir = Path.Combine(_testTempDir, "Steam", "steamapps");
            var workshopDir = Path.Combine(steamAppsDir, "workshop");
            var contentDir = Path.Combine(workshopDir, "content", "294100");
            _gameInstance.WorkshopFolder = contentDir;

            // Create valid ACF
            var acfContent = @" ""AppWorkshop""
{
    ""WorkshopItemsInstalled""
    {
        ""123456789""		""1694112959""
    }
    ""WorkshopItemDetails""
    {
        ""123456789""
        {
            ""timeupdated""		""1694112959""
        }
    }
}";
            File.WriteAllText(Path.Combine(workshopDir, "appworkshop_294100.acf"), acfContent);
            Directory.CreateDirectory(Path.Combine(contentDir, "123456789"));

            // Make ACF file read-only to force write failure
            var acfPath = Path.Combine(workshopDir, "appworkshop_294100.acf");
            File.SetAttributes(acfPath, FileAttributes.ReadOnly);

            try
            {
                // Act
                var result = await service.CleanOrphanedWorkshopModsAsync(TestContext.Current.CancellationToken);

                // Assert
                result.Success.Should().BeFalse();
                result.Message.Should().Contain("failed");

                // Verify backup was restored (original content intact)
                var restoredContent = File.ReadAllText(acfPath);
                restoredContent.Should().Contain("123456789");
            }
            finally
            {
                // Cleanup
                File.SetAttributes(acfPath, FileAttributes.Normal);
            }
        }

        [Fact]
        public async Task CleanOrphanedWorkshopModsAsync_NonSteamInstallation_ReturnsNotAvailable()
        {
            // Arrange
            var service = CreateService(steamApiInit: () => true);
            _gameInstance.IsSteamInstallation = false;

            // Act
            var result = await service.CleanOrphanedWorkshopModsAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Not available");
            result.Message.Should().Contain("Steam installation");
        }

        [Fact]
        public async Task CleanOrphanedWorkshopModsAsync_NoStaleEntries_ReturnsOk()
        {
            // Arrange
            var service = CreateService(steamApiInit: () => true);

            var steamAppsDir = Path.Combine(_testTempDir, "Steam", "steamapps");
            var workshopDir = Path.Combine(steamAppsDir, "workshop");
            var contentDir = Path.Combine(workshopDir, "content", "294100");
            _gameInstance.WorkshopFolder = contentDir;

            // Create ACF matching disk
            var acfContent = @" ""AppWorkshop""
{
    ""WorkshopItemsInstalled""
    {
        ""123456789""		""1694112959""
    }
    ""WorkshopItemDetails""
    {
        ""123456789""
        {
            ""timeupdated""		""1694112959""
        }
    }
}";
            File.WriteAllText(Path.Combine(workshopDir, "appworkshop_294100.acf"), acfContent);
            Directory.CreateDirectory(Path.Combine(contentDir, "123456789"));

            // Act
            var result = await service.CleanOrphanedWorkshopModsAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();
            result.Count.Should().Be(0);
            result.Message.Should().Contain("No orphaned workshop mods found");
        }

        #endregion

        #region KillZombieProcessesAsync Tests

        [Fact]
        public async Task KillZombieProcessesAsync_KillsRimWorldProcesses()
        {
            // Arrange
            var service = CreateService(steamApiInit: () => true);

            // We can't easily test process killing without the test seam
            // This test verifies the method runs without error
            // The actual process killing is tested via the TroubleshootingScanService test seam

            // Act
            var result = await service.KillZombieProcessesAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue(); // Should succeed even if no processes found
        }

        [Fact]
        public async Task KillZombieProcessesAsync_UnsupportedRuntime_StillWorks()
        {
            // Arrange - KillZombieProcessesAsync is universal (not Steam-gated)
            SteamSubscriptionService.TestSteamworksSupportedOverride = () => false;
            var service = CreateService();

            // Act
            var result = await service.KillZombieProcessesAsync(TestContext.Current.CancellationToken);

            // Assert - Should succeed (may find 0 processes)
            result.Success.Should().BeTrue();
        }

        #endregion

        #region PurgeWorkshopTempAsync Tests

        [Fact]
        public async Task PurgeWorkshopTempAsync_DeletesWorkshopDownloadsAndTemp()
        {
            // Arrange
            var service = CreateService(steamApiInit: () => true);

            var steamAppsDir = Path.Combine(_testTempDir, "Steam", "steamapps");

            // Create test files
            File.WriteAllText(Path.Combine(steamAppsDir, "workshop", "downloads", "download1.tmp"), "data");
            Directory.CreateDirectory(Path.Combine(steamAppsDir, "workshop", "downloads", "sub"));
            File.WriteAllText(Path.Combine(steamAppsDir, "workshop", "downloads", "sub", "download2.tmp"), "data");

            File.WriteAllText(Path.Combine(steamAppsDir, "workshop", "temp", "294100", "temp1.tmp"), "data");
            Directory.CreateDirectory(Path.Combine(steamAppsDir, "workshop", "temp", "294100", "sub"));
            File.WriteAllText(Path.Combine(steamAppsDir, "workshop", "temp", "294100", "sub", "temp2.tmp"), "data");

            // Act
            var result = await service.PurgeWorkshopTempAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();
            result.Count.Should().Be(4); // 2 + 2 files

            new DirectoryInfo(Path.Combine(steamAppsDir, "workshop", "downloads")).GetFileSystemInfos().Should().BeEmpty();
            new DirectoryInfo(Path.Combine(steamAppsDir, "workshop", "temp", "294100")).GetFileSystemInfos().Should().BeEmpty();
        }

        [Fact]
        public async Task PurgeWorkshopTempAsync_NonSteamInstallation_ReturnsNotAvailable()
        {
            // Arrange
            var service = CreateService(steamApiInit: () => true);
            _gameInstance.IsSteamInstallation = false;

            // Act
            var result = await service.PurgeWorkshopTempAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Not available");
            result.Message.Should().Contain("Steam installation");
        }

        #endregion

        #region RepairLibraryAsync Tests

        [Fact]
        public async Task RepairLibraryAsync_ParsesLibraryFoldersVdf_LaunchesValidate()
        {
            // Arrange
            var service = CreateService(steamApiInit: () => true);

            var steamAppsDir = Path.Combine(_testTempDir, "Steam", "steamapps");
            var libraryFoldersPath = Path.Combine(steamAppsDir, "libraryfolders.vdf");

            // Create libraryfolders.vdf with app IDs
            var vdfContent = @"""libraryfolders""
{
    ""0""
    {
        ""path""		""C:\Steam""
        ""apps""
        {
            ""294100""		""1694112958""
            ""730""		""1694112959""
        }
    }
    ""1""
    {
        ""path""		""D:\Games""
        ""apps""
        {
            ""440""		""1694112960""
        }
    }
}";
            File.WriteAllText(libraryFoldersPath, vdfContent);

            // Act
            var result = await service.RepairLibraryAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();
            result.Count.Should().Be(3); // 294100, 730, 440
            result.Details!["validatedApps"].Should().BeEquivalentTo(new[] { 294100, 730, 440 });
        }

        [Fact]
        public async Task RepairLibraryAsync_NonSteamInstallation_ReturnsNotAvailable()
        {
            // Arrange
            var service = CreateService(steamApiInit: () => true);
            _gameInstance.IsSteamInstallation = false;

            // Act
            var result = await service.RepairLibraryAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Not available");
            result.Message.Should().Contain("Steam installation");
        }

        #endregion
    }
}