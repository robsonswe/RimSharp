using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.LogViewer.Services;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Services.ModManagement;
using RimSharp.Services.Workshop;
using Xunit;

namespace RimSharp.Tests.Services.ModManagement
{
    public class TroubleshootingScanServiceTests : IDisposable
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

        public TroubleshootingScanServiceTests()
        {
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharp_ScanTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);

            _gameInstance.GameFolder = Path.Combine(_testTempDir, "RimWorld");
            _gameInstance.ConfigFolder = Path.Combine(_testTempDir, "Config");
            _gameInstance.LocalFolder = Path.Combine(_testTempDir, "RimWorld", "Mods");
            _gameInstance.WorkshopFolder = Path.Combine(_testTempDir, "Steam", "steamapps", "workshop", "content", "294100");

            Directory.CreateDirectory(_gameInstance.GameFolder);
            Directory.CreateDirectory(_gameInstance.ConfigFolder);
            Directory.CreateDirectory(_gameInstance.LocalFolder);
            Directory.CreateDirectory(_gameInstance.WorkshopFolder);
            Directory.CreateDirectory(Path.Combine(_gameInstance.WorkshopFolder, "..", "..", "..")); // steamapps

            _pathService.GetConfigPath().Returns(_gameInstance.ConfigFolder);
            _pathService.GetGamePath().Returns(_gameInstance.GameFolder);
            _pathService.GetModsPath().Returns(_gameInstance.LocalFolder);
            _pathService.GetMajorGameVersion().Returns("1.5");
        }

        public void Dispose()
        {
            // Reset test overrides
            TroubleshootingScanService.TestIsSteamInstallationOverride = null;
            TroubleshootingScanService.TestIsWindowsOverride = null;
            TroubleshootingScanService.TestGameVersionOverride = null;
            TroubleshootingScanService.TestGetProcessesOverride = null;
            TroubleshootingScanService.TestLogLocatorOverride = null;
            TroubleshootingScanService.TestLogReaderOverride = null;

            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        private TroubleshootingScanService CreateService()
        {
            return new TroubleshootingScanService(_gameInstance, _logger, _pathService, _steamService);
        }

        #region Gate Tests

        [Fact]
        public async Task RunScanAsync_SteamGatedChecks_SkippedWhenNotSteam()
        {
            // Arrange
            var service = CreateService();
            TroubleshootingScanService.TestIsSteamInstallationOverride = () => false;

            // Act
            var findings = await service.RunScanAsync(TestContext.Current.CancellationToken);

            // Assert - Steam-gated checks should be skipped silently (not in findings)
            var steamChecks = findings.Where(f => new[] { "workshop_mods", "orphaned_workshop_mods", "stuck_downloads" }.Contains(f.CheckId)).ToList();
            steamChecks.Should().BeEmpty("Steam-gated checks should be skipped silently when not Steam");
        }

        [Fact]
        public async Task RunScanAsync_WindowsOnlyCheck_SkippedOnLinux()
        {
            // Arrange
            var service = CreateService();
            TroubleshootingScanService.TestIsWindowsOverride = () => false;

            // Act
            var findings = await service.RunScanAsync(TestContext.Current.CancellationToken);

            // Assert - Windows-only check should be skipped silently (not in findings)
            var configAttrCheck = findings.FirstOrDefault(f => f.CheckId == "config_folder_attributes");
            configAttrCheck.Should().BeNull("Windows-only check should be skipped silently on Linux");
        }

        [Fact]
        public async Task RunScanAsync_UnityCrashHandlerCheck_OnlyOnWindowsAndVersionLT17()
        {
            // Arrange
            var service = CreateService();
            TroubleshootingScanService.TestIsWindowsOverride = () => true;
            TroubleshootingScanService.TestGameVersionOverride = () => "1.7.0"; // Waylith, no UnityCrashHandler64
            TroubleshootingScanService.TestGetProcessesOverride = () => Array.Empty<Process>();

            // Act
            var findings = await service.RunScanAsync(TestContext.Current.CancellationToken);

            // Assert
            var processCheck = findings.FirstOrDefault(f => f.CheckId == "running_processes");
            processCheck.Should().NotBeNull();
            processCheck!.Detail.Should().NotContain("UnityCrashHandler64", "UnityCrashHandler64 should not be checked on 1.7+");
        }

        [Fact]
        public async Task RunScanAsync_UnityCrashHandlerCheck_IncludedOnWindowsVersionLT17()
        {
            // Arrange
            var service = CreateService();
            TroubleshootingScanService.TestIsWindowsOverride = () => true;
            TroubleshootingScanService.TestGameVersionOverride = () => "1.6.4000"; // Unity era
            TroubleshootingScanService.TestGetProcessesOverride = () => Array.Empty<Process>();

            // Act
            var findings = await service.RunScanAsync(TestContext.Current.CancellationToken);

            // Assert - the check runs but finds no processes
            var processCheck = findings.FirstOrDefault(f => f.CheckId == "running_processes");
            processCheck.Should().NotBeNull();
            processCheck!.Passed.Should().BeTrue("No zombie processes found");
        }

        #endregion

        #region Individual Check Tests

        [Fact]
        public async Task CheckPathsResolve_ValidPaths_Passes()
        {
            // Arrange
            var service = CreateService();

            // Act
            var findings = await service.RunScanAsync(TestContext.Current.CancellationToken);

            // Assert
            var check = findings.FirstOrDefault(f => f.CheckId == "paths_resolve");
            check.Should().NotBeNull();
            check!.Passed.Should().BeTrue();
            check.Severity.Should().Be(ScanSeverity.Info);
        }

        [Fact]
        public async Task CheckPathsResolve_MissingGameFolder_Fails()
        {
            // Arrange
            var service = CreateService();
            _gameInstance.GameFolder = @"C:\NonExistent\Game";

            // Act
            var findings = await service.RunScanAsync(TestContext.Current.CancellationToken);

            // Assert
            var check = findings.FirstOrDefault(f => f.CheckId == "paths_resolve");
            check.Should().NotBeNull();
            check!.Passed.Should().BeFalse();
            check.Severity.Should().Be(ScanSeverity.Error);
            check.SuggestedActionId.Should().Be("configure_paths");
        }

        [Fact]
        public async Task CheckLogFileSizes_LargeLog_Warns()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            var largeLog = Path.Combine(configDir, "Player.log");
            using (var fs = new FileStream(largeLog, FileMode.Create))
            {
                var buffer = new byte[1024 * 1024];
                for (int i = 0; i < 60; i++) // 60MB
                {
                    await fs.WriteAsync(buffer, TestContext.Current.CancellationToken);
                }
            }

            // Act
            var findings = await service.RunScanAsync(TestContext.Current.CancellationToken);

            // Assert
            var check = findings.FirstOrDefault(f => f.CheckId == "log_file_sizes");
            check.Should().NotBeNull();
            check!.Passed.Should().BeFalse();
            check.Severity.Should().Be(ScanSeverity.Warning);
            check.SuggestedActionId.Should().Be("clean_log_files");
        }

        [Fact]
        public async Task CheckModSettingsXml_CorruptFile_Detects()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            File.WriteAllText(Path.Combine(configDir, "Mod_Test.xml"), "not valid xml");

            // Act
            var findings = await service.RunScanAsync(TestContext.Current.CancellationToken);

            // Assert
            var check = findings.FirstOrDefault(f => f.CheckId == "mod_settings_xml");
            check.Should().NotBeNull();
            check!.Passed.Should().BeFalse();
            check.Severity.Should().Be(ScanSeverity.Error);
            check.SuggestedActionId.Should().Be("verify_mod_settings_xml");
        }

        [Fact]
        public async Task CheckModSettingsXml_ZeroByteFile_Detects()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            File.WriteAllText(Path.Combine(configDir, "Mod_Test.xml"), "");

            // Act
            var findings = await service.RunScanAsync(TestContext.Current.CancellationToken);

            // Assert
            var check = findings.FirstOrDefault(f => f.CheckId == "mod_settings_xml");
            check.Should().NotBeNull();
            check!.Passed.Should().BeFalse();
            check.Severity.Should().Be(ScanSeverity.Error);
        }

        [Fact]
        public async Task CheckModAssemblies_ZeroByteAssembly_Detects()
        {
            // Arrange
            var service = CreateService();
            var modDir = Path.Combine(_gameInstance.LocalFolder!, "TestMod");
            Directory.CreateDirectory(modDir);
            File.WriteAllText(Path.Combine(modDir, "TestMod.dll"), ""); // Zero-byte

            // Act
            var findings = await service.RunScanAsync(TestContext.Current.CancellationToken);

            // Assert
            var check = findings.FirstOrDefault(f => f.CheckId == "mod_assemblies");
            check.Should().NotBeNull();
            check!.Passed.Should().BeFalse();
            check.Severity.Should().Be(ScanSeverity.Error);
            check.SuggestedActionId.Should().Be("scan_missing_assemblies");
        }

        [Fact]
        public async Task CheckPathCompatibility_NonAsciiPath_Detects()
        {
            // Arrange
            var service = CreateService();
            _gameInstance.GameFolder = @"C:\Test\RimWörld"; // ö is non-ASCII

            // Act
            var findings = await service.RunScanAsync(TestContext.Current.CancellationToken);

            // Assert
            var check = findings.FirstOrDefault(f => f.CheckId == "path_compatibility");
            check.Should().NotBeNull();
            check!.Passed.Should().BeFalse();
            check.Severity.Should().Be(ScanSeverity.Error);
            check.SuggestedActionId.Should().Be("check_path_compatibility");
        }

        [Fact]
        public async Task CheckModsConfigIntegrity_MissingCore_Warns()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            var modsConfigPath = Path.Combine(configDir, "ModsConfig.xml");
            var doc = new XDocument(
                new XElement("ModsConfigData",
                    new XElement("version", "1.4"),
                    new XElement("activeMods",
                        new XElement("li", "some.mod")
                    )
                ));
            doc.Save(modsConfigPath);

            // Act
            var findings = await service.RunScanAsync(TestContext.Current.CancellationToken);

            // Assert
            var check = findings.FirstOrDefault(f => f.CheckId == "mods_config_integrity");
            check.Should().NotBeNull();
            check!.Passed.Should().BeFalse();
            check.Severity.Should().Be(ScanSeverity.Warning);
            check.SuggestedActionId.Should().Be("import_mod_list");
        }

        [Fact]
        public async Task CheckSavesFolderSize_OverThreshold_Warns()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            var savesDir = Path.Combine(configDir, "Saves");
            Directory.CreateDirectory(savesDir);

            var largeSave = Path.Combine(savesDir, "large.rws");
            using (var fs = new FileStream(largeSave, FileMode.Create))
            {
                var buffer = new byte[1024 * 1024];
                for (int i = 0; i < 600; i++) // 600MB
                {
                    await fs.WriteAsync(buffer, TestContext.Current.CancellationToken);
                }
            }

            // Act
            var findings = await service.RunScanAsync(TestContext.Current.CancellationToken);

            // Assert
            var check = findings.FirstOrDefault(f => f.CheckId == "saves_folder_size");
            check.Should().NotBeNull();
            check!.Passed.Should().BeFalse();
            check.Severity.Should().Be(ScanSeverity.Warning);
            check.SuggestedActionId.Should().Be("analyze_saves_quota");
        }

        #endregion

        #region Steam-Gated Checks (when Steam enabled)

        [Fact]
        public async Task CheckWorkshopMods_ZeroByteMod_Detects()
        {
            // Arrange
            var service = CreateService();
            TroubleshootingScanService.TestIsSteamInstallationOverride = () => true;

            var workshopModDir = Path.Combine(_gameInstance.WorkshopFolder!, "123456789");
            Directory.CreateDirectory(workshopModDir);
            // Empty directory = stuck mod

            // Act
            var findings = await service.RunScanAsync(TestContext.Current.CancellationToken);

            // Assert
            var check = findings.FirstOrDefault(f => f.CheckId == "workshop_mods");
            check.Should().NotBeNull();
            check!.Passed.Should().BeFalse();
            check.Severity.Should().Be(ScanSeverity.Warning);
            check.SuggestedActionId.Should().Be("reset_steam_workshop_mods");
        }

        [Fact]
        public async Task CheckOrphanedWorkshopMods_StaleAcfEntries_Detects()
        {
            // Arrange
            var service = CreateService();
            TroubleshootingScanService.TestIsSteamInstallationOverride = () => true;

            // Create ACF with stale entry
            var steamAppsDir = Path.Combine(_testTempDir, "Steam", "steamapps");
            Directory.CreateDirectory(steamAppsDir);
            var workshopDir = Path.Combine(steamAppsDir, "workshop");
            Directory.CreateDirectory(workshopDir);
            var contentDir = Path.Combine(workshopDir, "content", "294100");
            Directory.CreateDirectory(contentDir);

            // Point game instance to this workshop folder
            _gameInstance.WorkshopFolder = contentDir;

            // Create ACF with entry for 999999999 (not on disk)
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

            // Create only 123456789 on disk
            Directory.CreateDirectory(Path.Combine(contentDir, "123456789"));

            // Act
            var findings = await service.RunScanAsync(TestContext.Current.CancellationToken);

            // Assert
            var check = findings.FirstOrDefault(f => f.CheckId == "orphaned_workshop_mods");
            check.Should().NotBeNull();
            check!.Passed.Should().BeFalse();
            check.Severity.Should().Be(ScanSeverity.Warning);
            check.SuggestedActionId.Should().Be("clean_orphaned_workshop_mods");
            check.Detail.Should().Contain("ghost mods");
        }

        [Fact]
        public async Task CheckStuckDownloads_DownloadsFolderNotEmpty_Detects()
        {
            // Arrange
            var service = CreateService();
            TroubleshootingScanService.TestIsSteamInstallationOverride = () => true;

            var steamAppsDir = Path.Combine(_testTempDir, "Steam", "steamapps");
            Directory.CreateDirectory(steamAppsDir);
            var workshopDir = Path.Combine(steamAppsDir, "workshop");
            Directory.CreateDirectory(workshopDir);
            var contentDir = Path.Combine(workshopDir, "content", "294100");
            Directory.CreateDirectory(contentDir);
            _gameInstance.WorkshopFolder = contentDir;

            // Create stuck download
            var downloadsDir = Path.Combine(steamAppsDir, "workshop", "downloads");
            Directory.CreateDirectory(downloadsDir);
            File.WriteAllText(Path.Combine(downloadsDir, "stuck_download.tmp"), "data");

            // Act
            var findings = await service.RunScanAsync(TestContext.Current.CancellationToken);

            // Assert
            var check = findings.FirstOrDefault(f => f.CheckId == "stuck_downloads");
            check.Should().NotBeNull();
            check!.Passed.Should().BeFalse();
            check.Severity.Should().Be(ScanSeverity.Warning);
            check.SuggestedActionId.Should().Be("clear_download_cache");
        }

        #endregion

        #region RunChecksAsync Tests (subset semantics + gate skipping)

        [Fact]
        public async Task RunChecksAsync_EmptyCheckIds_ReturnsEmpty()
        {
            // Arrange
            var service = CreateService();

            // Act
            var findings = await service.RunChecksAsync(Array.Empty<string>(), TestContext.Current.CancellationToken);

            // Assert
            findings.Should().BeEmpty();
        }

        [Fact]
        public async Task RunChecksAsync_NullCheckIds_ReturnsEmpty()
        {
            // Arrange
            var service = CreateService();

            // Act
            var findings = await service.RunChecksAsync(null!, TestContext.Current.CancellationToken);

            // Assert
            findings.Should().BeEmpty();
        }

        [Fact]
        public async Task RunChecksAsync_SingleCheck_RunsOnlyThatCheck()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            var largeLog = Path.Combine(configDir, "Player.log");
            using (var fs = new FileStream(largeLog, FileMode.Create))
            {
                var buffer = new byte[1024 * 1024];
                for (int i = 0; i < 60; i++) // 60MB
                {
                    await fs.WriteAsync(buffer, TestContext.Current.CancellationToken);
                }
            }

            // Act - only request log_file_sizes check
            var findings = await service.RunChecksAsync(new[] { "log_file_sizes" }, TestContext.Current.CancellationToken);

            // Assert - only that check should be in results
            findings.Should().HaveCount(1);
            findings[0].CheckId.Should().Be("log_file_sizes");
            findings[0].Passed.Should().BeFalse();
            findings[0].Severity.Should().Be(ScanSeverity.Warning);
        }

        [Fact]
        public async Task RunChecksAsync_MultipleChecks_RunsOnlyThoseChecks()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            var largeLog = Path.Combine(configDir, "Player.log");
            using (var fs = new FileStream(largeLog, FileMode.Create))
            {
                var buffer = new byte[1024 * 1024];
                for (int i = 0; i < 60; i++)
                {
                    await fs.WriteAsync(buffer, TestContext.Current.CancellationToken);
                }
            }
            File.WriteAllText(Path.Combine(configDir, "Mod_Test.xml"), "not valid xml");

            // Act - request two specific checks
            var findings = await service.RunChecksAsync(new[] { "log_file_sizes", "mod_settings_xml" }, TestContext.Current.CancellationToken);

            // Assert - only those two checks
            findings.Should().HaveCount(2);
            findings.Select(f => f.CheckId).Should().BeEquivalentTo(new[] { "log_file_sizes", "mod_settings_xml" });
        }

        [Fact]
        public async Task RunChecksAsync_UnknownCheckId_IgnoredSilently()
        {
            // Arrange
            var service = CreateService();

            // Act - request a non-existent check ID
            var findings = await service.RunChecksAsync(new[] { "nonexistent_check", "log_file_sizes" }, TestContext.Current.CancellationToken);

            // Assert - only known check runs
            findings.Should().HaveCount(1);
            findings[0].CheckId.Should().Be("log_file_sizes");
        }

        [Fact]
        public async Task RunChecksAsync_WindowsOnlyCheck_SkippedOnLinux()
        {
            // Arrange
            var service = CreateService();
            TroubleshootingScanService.TestIsWindowsOverride = () => false;

            // Act - explicitly request the Windows-only check
            var findings = await service.RunChecksAsync(new[] { "config_folder_attributes" }, TestContext.Current.CancellationToken);

            // Assert - check should be skipped silently (not in findings)
            findings.Should().BeEmpty("Windows-only check should be skipped silently on Linux even when explicitly requested");
        }

        [Fact]
        public async Task RunChecksAsync_SteamGatedCheck_SkippedWhenNotSteam()
        {
            // Arrange
            var service = CreateService();
            TroubleshootingScanService.TestIsSteamInstallationOverride = () => false;

            // Act - explicitly request Steam-gated checks
            var findings = await service.RunChecksAsync(new[] { "workshop_mods", "orphaned_workshop_mods", "stuck_downloads" }, TestContext.Current.CancellationToken);

            // Assert - all should be skipped silently
            findings.Should().BeEmpty("Steam-gated checks should be skipped silently when not Steam even when explicitly requested");
        }

        [Fact]
        public async Task RunChecksAsync_SteamGatedCheck_RunsWhenSteam()
        {
            // Arrange
            var service = CreateService();
            TroubleshootingScanService.TestIsSteamInstallationOverride = () => true;

            var workshopModDir = Path.Combine(_gameInstance.WorkshopFolder!, "123456789");
            Directory.CreateDirectory(workshopModDir); // Empty = stuck mod

            // Act - request Steam-gated check
            var findings = await service.RunChecksAsync(new[] { "workshop_mods" }, TestContext.Current.CancellationToken);

            // Assert - check should run
            findings.Should().HaveCount(1);
            findings[0].CheckId.Should().Be("workshop_mods");
            findings[0].Passed.Should().BeFalse();
            findings[0].Severity.Should().Be(ScanSeverity.Warning);
        }

        [Fact]
        public async Task RunChecksAsync_CaseInsensitiveCheckIds()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            var largeLog = Path.Combine(configDir, "Player.log");
            using (var fs = new FileStream(largeLog, FileMode.Create))
            {
                var buffer = new byte[1024 * 1024];
                for (int i = 0; i < 60; i++)
                {
                    await fs.WriteAsync(buffer, TestContext.Current.CancellationToken);
                }
            }

            // Act - mixed case check IDs
            var findings = await service.RunChecksAsync(new[] { "LOG_FILE_SIZES", "Log_File_Sizes" }, TestContext.Current.CancellationToken);

            // Assert - should run once (deduplicated by HashSet)
            findings.Should().HaveCount(1);
            findings[0].CheckId.Should().Be("log_file_sizes");
        }

        [Fact]
        public async Task RunChecksAsync_CancellationToken_Respected()
        {
            // Arrange
            var service = CreateService();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                service.RunChecksAsync(new[] { "log_file_sizes" }, cts.Token));
        }

        [Fact]
        public async Task RunChecksAsync_ConfigFolderAttributes_WindowsOnly_RunsOnWindows()
        {
            // Arrange
            var service = CreateService();
            TroubleshootingScanService.TestIsWindowsOverride = () => true;

            // Act
            var findings = await service.RunChecksAsync(new[] { "config_folder_attributes" }, TestContext.Current.CancellationToken);

            // Assert - check should run on Windows
            findings.Should().HaveCount(1);
            findings[0].CheckId.Should().Be("config_folder_attributes");
        }

        #endregion

        #region Log Signatures Check Tests

        [Fact]
        public async Task CheckLogSignaturesAsync_PlantedNullReference_FindsIssue()
        {
            // Arrange
            var service = CreateService();

            // Locator override returns dummy paths so reader gets called
            TroubleshootingScanService.TestLogLocatorOverride = (configPath, majorVersion) =>
                new LogSourcePaths("dummy_current.log", null);

            // Reader override returns test content
            TroubleshootingScanService.TestLogReaderOverride = (path, maxBytes, ct) =>
                Task.FromResult(new LogHeadResult
                {
                    Found = true,
                    Text = @"Initialize engine version: 2022.3.21f1 (1.5.4000)
NullReferenceException: Object reference not set to an instance of an object
  at Verse.Thing.Def.get_ThingDef () [0x00000] in <00000000000000000000000000000000>:0
  at RimWorld.Pawn_HealthTracker.CheckForStateChange () [0x00000] in <00000000000000000000000000000000>:0
NullReferenceException: Object reference not set to an instance of an object
  at Verse.AI.JobDriver.TryActuallyStartNextToil () [0x00000] in <00000000000000000000000000000000>:0",
                    FileSize = 1000,
                    BytesRead = 1000,
                    CoversWholeFile = true
                });

            // Act
            var findings = await service.RunChecksAsync(new[] { "log_signatures" }, TestContext.Current.CancellationToken);

            // Assert
            findings.Should().HaveCount(1);
            var finding = findings[0];
            finding.CheckId.Should().Be("log_signatures");
            finding.Passed.Should().BeFalse();
            finding.Severity.Should().Be(ScanSeverity.Error);
            finding.Title.Should().Be("Null Reference Exception");
            finding.Detail.Should().Contain("A mod or the game dereferenced a null object");
            finding.Detail.Should().Contain("What to try:");
            finding.Detail.Should().Contain("run Maintenance");
            finding.SuggestedActionId.Should().BeNull("Honesty: no fix action for log signatures");
            finding.Metadata.Should().ContainKey("logSignature");
            ((bool)finding.Metadata!["logSignature"]).Should().BeTrue();
            ((int)finding.Metadata!["count"]).Should().Be(2); // Two occurrences in the log
            ((long)finding.Metadata!["firstLineNumber"]).Should().Be(2); // First occurrence at line 2
        }

        [Fact]
        public async Task CheckLogSignaturesAsync_PlantedMultipleSignatures_FindsAll()
        {
            // Arrange
            var service = CreateService();

            TroubleshootingScanService.TestLogLocatorOverride = (configPath, majorVersion) =>
                new LogSourcePaths("dummy_current.log", null);

            TroubleshootingScanService.TestLogReaderOverride = (path, maxBytes, ct) =>
                Task.FromResult(new LogHeadResult
                {
                    Found = true,
                    Text = @"Initialize engine version: 2022.3.21f1 (1.5.4000)
NullReferenceException: Object reference not set to an instance of an object
  at Verse.Thing.Def.get_ThingDef () [0x00000] in <00000000000000000000000000000000>:0
MissingMethodException: Method not found: 'Void Verse.Thing::SetDef()'
  at SomeMod.SomeClass.SomeMethod () [0x00000] in <00000000000000000000000000000000>:0
Tried loading mod with the same packageId multiple times: SomeMod
Could not find a Def named SomeDef from SomeMod",
                    FileSize = 1000,
                    BytesRead = 1000,
                    CoversWholeFile = true
                });

            // Act
            var findings = await service.RunChecksAsync(new[] { "log_signatures" }, TestContext.Current.CancellationToken);

            // Assert - should find 4 distinct signatures (NullReferenceException, MissingMethodException, Duplicate Mod, Missing Def)
            findings.Should().HaveCount(4);
            findings.All(f => f.CheckId == "log_signatures").Should().BeTrue();
            findings.All(f => f.SuggestedActionId == null).Should().BeTrue("Honesty: no fix actions for log signatures");

            var titles = findings.Select(f => f.Title).ToList();
            titles.Should().Contain("Null Reference Exception");
            titles.Should().Contain("Missing Method Exception");
            titles.Should().Contain("Duplicate Mod");
            titles.Should().Contain("Missing Def");

            // Ordering: Errors first, then Warnings, then Info
            var errorFindings = findings.Where(f => f.Severity == ScanSeverity.Error).ToList();
            var warningFindings = findings.Where(f => f.Severity == ScanSeverity.Warning).ToList();
            errorFindings.Should().HaveCount(2); // NullReferenceException, MissingMethodException
            warningFindings.Should().HaveCount(2); // Duplicate Mod, Missing Def
        }

        [Fact]
        public async Task CheckLogSignaturesAsync_NoSignatureHit_ReturnsZeroFindings()
        {
            // Arrange
            var service = CreateService();

            TroubleshootingScanService.TestLogLocatorOverride = (configPath, majorVersion) =>
                new LogSourcePaths("dummy_current.log", null);

            TroubleshootingScanService.TestLogReaderOverride = (path, maxBytes, ct) =>
                Task.FromResult(new LogHeadResult
                {
                    Found = true,
                    Text = @"Initialize engine version: 2022.3.21f1 (1.5.4000)
Loading mod: Core
Loading mod: Royalty
Loading mod: Ideology
Game started successfully
Saving game...",
                    FileSize = 1000,
                    BytesRead = 1000,
                    CoversWholeFile = true
                });

            // Act
            var findings = await service.RunChecksAsync(new[] { "log_signatures" }, TestContext.Current.CancellationToken);

            // Assert - no findings when no signatures match
            findings.Should().BeEmpty();
        }

        [Fact]
        public async Task CheckLogSignaturesAsync_MissingLogFile_ReturnsZeroFindingsNoFailure()
        {
            // Arrange
            var service = CreateService();

            // Locator returns null paths (simulating missing log files)
            TroubleshootingScanService.TestLogLocatorOverride = (configPath, majorVersion) =>
                new LogSourcePaths(null, null);

            // Act
            var findings = await service.RunChecksAsync(new[] { "log_signatures" }, TestContext.Current.CancellationToken);

            // Assert - zero findings, no exception thrown
            findings.Should().BeEmpty();
        }

        [Fact]
        public async Task CheckLogSignaturesAsync_UnreadableLogFile_ReturnsZeroFindingsNoFailure()
        {
            // Arrange
            var service = CreateService();

            // Locator returns paths but reader returns not found (simulating unreadable)
            TroubleshootingScanService.TestLogLocatorOverride = (configPath, majorVersion) =>
                new LogSourcePaths("dummy_current.log", null);

            TroubleshootingScanService.TestLogReaderOverride = (path, maxBytes, ct) =>
                Task.FromResult(new LogHeadResult { Found = false });

            // Act
            var findings = await service.RunChecksAsync(new[] { "log_signatures" }, TestContext.Current.CancellationToken);

            // Assert - zero findings, no exception thrown
            findings.Should().BeEmpty();
        }

        [Fact]
        public async Task CheckLogSignaturesAsync_ReadsBothCurrentAndPreviousLogs()
        {
            // Arrange
            var service = CreateService();
            var callCount = 0;

            TroubleshootingScanService.TestLogLocatorOverride = (configPath, majorVersion) =>
                new LogSourcePaths("dummy_current.log", "dummy_previous.log");

            // Reader override returns different content for each call
            TroubleshootingScanService.TestLogReaderOverride = (path, maxBytes, ct) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return Task.FromResult(new LogHeadResult
                    {
                        Found = true,
                        Text = @"NullReferenceException: Current log error",
                        FileSize = 100,
                        BytesRead = 100,
                        CoversWholeFile = true
                    });
                }
                else
                {
                    return Task.FromResult(new LogHeadResult
                    {
                        Found = true,
                        Text = @"MissingMethodException: Previous log error",
                        FileSize = 100,
                        BytesRead = 100,
                        CoversWholeFile = true
                    });
                }
            };

            // Act
            var findings = await service.RunChecksAsync(new[] { "log_signatures" }, TestContext.Current.CancellationToken);

            // Assert - should find signatures from both logs
            findings.Should().HaveCount(2);
            var titles = findings.Select(f => f.Title).ToList();
            titles.Should().Contain("Null Reference Exception");
            titles.Should().Contain("Missing Method Exception");
        }

        [Fact]
        public async Task RunScanAsync_LogSignaturesCheck_RunsInBulkScan()
        {
            // Arrange
            var service = CreateService();

            TroubleshootingScanService.TestLogLocatorOverride = (configPath, majorVersion) =>
                new LogSourcePaths("dummy.log", null);

            TroubleshootingScanService.TestLogReaderOverride = (path, maxBytes, ct) =>
                Task.FromResult(new LogHeadResult
                {
                    Found = true,
                    Text = @"NullReferenceException: Bulk scan test",
                    FileSize = 100,
                    BytesRead = 100,
                    CoversWholeFile = true
                });

            // Act - full scan
            var findings = await service.RunScanAsync(TestContext.Current.CancellationToken);

            // Assert - log_signatures check should be included in bulk scan
            var logSigFindings = findings.Where(f => f.CheckId == "log_signatures").ToList();
            logSigFindings.Should().HaveCount(1);
            logSigFindings[0].Title.Should().Be("Null Reference Exception");
        }

        #endregion
    }
}