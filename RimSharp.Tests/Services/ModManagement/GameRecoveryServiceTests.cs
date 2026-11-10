using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Services.ModManagement;
using RimSharp.Services.Workshop;
using Xunit;

namespace RimSharp.Tests.Services.ModManagement
{
    public class GameRecoveryServiceTests : IDisposable
    {
        private readonly ILoggerService _logger = Substitute.For<ILoggerService>();
        private readonly IPathService _pathService = Substitute.For<IPathService>();
        private readonly ISteamSubscriptionService _steamService = Substitute.For<ISteamSubscriptionService>();
        private readonly GameInstance _gameInstance = new GameInstance
        {
            GameFolder = @"C:\Test\RimWorld",
            ConfigFolder = @"C:\Test\Config",
            LocalFolder = @"C:\Test\RimWorld\Mods",
            WorkshopFolder = @"C:\Test\Steam\steamapps\workshop\content\294100",
            IsSteamInstallation = true,
            SteamClientIntegration = true
        };
        private readonly string _testTempDir;

        public GameRecoveryServiceTests()
        {
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharp_GameRecoveryTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);

            // Setup default paths
            _gameInstance.GameFolder = Path.Combine(_testTempDir, "RimWorld");
            _gameInstance.ConfigFolder = Path.Combine(_testTempDir, "Config");
            _gameInstance.LocalFolder = Path.Combine(_testTempDir, "RimWorld", "Mods");
            _gameInstance.WorkshopFolder = Path.Combine(_testTempDir, "Steam", "steamapps", "workshop", "content", "294100");

            Directory.CreateDirectory(_gameInstance.GameFolder);
            Directory.CreateDirectory(_gameInstance.ConfigFolder);
            Directory.CreateDirectory(_gameInstance.LocalFolder);
            Directory.CreateDirectory(_gameInstance.WorkshopFolder);

            _pathService.GetConfigPath().Returns(_gameInstance.ConfigFolder);
            _pathService.GetGamePath().Returns(_gameInstance.GameFolder);
            _pathService.GetModsPath().Returns(_gameInstance.LocalFolder);

            // Tests must never reach the real Steam client.
            GameRecoveryService.TestProtocolLaunchOverride = _ => Task.FromResult(true);
        }

        public void Dispose()
        {
            GameRecoveryService.TestProtocolLaunchOverride = null;

            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        private GameRecoveryService CreateService()
        {
            return new GameRecoveryService(_gameInstance, _logger, _pathService, _steamService);
        }

        #region ResetModConfigurationsAsync Tests

        [Fact]
        public async Task ResetModConfigurationsAsync_ProtectsModsConfigXmlAndPrefsXml_DeletesOthers()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;

            File.WriteAllText(Path.Combine(configDir, "ModsConfig.xml"), "<ModsConfigData/>");
            File.WriteAllText(Path.Combine(configDir, "Prefs.xml"), "<Prefs/>");
            File.WriteAllText(Path.Combine(configDir, "KeyPrefs.xml"), "<KeyPrefs/>");
            File.WriteAllText(Path.Combine(configDir, "Mod_SomeMod.xml"), "<Mod/>");
            File.WriteAllText(Path.Combine(configDir, "AnotherConfig.txt"), "config");

            // Act
            var result = await service.ResetModConfigurationsAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();
            File.Exists(Path.Combine(configDir, "ModsConfig.xml")).Should().BeTrue("ModsConfig.xml should be protected");
            File.Exists(Path.Combine(configDir, "Prefs.xml")).Should().BeTrue("Prefs.xml should be protected");
            File.Exists(Path.Combine(configDir, "KeyPrefs.xml")).Should().BeFalse("KeyPrefs.xml should be deleted");
            File.Exists(Path.Combine(configDir, "Mod_SomeMod.xml")).Should().BeFalse("Mod_*.xml should be deleted");
            File.Exists(Path.Combine(configDir, "AnotherConfig.txt")).Should().BeFalse("Other files should be deleted");
            result.Count.Should().Be(3); // KeyPrefs.xml, Mod_SomeMod.xml, AnotherConfig.txt
        }

        [Fact]
        public async Task ResetModConfigurationsAsync_ConfigFolderNotFound_ReturnsFail()
        {
            // Arrange
            var service = CreateService();
            _pathService.GetConfigPath().Returns(@"C:\NonExistent\Config");

            // Act
            var result = await service.ResetModConfigurationsAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Config folder not found");
        }

        #endregion

        #region ResetGameConfigurationsAsync Tests

        [Fact]
        public async Task ResetGameConfigurationsAsync_DeletesExactlyThreeFiles()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;

            File.WriteAllText(Path.Combine(configDir, "ModsConfig.xml"), "<ModsConfigData/>");
            File.WriteAllText(Path.Combine(configDir, "Prefs.xml"), "<Prefs/>");
            File.WriteAllText(Path.Combine(configDir, "KeyPrefs.xml"), "<KeyPrefs/>");
            File.WriteAllText(Path.Combine(configDir, "Other.xml"), "<Other/>");

            // Act
            var result = await service.ResetGameConfigurationsAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();
            File.Exists(Path.Combine(configDir, "ModsConfig.xml")).Should().BeFalse();
            File.Exists(Path.Combine(configDir, "Prefs.xml")).Should().BeFalse();
            File.Exists(Path.Combine(configDir, "KeyPrefs.xml")).Should().BeFalse();
            File.Exists(Path.Combine(configDir, "Other.xml")).Should().BeTrue("Other files should not be touched");
            result.Count.Should().Be(3);
        }

        [Fact]
        public async Task ResetGameConfigurationsAsync_MissingFiles_DeletesOnlyExisting()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;

            File.WriteAllText(Path.Combine(configDir, "ModsConfig.xml"), "<ModsConfigData/>");
            // Prefs.xml and KeyPrefs.xml don't exist

            // Act
            var result = await service.ResetGameConfigurationsAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();
            result.Count.Should().Be(1);
            File.Exists(Path.Combine(configDir, "ModsConfig.xml")).Should().BeFalse();
        }

        #endregion

        #region CleanLogFilesAsync Tests

        [Fact]
        public async Task CleanLogFilesAsync_TruncatesLargeLogs_DeletesSmallLogs()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;

            // Create a large Player.log (>50MB)
            var largeLog = Path.Combine(configDir, "Player.log");
            using (var fs = new FileStream(largeLog, FileMode.Create))
            {
                var buffer = new byte[1024 * 1024]; // 1MB
                for (int i = 0; i < 60; i++) // 60MB
                {
                    await fs.WriteAsync(buffer, TestContext.Current.CancellationToken);
                }
            }

            // Create a small Player-prev.log (<50MB)
            var smallLog = Path.Combine(configDir, "Player-prev.log");
            File.WriteAllText(smallLog, "small log content");

            // Act
            var result = await service.CleanLogFilesAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();
            result.Count.Should().Be(1); // Only large log processed

            var largeLogInfo = new FileInfo(largeLog);
            largeLogInfo.Length.Should().BeLessThan(60 * 1024 * 1024); // Should be truncated
            largeLogInfo.Length.Should().BeGreaterThan(0); // But not empty (keeps last 1MB)

            File.Exists(smallLog).Should().BeTrue("Small log should not be touched");
        }

        [Fact]
        public async Task CleanLogFilesAsync_NoLargeLogs_NoOp()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;

            File.WriteAllText(Path.Combine(configDir, "Player.log"), "small log");
            File.WriteAllText(Path.Combine(configDir, "Player-prev.log"), "small prev log");

            // Act
            var result = await service.CleanLogFilesAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();
            result.Count.Should().Be(0); // No files processed
            File.Exists(Path.Combine(configDir, "Player.log")).Should().BeTrue();
            File.Exists(Path.Combine(configDir, "Player-prev.log")).Should().BeTrue();
        }

        #endregion

        #region ResetDisplayResolutionAsync Tests

        [Fact]
        public async Task ResetDisplayResolutionAsync_EditsOnlyDisplayNodes()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            var prefsPath = Path.Combine(configDir, "Prefs.xml");

            var prefsXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Prefs>
  <screenWidth>1920</screenWidth>
  <screenHeight>1080</screenHeight>
  <fullscreen>True</fullscreen>
  <uiScale>1.5</uiScale>
  <windowPositionX>100</windowPositionX>
  <windowPositionY>200</windowPositionY>
  <someOtherSetting>value</someOtherSetting>
  <Screenmanager_Is_Fullscreen_mode>1</Screenmanager_Is_Fullscreen_mode>
  <Screenmanager_Resolution_Width>1920</Screenmanager_Resolution_Width>
  <Screenmanager_Resolution_Height>1080</Screenmanager_Resolution_Height>
</Prefs>";
            File.WriteAllText(prefsPath, prefsXml);

            // Act
            var result = await service.ResetDisplayResolutionAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue($"Failed with message: {result.Message}");
            result.Count.Should().BeGreaterThan(0);

            var doc = XDocument.Load(prefsPath);
            var root = doc.Root!;

            root.Element("screenWidth")?.Value.Should().Be("1280");
            root.Element("screenHeight")?.Value.Should().Be("720");
            root.Element("fullscreen")?.Value.Should().Be("False");
            root.Element("uiScale")?.Value.Should().Be("1.0");
            root.Element("windowPositionX")?.Value.Should().Be("0");
            root.Element("windowPositionY")?.Value.Should().Be("0");
            root.Element("Screenmanager_Is_Fullscreen_mode")?.Value.Should().Be("0");
            root.Element("Screenmanager_Resolution_Width")?.Value.Should().Be("1280");
            root.Element("Screenmanager_Resolution_Height")?.Value.Should().Be("720");

            // Non-display setting should be unchanged
            root.Element("someOtherSetting")?.Value.Should().Be("value");
        }

        [Fact]
        public async Task ResetDisplayResolutionAsync_PrefsXmlNotFound_ReturnsFail()
        {
            // Arrange
            var service = CreateService();
            _pathService.GetConfigPath().Returns(@"C:\NonExistent\Config");

            // Act
            var result = await service.ResetDisplayResolutionAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Config folder not found");
        }

        #endregion

        #region Gated/Graceful Path Tests

        [Fact]
        public async Task ResetGameFilesAsync_NonSteamInstallation_ReturnsNotAvailable()
        {
            // Arrange
            var service = CreateService();
            _gameInstance.IsSteamInstallation = false;

            // Act
            var result = await service.ResetGameFilesAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Not available");
            result.Message.Should().Contain("Steam installation");
        }

        [Fact]
        public async Task ResetSteamWorkshopModsAsync_NonSteamInstallation_ReturnsNotAvailable()
        {
            // Arrange
            var service = CreateService();
            _gameInstance.IsSteamInstallation = false;

            // Act
            var result = await service.ResetSteamWorkshopModsAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Not available");
            result.Message.Should().Contain("Steam installation");
        }

        [Fact]
        public async Task ResetGameFilesAsync_GameFolderNotFound_ReturnsFail()
        {
            // Arrange
            var service = CreateService();
            _gameInstance.GameFolder = @"C:\NonExistent\Game";

            // Act
            var result = await service.ResetGameFilesAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Game folder not found");
        }

        #endregion
    }
}