using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.ModManager.ViewModels;
using RimSharp.Features.ModManager.ViewModels.Actions;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.ViewModels
{
    public class ModsViewModelTests
    {
        private readonly IModDataService _mockDataService;
        private readonly IModFilterService _mockFilterService;
        private readonly IModListManager _mockModListManager;
        private readonly IDialogService _mockDialogService;
        private readonly IPathService _mockPathService;
        private readonly IModService _mockModService;
        private readonly IModDeletionService _mockDeletionService;
        private readonly ISteamWorkshopQueueProcessor _mockSteamWorkshopQueueProcessor;
        private readonly IGitService _mockGitService;
        private readonly IModCommandService _mockCommandService;
        private readonly IModListIOService _mockIoService;
        private readonly IModIncompatibilityService _mockIncompatibilityService;
        private readonly IModDuplicateService _mockDuplicateService;
        private readonly IModRulesService _mockRulesService;
        private readonly IModReplacementService _mockReplacementService;
        private readonly IDownloadQueueService _mockDownloadQueueService;
        private readonly ISteamApiClient _mockSteamApiClient;
        private readonly IApplicationNavigationService _mockNavigationService;

        public ModsViewModelTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();
            
            _mockDataService = Substitute.For<IModDataService>();
            _mockFilterService = Substitute.For<IModFilterService>();
            _mockModListManager = Substitute.For<IModListManager>();
            _mockDialogService = Substitute.For<IDialogService>();
            _mockPathService = Substitute.For<IPathService>();
            _mockModService = Substitute.For<IModService>();
            _mockDeletionService = Substitute.For<IModDeletionService>();
            _mockSteamWorkshopQueueProcessor = Substitute.For<ISteamWorkshopQueueProcessor>();
            _mockGitService = Substitute.For<IGitService>();
            _mockCommandService = Substitute.For<IModCommandService>();
            _mockIoService = Substitute.For<IModListIOService>();
            _mockIncompatibilityService = Substitute.For<IModIncompatibilityService>();
            _mockDuplicateService = Substitute.For<IModDuplicateService>();
            _mockRulesService = Substitute.For<IModRulesService>();
            _mockReplacementService = Substitute.For<IModReplacementService>();
            _mockDownloadQueueService = Substitute.For<IDownloadQueueService>();
            _mockSteamApiClient = Substitute.For<ISteamApiClient>();
            _mockNavigationService = Substitute.For<IApplicationNavigationService>();

            // Mock ActiveMods and InactiveMods to avoid null refs
            _mockFilterService.ActiveMods.Returns(new System.Collections.ObjectModel.ObservableCollection<ModItem>());
            _mockFilterService.InactiveMods.Returns(new System.Collections.ObjectModel.ObservableCollection<ModItem>());

            // Mock VirtualActiveMods and AllInactiveMods to avoid null refs
            _mockModListManager.VirtualActiveMods.Returns(new List<(ModItem Mod, int LoadOrder)>());
            _mockModListManager.AllInactiveMods.Returns(new List<ModItem>());
        }

        private ModsViewModel CreateViewModel()
        {
            return new ModsViewModel(
                _mockDataService,
                _mockFilterService,
                _mockCommandService,
                _mockIoService,
                _mockModListManager,
                _mockIncompatibilityService,
                _mockDuplicateService,
                _mockDeletionService,
                _mockDialogService,
                _mockModService,
                _mockPathService,
                _mockRulesService,
                _mockReplacementService,
                _mockDownloadQueueService,
                _mockSteamApiClient,
                _mockNavigationService,
                _mockSteamWorkshopQueueProcessor,
                _mockGitService
            );
        }

        [Fact]
        public async Task InitializeAsync_ShouldLoadDataAndInitializeManager()
        {
            // Arrange
            var vm = CreateViewModel();
            var mods = new List<ModItem> { new ModItem { Name = "Mod 1", PackageId = "mod1" } };
            var activeIds = new List<string> { "mod1" };

            _mockDataService.LoadAllModsAsync(Arg.Any<IProgress<(int current, int total, string message)>>()).Returns(mods);
            _mockDataService.LoadActiveModIdsFromConfig().Returns(activeIds);

            // Act
            await vm.InitializeAsync();

            // Assert
            await _mockDataService.Received(1).LoadAllModsAsync(Arg.Any<IProgress<(int current, int total, string message)>>());
            _mockDataService.Received(1).LoadActiveModIdsFromConfig();
            _mockModListManager.Received(1).Initialize(mods, activeIds);
            _mockFilterService.Received(1).UpdateCollections(Arg.Any<IEnumerable<(ModItem Mod, int LoadOrder)>>(), Arg.Any<IEnumerable<ModItem>>());
        }

        [Fact]
        public async Task RequestRefreshCommand_ShouldReloadData()
        {
            // Arrange
            var vm = CreateViewModel();
            _mockDataService.LoadAllModsAsync(Arg.Any<IProgress<(int current, int total, string message)>>()).Returns(new List<ModItem>());
            _mockDataService.LoadActiveModIdsFromConfig().Returns(new List<string>());

            // Act
            vm.RequestRefreshCommand.Execute(null);
            
            // Wait for async command
            for (int i = 0; i < 100 && !vm.IsLoading; i++) await Task.Delay(10);
            for (int i = 0; i < 500 && vm.IsLoading; i++) await Task.Delay(10);

            // Assert
            await _mockDataService.Received(1).LoadAllModsAsync(Arg.Any<IProgress<(int current, int total, string message)>>());
            _mockModListManager.Received(1).Initialize(Arg.Any<List<ModItem>>(), Arg.Any<List<string>>());
        }

        [Fact]
        public void SelectedMod_WhenChanged_ShouldUpdateChildViewModels()
        {
            // Arrange
            var vm = CreateViewModel();
            var mod = new ModItem { Name = "Test Mod" };

            // Act
            vm.SelectedMod = mod;

            // Assert
            vm.SelectedMod.Should().Be(mod);
            vm.ModDetailsViewModel.CurrentMod.Should().Be(mod);
            vm.ModActionsViewModel.SelectedMod.Should().Be(mod);
        }

        [Fact]
        public async Task LoadDataAsync_WhenExceptionOccurs_ShouldShowErrorAndStopLoading()
        {
            // Arrange
            var vm = CreateViewModel();
            _mockDataService.LoadAllModsAsync(Arg.Any<IProgress<(int current, int total, string message)>>())
                .Returns(Task.FromException<List<ModItem>>(new Exception("Test Error")));

            // Act
            await vm.InitializeAsync();

            // Assert
            vm.IsLoading.Should().BeFalse();
            _mockDialogService.Received(1).ShowError(Arg.Any<string>(), Arg.Is<string>(s => s.Contains("Test Error")));
        }
    }
}
