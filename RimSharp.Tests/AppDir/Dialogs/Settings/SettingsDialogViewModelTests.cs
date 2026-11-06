#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NSubstitute;
using RimSharp.AppDir.Dialogs;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Infrastructure.Logging;
using Xunit;

namespace RimSharp.Tests.AppDir.Dialogs.Settings
{
    public class SettingsDialogViewModelTests
    {
        private readonly IConfigService _mockConfigService;
        private readonly IPathService _mockPathService;
        private readonly ILoggerService _mockLogger;

        public SettingsDialogViewModelTests()
        {
            _mockConfigService = Substitute.For<IConfigService>();
            _mockPathService = Substitute.For<IPathService>();
            _mockLogger = Substitute.For<ILoggerService>();
        }

        [Fact]
        public void ModsFolderPath_WhenSet_ShouldBeSaved_WhenSaveIsCalled()
        {
            var viewModel = new SettingsDialogViewModel(
                _mockConfigService,
                _mockPathService,
                null,
                _mockLogger,
                null
            );

            viewModel.ModsFolderPath = @"D:\CustomMods";
            viewModel.Save();

            _mockConfigService.Received(1).SetConfigValue("mods_folder", @"D:\CustomMods");
        }

        [Fact]
        public void ModsFolderPath_WhenCleared_ShouldBeCleared_WhenSaveIsCalled()
        {
            var viewModel = new SettingsDialogViewModel(
                _mockConfigService,
                _mockPathService,
                null,
                _mockLogger,
                null
            );

            viewModel.ModsFolderPath = string.Empty;
            viewModel.Save();

            _mockConfigService.Received(1).SetConfigValue("mods_folder", string.Empty);
        }

        [Fact]
        public void ModsFolderPath_WhenInvalid_ShouldWarnButNotClear_WhenSaveIsCalled()
        {
            var viewModel = new SettingsDialogViewModel(
                _mockConfigService,
                _mockPathService,
                null,
                _mockLogger,
                null
            );

            viewModel.ModsFolderPath = @"D:\DoesNotExist";
            viewModel.Save();

            _mockLogger.Received(1).LogWarning(
                Arg.Is<string>(s => s.Contains("does not exist")),
                Arg.Any<string>()
            );

            // The invalid value should still be saved (warned but not cleared)
            _mockConfigService.Received(1).SetConfigValue("mods_folder", @"D:\DoesNotExist");
        }

        [Fact]
        public void IsGamePathSet_ShouldBeFalse_Initially()
        {
            var viewModel = new SettingsDialogViewModel(
                _mockConfigService,
                _mockPathService,
                null,
                _mockLogger,
                null
            );

            viewModel.IsGamePathSet.Should().BeFalse();
        }

        [Fact]
        public void IsGamePathSet_ShouldBeTrue_WhenGamePathIsSet()
        {
            var viewModel = new SettingsDialogViewModel(
                _mockConfigService,
                _mockPathService,
                null,
                _mockLogger,
                null
            );

            viewModel.GamePath = @"D:\Game";
            viewModel.IsGamePathSet.Should().BeTrue();
        }

        [Fact]
        public void IsGamePathSet_ShouldBeFalse_WhenGamePathIsEmpty()
        {
            var viewModel = new SettingsDialogViewModel(
                _mockConfigService,
                _mockPathService,
                null,
                _mockLogger,
                null
            );

            viewModel.IsGamePathSet.Should().BeFalse();
        }

        [Fact]
        public void IsModsFolderEnabled_ShouldBeFalse_Initially()
        {
            var viewModel = new SettingsDialogViewModel(
                _mockConfigService,
                _mockPathService,
                null,
                _mockLogger,
                null
            );

            viewModel.IsModsFolderEnabled.Should().BeFalse();
        }

        [Fact]
        public void IsModsFolderEnabled_ShouldBeTrue_WhenGamePathIsSet()
        {
            var viewModel = new SettingsDialogViewModel(
                _mockConfigService,
                _mockPathService,
                null,
                _mockLogger,
                null
            );

            viewModel.GamePath = @"D:\Game";
            viewModel.IsModsFolderEnabled.Should().BeTrue();
        }

        [Fact]
        public void ModsFolderPath_WhenGamePathSetFromEmpty_ShouldShowDerivedPath()
        {
            // Regression: the exact first-run scenario — no config, user picks a game
            // path in the dialog; the mods field must show <game>\Mods immediately.
            var viewModel = new SettingsDialogViewModel(
                _mockConfigService,
                _mockPathService,
                null,
                _mockLogger,
                null
            );

            viewModel.GamePath = @"D:\Game";

            viewModel.ModsFolderPath.Should().Be(Path.Combine(@"D:\Game", "Mods"));
        }

        [Fact]
        public void ModsFolderPath_WhenGamePathChangesWhileAutoSynced_ShouldFollowNewGamePath()
        {
            var viewModel = new SettingsDialogViewModel(
                _mockConfigService,
                _mockPathService,
                null,
                _mockLogger,
                null
            );

            viewModel.GamePath = @"D:\Game";
            var first = viewModel.ModsFolderPath; // D:\Game\Mods

            viewModel.GamePath = @"E:\OtherGame";

            viewModel.ModsFolderPath.Should().Be(Path.Combine(@"E:\OtherGame", "Mods"));
        }

        [Fact]
        public void ModsFolderPath_WhenUserOverrides_ShouldNotFollowGamePathChanges()
        {
            var viewModel = new SettingsDialogViewModel(
                _mockConfigService,
                _mockPathService,
                null,
                _mockLogger,
                null
            );

            viewModel.GamePath = @"D:\Game";
            viewModel.ModsFolderPath = @"D:\CustomMods"; // manual override, ≠ derived

            viewModel.GamePath = @"E:\OtherGame";

            viewModel.ModsFolderPath.Should().Be(@"D:\CustomMods");
        }

        [Fact]
        public void ModsFolderPath_WhenRevertedToDerivedPath_ShouldResumeAutoSync()
        {
            var viewModel = new SettingsDialogViewModel(
                _mockConfigService,
                _mockPathService,
                null,
                _mockLogger,
                null
            );

            viewModel.GamePath = @"D:\Game";
            viewModel.ModsFolderPath = @"D:\CustomMods";

            // User types the derived path back → auto-sync state (canonical empty)
            viewModel.ModsFolderPath = Path.Combine(@"D:\Game", "Mods");

            viewModel.GamePath = @"E:\OtherGame";
            viewModel.ModsFolderPath.Should().Be(Path.Combine(@"E:\OtherGame", "Mods"));
        }

        [Fact]
        public void Save_WhenAutoSyncedWithDerivedPath_ShouldPersistCanonicalEmptyValue()
        {
            viewModel_GamePathSet(out var viewModel);

            viewModel.Save();

            // Auto-synced state stores EMPTY config value (= derive at runtime)
            _mockConfigService.Received(1).SetConfigValue("mods_folder", string.Empty);
        }

        [Fact]
        public void Save_WhenAutoSyncedAndGamePathIsE_ShouldNotStoreOldDerivedPath()
        {
            // Regression: storing the concrete derived path while "in sync" would become
            // a stale override if the game path later changes outside this dialog.
            var viewModel = new SettingsDialogViewModel(
                _mockConfigService,
                _mockPathService,
                null,
                _mockLogger,
                null
            );
            viewModel.GamePath = @"D:\Game";

            viewModel.Save();

            _mockConfigService.DidNotReceive().SetConfigValue("mods_folder", Path.Combine(@"D:\Game", "Mods"));
        }

        private void viewModel_GamePathSet(out SettingsDialogViewModel viewModel)
        {
            viewModel = new SettingsDialogViewModel(
                _mockConfigService,
                _mockPathService,
                null,
                _mockLogger,
                null
            );
            viewModel.GamePath = @"D:\Game";
        }

        [Fact]
        public void ResolvedModsPath_WhenGamePathSet_ShouldShowResolvedPath()
        {
            _mockPathService.GetGamePath().Returns(@"D:\Game");
            _mockPathService.GetModsPath().Returns(@"D:\Game\Mods");

            var viewModel = new SettingsDialogViewModel(
                _mockConfigService,
                _mockPathService,
                null,
                _mockLogger,
                null
            );

            viewModel.ResolvedModsPath.Should().Be(@"D:\Game\Mods");
        }

        [Fact]
        public void ResolvedModsPath_WhenNoGamePath_ShouldShowNoPathMessage()
        {
            _mockPathService.GetGamePath().Returns(string.Empty);

            var viewModel = new SettingsDialogViewModel(
                _mockConfigService,
                _mockPathService,
                null,
                _mockLogger,
                null
            );

            viewModel.ResolvedModsPath.Should().Be("No game path set");
        }

        [Fact]
        public void ResolvedModsPath_WhenOverrideSet_ShouldShowOverridePath()
        {
            _mockPathService.GetGamePath().Returns(@"D:\Game");
            _mockPathService.GetModsPath().Returns(@"D:\CustomMods");

            var viewModel = new SettingsDialogViewModel(
                _mockConfigService,
                _mockPathService,
                null,
                _mockLogger,
                null
            );

            viewModel.ResolvedModsPath.Should().Be(@"D:\CustomMods");
        }

        // ---- Manual Steam confirmation flow (ValidateSteamClaim-backed Save) ----

        [Fact]
        public void Save_WhenSteamSelectedWithoutCandidateAndClaimInvalid_ShouldAbortWithoutPersisting()
        {
            var stubDetection = CreateStubDetectionWithoutCandidates();
            var mockConfig = Substitute.For<IConfigService>();
            var mockLogger = Substitute.For<ILoggerService>();
            var viewModel = new SettingsDialogViewModel(
                mockConfig,
                _mockPathService,
                null,
                mockLogger,
                null,
                stubDetection
            );

            viewModel.SteamCandidate.Should().BeNull(); // stubbed: no auto-detection
            viewModel.SelectedDistribution = GameDistribution.Steam;
            viewModel.GamePath = @"Q:\No_Such_Dir\RimWorld"; // fails every structural check

            viewModel.Save();

            // Refusal must write NOTHING — no in-memory config writes, no SaveConfig
            mockConfig.DidNotReceive().SaveConfig();
            mockConfig.DidNotReceive().SetConfigValue("steam_installation", "true");
            mockLogger.Received(1).LogWarning(
                Arg.Is<string>(s => s.Contains("rejected") && s.Contains("Q:\\No_Such_Dir\\RimWorld")),
                Arg.Any<string>());
        }

        [Fact]
        public void Save_WhenSteamSelectedWithoutCandidateAndClaimValid_ShouldPersistSteamSettings()
        {
            // Build a real, fully-valid Steam layout on disk for the structural check
            var tempRoot = Path.Combine(Path.GetTempPath(), "RimSharpTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var gameDir = Path.Combine(tempRoot, "steamapps", "common", "RimWorld");
                var workshopContent = Path.Combine(tempRoot, "steamapps", "workshop", "content", "294100");
                Directory.CreateDirectory(gameDir);
                Directory.CreateDirectory(workshopContent);
                File.WriteAllText(Path.Combine(gameDir, "RimWorldWin64.exe"), "");
                File.WriteAllText(Path.Combine(gameDir, "Version.txt"), "1.6.4518 rev1234");
                File.WriteAllText(Path.Combine(tempRoot, "steamapps", "appmanifest_294100.acf"), "\"294100\"{}");
                File.WriteAllText(Path.Combine(tempRoot, "steamapps", "workshop", "appworkshop_294100.acf"), "\"appworkshop_294100\"{}");

                var stubDetection = CreateStubDetectionWithoutCandidates();
                var mockConfig = Substitute.For<IConfigService>();
                var viewModel = new SettingsDialogViewModel(
                    mockConfig,
                    _mockPathService,
                    null,
                    _mockLogger,
                    null,
                    stubDetection
                );

                viewModel.SelectedDistribution = GameDistribution.Steam;
                viewModel.GamePath = gameDir;

                viewModel.Save();

                mockConfig.Received(1).SetConfigValue("steam_installation", "true");
                mockConfig.Received(1).SetConfigValue("steam_workshop_folder", workshopContent);
                mockConfig.Received(1).SetConfigValue("steam_integration", "true");
                mockConfig.Received(1).SaveConfig();
            }
            finally
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
            }
        }

        [Fact]
        public void Save_WhenSteamSelectedWithoutCandidateAndNoWorkshopContent_ShouldRefuseAndNotPersist()
        {
            // Valid Steam layout but zero workshop mods downloaded: there is no content
            // folder to validate against GameInstance.ValidateAndFix semantics (which
            // require an existing workshop folder), so the claim is refused — persisting
            // it would be stripped at next startup anyway. Nothing is written.
            var tempRoot = Path.Combine(Path.GetTempPath(), "RimSharpTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var gameDir = Path.Combine(tempRoot, "steamapps", "common", "RimWorld");
                Directory.CreateDirectory(gameDir);
                Directory.CreateDirectory(Path.Combine(tempRoot, "steamapps", "workshop"));
                File.WriteAllText(Path.Combine(gameDir, "RimWorldWin64.exe"), "");
                File.WriteAllText(Path.Combine(gameDir, "Version.txt"), "1.6.4518 rev1234");
                File.WriteAllText(Path.Combine(tempRoot, "steamapps", "appmanifest_294100.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"294100\"\n\t\"installdir\"\t\t\"RimWorld\"\n}\n");
                File.WriteAllText(Path.Combine(tempRoot, "steamapps", "workshop", "appworkshop_294100.acf"), "\"appworkshop_294100\"{}");

                var stubDetection = CreateStubDetectionWithoutCandidates();
                var mockConfig = Substitute.For<IConfigService>();
                var mockLogger = Substitute.For<ILoggerService>();
                var viewModel = new SettingsDialogViewModel(
                    mockConfig,
                    _mockPathService,
                    null,
                    mockLogger,
                    null,
                    stubDetection
                );

                viewModel.SelectedDistribution = GameDistribution.Steam;
                viewModel.GamePath = gameDir;

                viewModel.Save();

                // Refused claim: nothing persisted, dialog stays open, reason logged
                mockConfig.DidNotReceive().SaveConfig();
                mockConfig.DidNotReceive().SetConfigValue("steam_installation", "true");
                mockLogger.Received(1).LogWarning(
                    Arg.Is<string>(s => s.Contains("rejected")),
                    Arg.Any<string>());
            }
            finally
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
            }
        }

        private static GameInstallationDetectionService CreateStubDetectionWithoutCandidates()
        {
            var stub = Substitute.ForPartsOf<GameInstallationDetectionService>();
            stub.DetectSteamInstallation().Returns((GameInstallationCandidate?)null);
            stub.DetectNonSteamInstallation().Returns((GameInstallationCandidate?)null);
            return stub;
        }

        // ---- Manual Steam confirmation UX (mode flag, hint, Check-now command) ----

        [Fact]
        public void IsManualSteamClaim_ShouldBeFalse_WithNonSteamSelected()
        {
            var viewModel = new SettingsDialogViewModel(
                _mockConfigService, _mockPathService, null, _mockLogger, null,
                CreateStubDetectionWithoutCandidates());

            viewModel.IsManualSteamClaim.Should().BeFalse();
            viewModel.ManualSteamClaimHint.Should().BeEmpty();
        }

        [Fact]
        public void IsManualSteamClaim_ShouldBeTrue_WhenSteamSelectedWithoutCandidate()
        {
            var viewModel = new SettingsDialogViewModel(
                _mockConfigService, _mockPathService, null, _mockLogger, null,
                CreateStubDetectionWithoutCandidates());

            viewModel.SelectedDistribution = GameDistribution.Steam;

            viewModel.IsManualSteamClaim.Should().BeTrue();
            viewModel.ManualSteamClaimHint.Should().Contain("not auto-detected");
        }

        [Fact]
        public void ValidateSteamClaimCommand_WhenNotPending_ShouldLeaveStatusEmpty()
        {
            var viewModel = new SettingsDialogViewModel(
                _mockConfigService, _mockPathService, null, _mockLogger, null,
                CreateStubDetectionWithoutCandidates());

            viewModel.ValidateSteamClaimCommand.Execute(null);

            viewModel.ManualSteamClaimStatus.Should().BeEmpty();
        }

        [Fact]
        public void ValidateSteamClaimCommand_WhenClaimInvalid_ShouldShowFailureStatus()
        {
            var viewModel = new SettingsDialogViewModel(
                _mockConfigService, _mockPathService, null, _mockLogger, null,
                CreateStubDetectionWithoutCandidates());
            viewModel.SelectedDistribution = GameDistribution.Steam;
            viewModel.GamePath = @"Q:\No_Such_Dir\RimWorld";

            viewModel.ValidateSteamClaimCommand.Execute(null);

            viewModel.ManualSteamClaimStatus.Should().NotBeEmpty();
            viewModel.ManualSteamClaimStatus.Should().Contain("does not exist");
        }

        [Fact]
        public void ValidateSteamClaimCommand_WhenClaimValid_ShouldShowSuccessStatus()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "RimSharpTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var gameDir = Path.Combine(tempRoot, "steamapps", "common", "RimWorld");
                var workshopContent = Path.Combine(tempRoot, "steamapps", "workshop", "content", "294100");
                Directory.CreateDirectory(gameDir);
                Directory.CreateDirectory(workshopContent);
                File.WriteAllText(Path.Combine(gameDir, "RimWorldWin64.exe"), "");
                File.WriteAllText(Path.Combine(gameDir, "Version.txt"), "1.6.4518 rev1234");
                File.WriteAllText(Path.Combine(tempRoot, "steamapps", "appmanifest_294100.acf"),
                    "\"AppState\"\n{\n\t\"appid\"\t\t\"294100\"\n\t\"installdir\"\t\t\"RimWorld\"\n}\n");
                File.WriteAllText(Path.Combine(tempRoot, "steamapps", "workshop", "appworkshop_294100.acf"),
                    "\"appworkshop_294100\"{}");

                var viewModel = new SettingsDialogViewModel(
                    _mockConfigService, _mockPathService, null, _mockLogger, null,
                    CreateStubDetectionWithoutCandidates());
                viewModel.SelectedDistribution = GameDistribution.Steam;
                viewModel.GamePath = gameDir;

                viewModel.ValidateSteamClaimCommand.Execute(null);

                viewModel.ManualSteamClaimStatus.Should().Contain("✓");
                viewModel.ManualSteamClaimStatus.Should().Contain(workshopContent);
            }
            finally
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
            }
        }
    }
}
