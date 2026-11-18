#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.AppDir.Dialogs;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Logging;
using RimSharp.Infrastructure.Mods.ModInfo;
using RimSharp.Infrastructure.Mods.Replacements;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Infrastructure.Mods.Versions;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.AppDir.Dialogs.Settings
{
    public class SettingsDialogViewModelExternalSourcesTests : IDisposable
    {
        private readonly string _configPath;
        private readonly IConfigService _configService;
        private readonly IPathService _mockPathService;
        private readonly ILoggerService _mockLogger;
        private readonly ISteamClaimValidator _mockSteamClaimValidator;
        private readonly IRuleSourceManager _mockRuleSourceManager;
        private readonly IModInfoSourceManager _mockModInfoSourceManager;
        private readonly IModReplacementSourceManager _mockReplacementSourceManager;
        private readonly INoVersionWarningSourceManager _mockNoVersionWarningSourceManager;

        public SettingsDialogViewModelExternalSourcesTests()
        {
            _configPath = Path.Combine(Path.GetTempPath(), "RimSharp_ExtVmTest_" + Guid.NewGuid().ToString("N") + ".cfg");
            _configService = new ConfigService(_configPath);

            _mockPathService = Substitute.For<IPathService>();
            _mockLogger = Substitute.For<ILoggerService>();
            _mockSteamClaimValidator = Substitute.For<ISteamClaimValidator>();
            _mockSteamClaimValidator.ValidateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(SteamClaimValidationResult.Fail("stub rejection")));

            _mockPathService.GetModsPathFor(Arg.Any<string>())
                .Returns(callInfo => string.IsNullOrWhiteSpace(callInfo.Arg<string>())
                    ? string.Empty
                    : Path.Combine(callInfo.Arg<string>(), "Mods"));

            _mockRuleSourceManager = Substitute.For<IRuleSourceManager>();
            _mockRuleSourceManager.Registry.Returns(new ModRuleSourceRegistry(_configService));

            _mockModInfoSourceManager = Substitute.For<IModInfoSourceManager>();
            _mockModInfoSourceManager.Registry.Returns(new ModInfoSourceRegistry(_configService));

            _mockReplacementSourceManager = Substitute.For<IModReplacementSourceManager>();
            _mockReplacementSourceManager.Registry.Returns(new ModReplacementSourceRegistry(_configService));

            _mockNoVersionWarningSourceManager = Substitute.For<INoVersionWarningSourceManager>();
            _mockNoVersionWarningSourceManager.Registry.Returns(new NoVersionWarningSourceRegistry(_configService));
        }

        public void Dispose()
        {
            if (File.Exists(_configPath))
            {
                File.Delete(_configPath);
            }
        }

        private SettingsDialogViewModel CreateViewModel()
        {
            var stubDetection = Substitute.ForPartsOf<GameInstallationDetectionService>();
            stubDetection.DetectSteamInstallation().Returns((GameInstallationCandidate?)null);
            stubDetection.DetectNonSteamInstallation().Returns((GameInstallationCandidate?)null);

            return new SettingsDialogViewModel(
                _configService,
                _mockPathService,
                fileDialogService: null,
                _mockLogger,
                providers: null,
                _mockSteamClaimValidator,
                stubDetection,
                dialogService: null,
                showAiProviders: false,
                ruleSourceManager: _mockRuleSourceManager,
                modInfoSourceManager: _mockModInfoSourceManager,
                replacementSourceManager: _mockReplacementSourceManager,
                noVersionWarningSourceManager: _mockNoVersionWarningSourceManager);
        }

        [Fact]
        public void ModInfoSources_ShouldLoadCuratedSource()
        {
            var vm = CreateViewModel();

            vm.ModInfoSources.Should().ContainSingle();
            var row = vm.ModInfoSources[0];
            row.Id.Should().Be(ModInfoSourceRegistry.CuratedSourceId);
            row.Locked.Should().BeTrue();
            row.LockedBadge.Should().Be("built-in");
            row.CanUpdate.Should().BeFalse();
        }

        [Fact]
        public void ReplacementSources_WhenModInstalled_ShouldIncludeModRow()
        {
            _mockReplacementSourceManager.IsModInstalled("3396308787").Returns(true);

            var vm = CreateViewModel();

            vm.ReplacementSources.Should().HaveCount(3);

            var modRow = vm.ReplacementSources.FirstOrDefault(r => r.Id == ModReplacementSourceRegistry.ModSourceId);
            modRow.Should().NotBeNull();
            modRow!.IsMod.Should().BeTrue();
            modRow.Locked.Should().BeTrue();
            modRow.LockedBadge.Should().Be("mod");
            modRow.CanUpdate.Should().BeFalse();
        }

        [Fact]
        public void ReplacementSources_WhenModNotInstalled_ShouldExcludeModRow()
        {
            _mockReplacementSourceManager.IsModInstalled("3396308787").Returns(false);

            var vm = CreateViewModel();

            vm.ReplacementSources.Should().HaveCount(2);
            vm.ReplacementSources.Should().NotContain(r => r.Id == ModReplacementSourceRegistry.ModSourceId);
            vm.ReplacementSources.Should().Contain(r => r.Id == ModReplacementSourceRegistry.CommunitySourceId);
            vm.ReplacementSources.Should().Contain(r => r.Id == ModReplacementSourceRegistry.CuratedSourceId);
        }

        [Fact]
        public void NoVersionWarningSources_WhenModInstalled_ShouldIncludeModRow()
        {
            _mockNoVersionWarningSourceManager.IsModInstalled("2599504692").Returns(true);

            var vm = CreateViewModel();

            vm.NoVersionWarningSources.Should().HaveCount(2);

            var modRow = vm.NoVersionWarningSources.FirstOrDefault(r => r.Id == NoVersionWarningSourceRegistry.ModSourceId);
            modRow.Should().NotBeNull();
            modRow!.IsMod.Should().BeTrue();
            modRow.Locked.Should().BeTrue();
            modRow.LockedBadge.Should().Be("mod");
        }

        [Fact]
        public void NoVersionWarningSources_WhenModNotInstalled_ShouldExcludeModRow()
        {
            _mockNoVersionWarningSourceManager.IsModInstalled("2599504692").Returns(false);

            var vm = CreateViewModel();

            vm.NoVersionWarningSources.Should().ContainSingle();
            vm.NoVersionWarningSources[0].Id.Should().Be(NoVersionWarningSourceRegistry.CommunitySourceId);
            vm.NoVersionWarningSources[0].Locked.Should().BeFalse();
        }

        [Fact]
        public void ReorderingAndToggling_ExternalSources_ShouldWork()
        {
            _mockReplacementSourceManager.IsModInstalled("3396308787").Returns(true);

            var vm = CreateViewModel();

            // Toggle first row
            var firstRow = vm.ReplacementSources[0];
            firstRow.Enabled.Should().BeTrue();
            firstRow.ToggleEnabledCommand.Execute(null);
            firstRow.Enabled.Should().BeFalse();

            // Move first row down
            var secondRowId = vm.ReplacementSources[1].Id;
            vm.MoveReplacementSourceDownCommand.Execute(firstRow);
            vm.ReplacementSources[1].Id.Should().Be(firstRow.Id);
            vm.ReplacementSources[0].Id.Should().Be(secondRowId);

            // Move back up
            vm.MoveReplacementSourceUpCommand.Execute(vm.ReplacementSources[1]);
            vm.ReplacementSources[0].Id.Should().Be(firstRow.Id);
        }

        [Fact]
        public void Save_WhenModUninstalled_ShouldPreserveModInRegistry()
        {
            _mockReplacementSourceManager.IsModInstalled("3396308787").Returns(false);
            _mockNoVersionWarningSourceManager.IsModInstalled("2599504692").Returns(false);

            var vm = CreateViewModel();

            // Save changes
            vm.Save();

            // Check that registries committed changes and kept mod records
            _mockReplacementSourceManager.Received().CommitRegistryChanges();
            _mockNoVersionWarningSourceManager.Received().CommitRegistryChanges();
            _mockModInfoSourceManager.Received().CommitRegistryChanges();

            var replacementMod = _mockReplacementSourceManager.Registry.Find(ModReplacementSourceRegistry.ModSourceId);
            replacementMod.Should().NotBeNull();

            var noVersionMod = _mockNoVersionWarningSourceManager.Registry.Find(NoVersionWarningSourceRegistry.ModSourceId);
            noVersionMod.Should().NotBeNull();
        }
    }
}
