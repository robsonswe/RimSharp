using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.ModManager.ViewModels.Actions;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Tests.Base;
using Xunit;

using IModCommandService = RimSharp.Shared.Services.Contracts.IModCommandService;

namespace RimSharp.Tests.Features.ModManager.ViewModels.Actions
{
    public class ModActionsViewModelTests
    {
        private readonly IModDataService _dataService = Substitute.For<IModDataService>();
        private readonly IModCommandService _commandService = Substitute.For<IModCommandService>();
        private readonly IModListIOService _ioService = Substitute.For<IModListIOService>();
        private readonly IModListManager _modListManager = Substitute.For<IModListManager>();
        private readonly IModIncompatibilityService _incompatibilityService = Substitute.For<IModIncompatibilityService>();
        private readonly IModDuplicateService _duplicateService = Substitute.For<IModDuplicateService>();
        private readonly IModDeletionService _deletionService = Substitute.For<IModDeletionService>();
        private readonly IDialogService _dialogService = Substitute.For<IDialogService>();
        private readonly IPathService _pathService = Substitute.For<IPathService>();
        private readonly IModService _modService = Substitute.For<IModService>();
        private readonly IModReplacementService _replacementService = Substitute.For<IModReplacementService>();
        private readonly IDownloadQueueService _downloadQueueService = Substitute.For<IDownloadQueueService>();
        private readonly ISteamApiClient _steamApiClient = Substitute.For<ISteamApiClient>();
        private readonly IApplicationNavigationService _navigationService = Substitute.For<IApplicationNavigationService>();
        private readonly ISteamWorkshopQueueProcessor _steamWorkshopQueueProcessor = Substitute.For<ISteamWorkshopQueueProcessor>();
        private readonly IGitService _gitService = Substitute.For<IGitService>();

        public ModActionsViewModelTests()
        {
            TestApp.InitializeTestApp();
            _modListManager.GetAllMods().Returns(Enumerable.Empty<ModItem>());
            _modListManager.VirtualActiveMods.Returns(new List<(ModItem Mod, int LoadOrder)>());
            
            // Setup default valid paths
            _pathService.GetGamePath().Returns("C:\\Game");
            _pathService.GetModsPath().Returns("C:\\Mods");
            _pathService.GetConfigPath().Returns("C:\\Config");
        }

        private ModActionsViewModel CreateViewModel()
        {
            return new ModActionsViewModel(
                _dataService, _commandService, _ioService, _modListManager,
                _incompatibilityService, _duplicateService, _deletionService,
                _dialogService, _pathService, _modService, _replacementService,
                _downloadQueueService, _steamApiClient, _navigationService,
                _steamWorkshopQueueProcessor, _gitService);
        }

        [AvaloniaFact]
        public void DeleteModCommand_WhenNoModSelected_ShouldBeDisabled()
        {

            var vm = CreateViewModel();
            vm.SelectedMod = null;

            vm.DeleteModCommand.CanExecute(null).Should().BeFalse();
        }

        [AvaloniaFact]
        public void DeleteModCommand_WhenWorkshopModSelected_ShouldBeEnabled()
        {

            var vm = CreateViewModel();
            var mod = new ModItem { ModType = ModType.Workshop, Path = "C:\\Mods\\Mod1" };

            vm.SelectedMod = mod;

            vm.DeleteModCommand.CanExecute(null).Should().BeTrue();
        }

        [AvaloniaFact]
        public void InstallFromZipCommand_WhenPathsInvalid_ShouldBeDisabled()
        {

            _pathService.GetGamePath().Returns(""); // Make it invalid
            var vm = CreateViewModel();

            vm.InstallFromZipCommand.CanExecute(null).Should().BeFalse();
        }

        [AvaloniaFact]
        public void TotalActiveIssues_ShouldSumCorrectly()
        {

            _modListManager.ActiveMissingDependenciesCount.Returns(2);
            _modListManager.ActiveIncompatibilitiesCount.Returns(3);
            _modListManager.ActiveDuplicateIssuesCount.Returns(1);
            var vm = CreateViewModel();

            vm.TotalActiveIssues.Should().Be(6);
        }

        [AvaloniaFact]
        public void CheckIncompatibilitiesCommand_WhenNoActiveMods_ShouldBeDisabled()
        {

            _modListManager.VirtualActiveMods.Returns(new List<(ModItem Mod, int LoadOrder)>());
            var vm = CreateViewModel();

            vm.CheckIncompatibilitiesCommand.CanExecute(null).Should().BeFalse();
        }

        [AvaloniaFact]
        public void CheckIncompatibilitiesCommand_WhenActiveModsExist_ShouldBeEnabled()
        {

            _modListManager.VirtualActiveMods.Returns(new List<(ModItem Mod, int LoadOrder)> { (new ModItem(), 1) });
            var vm = CreateViewModel();

            vm.CheckIncompatibilitiesCommand.CanExecute(null).Should().BeTrue();
        }

        [AvaloniaFact]
        public void ResolveDependenciesCommand_WhenPathsValid_ShouldBeEnabled()
        {

            var vm = CreateViewModel();

            vm.ResolveDependenciesCommand.CanExecute(null).Should().BeTrue();
        }
    }
}


