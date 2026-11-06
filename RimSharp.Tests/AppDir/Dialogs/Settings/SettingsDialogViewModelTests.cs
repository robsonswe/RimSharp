#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NSubstitute;
using RimSharp.AppDir.Dialogs;
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
    }
}
