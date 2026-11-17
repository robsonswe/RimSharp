#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.AppDir.Dialogs;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Infrastructure.Logging;
using Xunit;

namespace RimSharp.Tests.AppDir.Dialogs.Settings
{
    public class SettingsDialogViewModelRuleSourcesTests
    {
        private readonly IPathService _mockPathService;
        private readonly ILoggerService _mockLogger;
        private readonly ISteamClaimValidator _mockSteamClaimValidator;
        private readonly IRuleSourceManager _mockRuleSourceManager;

        public SettingsDialogViewModelRuleSourcesTests()
        {
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
            _mockRuleSourceManager.Registry.Returns(new ModRuleSourceRegistry(new NullConfigService()));
            _mockRuleSourceManager.EffectiveTimestamp.Returns((long?)1789239946);
        }

        private SettingsDialogViewModel CreateViewModel(
            IConfigService configService,
            IRuleSourceManager? ruleSourceManager = null,
            IDialogService? dialogService = null,
            IFileDialogService? fileDialogService = null)
        {
            return new SettingsDialogViewModel(
                configService,
                _mockPathService,
                fileDialogService,
                _mockLogger,
                null,
                _mockSteamClaimValidator,
                CreateStubDetectionWithoutCandidates(),
                dialogService: dialogService,
                showAiProviders: false,
                ruleSourceManager: ruleSourceManager);
        }

        private static GameInstallationDetectionService CreateStubDetectionWithoutCandidates()
        {
            var stub = Substitute.ForPartsOf<GameInstallationDetectionService>();
            stub.DetectSteamInstallation().Returns((GameInstallationCandidate?)null);
            stub.DetectNonSteamInstallation().Returns((GameInstallationCandidate?)null);
            return stub;
        }

        [Fact]
        public void Constructor_WithRuleSourceManager_ShouldLoadDefaultRows()
        {
            var configService = new NullConfigService();

            var vm = CreateViewModel(configService, _mockRuleSourceManager);

            vm.RuleSources.Should().HaveCount(2);
            vm.RuleSources[0].Id.Should().Be(ModRuleSourceRegistry.CuratedSourceId);
            vm.RuleSources[1].Id.Should().Be(ModRuleSourceRegistry.CommunitySourceId);
            vm.RuleSources[1].Url.Should().Be(ModRuleSourceRegistry.CommunitySourceUrl);
            vm.RuleSources[1].TypeBadge.Should().Be("Remote file");
            vm.RuleSourcesStatus.Should().Contain("Rules as of");
        }

        [Fact]
        public void Constructor_WithoutRuleSourceManager_ShouldHaveNoRows()
        {
            var vm = CreateViewModel(new NullConfigService());

            vm.RuleSources.Should().BeEmpty();
        }

        [Fact]
        public void CuratedRow_ShouldShowPlaceholderAndRejectLocationEdits()
        {
            var vm = CreateViewModel(new NullConfigService(), _mockRuleSourceManager);

            var curated = vm.RuleSources[0];
            curated.IsCurated.Should().BeTrue();
            curated.Location.Should().BeEmpty();

            curated.Location = "https://example.invalid/hijack.json";
            curated.Location.Should().BeEmpty();
        }

        [Fact]
        public void Save_ShouldCommitRegistryChanges()
        {
            var vm = CreateViewModel(new NullConfigService(), _mockRuleSourceManager);

            vm.Save();

            _mockRuleSourceManager.Received(1).CommitRegistryChanges();
        }

        [Fact]
        public void Save_ShouldPersistEditedRows()
        {
            var configPath = Path.Combine(Path.GetTempPath(), "RimSharp_SettingsRules_" + Guid.NewGuid().ToString("N") + ".cfg");
            try
            {
                var configService = new ConfigService(configPath);
                var registry = new ModRuleSourceRegistry(configService);
                var manager = new RuleSourceManager(
                    registry,
                    Substitute.For<IModRulesRepository>(),
                    Substitute.For<IModService>(),
                    Substitute.For<IDataUpdateService>(),
                    _mockLogger);

                var vm = CreateViewModel(configService, manager);
                vm.RuleSources[1].Enabled = false;
                vm.RuleSources[1].Name = "My Community Mirror";

                vm.Save();

                var reloaded = new ModRuleSourceRegistry(new ConfigService(configPath));
                var community = reloaded.Find(ModRuleSourceRegistry.CommunitySourceId)!;
                community.Enabled.Should().BeFalse();
                community.Name.Should().Be("My Community Mirror");
            }
            finally
            {
                if (File.Exists(configPath)) File.Delete(configPath);
            }
        }

        [Fact]
        public void RemoveRuleSource_ShouldRemoveTheSelectedRow()
        {
            var vm = CreateViewModel(new NullConfigService(), _mockRuleSourceManager);
            vm.SelectedRuleSource = vm.RuleSources[1];

            vm.RemoveRuleSourceCommand.Execute(null);

            vm.RuleSources.Should().ContainSingle();
            vm.RuleSources[0].Id.Should().Be(ModRuleSourceRegistry.CuratedSourceId);
        }

        [Fact]
        public void MoveRuleSourceUp_ShouldReorderRows()
        {
            var vm = CreateViewModel(new NullConfigService(), _mockRuleSourceManager);
            vm.SelectedRuleSource = vm.RuleSources[1];

            vm.MoveRuleSourceUpCommand.Execute(null);

            vm.RuleSources[0].Id.Should().Be(ModRuleSourceRegistry.CommunitySourceId);
            vm.RuleSources[1].Id.Should().Be(ModRuleSourceRegistry.CuratedSourceId);
        }

        [Fact]
        public void ResetRuleSources_ShouldRestoreDefaults()
        {
            var vm = CreateViewModel(new NullConfigService(), _mockRuleSourceManager);
            vm.SelectedRuleSource = vm.RuleSources[1]; // community source (not locked)
            vm.RemoveRuleSourceCommand.Execute(null);
            vm.RuleSources.Should().ContainSingle();
            vm.RuleSources[0].Id.Should().Be(ModRuleSourceRegistry.CuratedSourceId);

            vm.ResetRuleSourcesCommand.Execute(null);

            vm.RuleSources.Should().HaveCount(2);
        }

        [Fact]
        public async Task AddRemoteRuleSource_ShouldAppendARowWithTheUrl()
        {
            var dialogService = Substitute.For<IDialogService>();
            dialogService.ShowRuleSourceEditorDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromResult((MessageDialogResult.OK, "My Rules", "https://example.invalid/myRules.json")));

            var vm = CreateViewModel(new NullConfigService(), _mockRuleSourceManager, dialogService);

            vm.AddRemoteRuleSourceCommand.Execute(null);
            await Task.Delay(100, TestContext.Current.CancellationToken);

            vm.RuleSources.Should().HaveCount(3);
            var added = vm.RuleSources.Last();
            added.Url.Should().Be("https://example.invalid/myRules.json");
            added.Name.Should().Be("My Rules");
            added.IsRepoFile.Should().BeTrue();
            added.Id.Should().Be("myRules");
        }

        private sealed class NullConfigService : IConfigService
        {
            public string GetConfigValue(string key) => string.Empty;
            public void SetConfigValue(string key, string value) { }
            public void LoadConfig() { }
            public void SaveConfig() { }
        }
    }
}