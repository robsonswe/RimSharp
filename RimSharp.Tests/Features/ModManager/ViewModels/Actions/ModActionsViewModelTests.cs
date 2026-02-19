using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.ModManager.ViewModels.Actions;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.ViewModels.Actions
{
    public class ModActionsViewModelTests
    {
        private readonly IModDataService _mockDataService;
        private readonly IModCommandService _mockCommandService;
        private readonly IModListIOService _mockIoService;
        private readonly IModListManager _mockModListManager;
        private readonly IModIncompatibilityService _mockIncompatibilityService;
        private readonly IModDuplicateService _mockDuplicateService;
        private readonly IModDeletionService _mockDeletionService;
        private readonly IDialogService _mockDialogService;
        private readonly IPathService _mockPathService;
        private readonly IModService _mockModService;
        private readonly IModRulesService _mockRulesService;
        private readonly IModReplacementService _mockReplacementService;
        private readonly IDownloadQueueService _mockDownloadQueueService;
        private readonly ISteamApiClient _mockSteamApiClient;
        private readonly IApplicationNavigationService _mockNavigationService;
        private readonly ISteamWorkshopQueueProcessor _mockSteamWorkshopQueueProcessor;
        private readonly IGitService _mockGitService;

        public ModActionsViewModelTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();

            _mockDataService = Substitute.For<IModDataService>();
            _mockCommandService = Substitute.For<IModCommandService>();
            _mockIoService = Substitute.For<IModListIOService>();
            _mockModListManager = Substitute.For<IModListManager>();
            _mockIncompatibilityService = Substitute.For<IModIncompatibilityService>();
            _mockDuplicateService = Substitute.For<IModDuplicateService>();
            _mockDeletionService = Substitute.For<IModDeletionService>();
            _mockDialogService = Substitute.For<IDialogService>();
            _mockPathService = Substitute.For<IPathService>();
            _mockModService = Substitute.For<IModService>();
            _mockRulesService = Substitute.For<IModRulesService>();
            _mockReplacementService = Substitute.For<IModReplacementService>();
            _mockDownloadQueueService = Substitute.For<IDownloadQueueService>();
            _mockSteamApiClient = Substitute.For<ISteamApiClient>();
            _mockNavigationService = Substitute.For<IApplicationNavigationService>();
            _mockSteamWorkshopQueueProcessor = Substitute.For<ISteamWorkshopQueueProcessor>();
            _mockGitService = Substitute.For<IGitService>();

            var mockProgress = Substitute.For<ProgressDialogViewModel>("title", "message", false, true, (CancellationTokenSource)null!, true);
            _mockDialogService.ShowProgressDialog(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationTokenSource>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(mockProgress);
        }

        private ModActionsViewModel CreateViewModel()
        {
            var vm = new ModActionsViewModel(
                _mockDataService,
                _mockCommandService,
                _mockIoService,
                _mockModListManager,
                _mockIncompatibilityService,
                _mockDuplicateService,
                _mockDeletionService,
                _mockDialogService,
                _mockPathService,
                _mockModService,
                _mockReplacementService,
                _mockDownloadQueueService,
                _mockSteamApiClient,
                _mockNavigationService,
                _mockSteamWorkshopQueueProcessor,
                _mockGitService
            );

            // Sync loading state for tests
            vm.IsLoadingRequest += (s, isLoading) => vm.IsParentLoading = isLoading;

            return vm;
        }

        [Fact]
        public async Task ClearActiveListCommand_ShouldCallManager()
        {
            // Arrange
            var vm = CreateViewModel();
            _mockPathService.GetGamePath().Returns(@"C:\Game");
            _mockPathService.GetConfigPath().Returns(@"C:\Config");
            _mockPathService.GetModsPath().Returns(@"C:\Mods");
            vm.RefreshPathValidity();

            // Act
            vm.ClearActiveListCommand.Execute(null);
            
            // Wait for command
            await Task.Delay(50);
            await WaitUntil(() => _mockModListManager.ReceivedCalls().Any(c => c.GetMethodInfo().Name == "ClearActiveList"));

            // Assert
            _mockModListManager.Received(1).ClearActiveList();
        }

        [Fact]
        public void SaveCommand_ShouldCallDataService()
        {
            // Arrange
            var vm = CreateViewModel();
            vm.HasUnsavedChanges = true;
            var activeMods = new List<(ModItem Mod, int LoadOrder)> 
            { 
                (new ModItem { PackageId = "m1" }, 0) 
            };
            _mockModListManager.VirtualActiveMods.Returns(activeMods);

            // Act
            vm.SaveCommand.Execute(null);

            // Assert
            _mockDataService.Received(1).SaveActiveModIdsToConfig(Arg.Is<IEnumerable<string>>(en => en.Count() == 1 && en.First() == "m1"));
        }

        [Fact]
        public async Task DeleteModsCommand_ShouldCallDeletionService()
        {
            // Arrange
            var vm = CreateViewModel();
            _mockPathService.GetGamePath().Returns(@"C:\Game");
            _mockPathService.GetConfigPath().Returns(@"C:\Config");
            _mockPathService.GetModsPath().Returns(@"C:\Mods");
            vm.RefreshPathValidity();

            string testPath = Path.Combine(Path.GetTempPath(), "RimSharp_DeleteTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testPath);
            try
            {
                var mods = new List<ModItem> { new ModItem { Name = "M1", Path = testPath, ModType = ModType.Workshop } };
                vm.SelectedItems = mods;
                _mockDialogService.ShowConfirmation(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
                    .Returns(RimSharp.AppDir.Dialogs.MessageDialogResult.OK);

                // Act
                vm.DeleteModsCommand.Execute(mods);
                
                // Wait for call
                await Task.Delay(50);
                await WaitUntil(() => _mockDeletionService.ReceivedCalls().Any(c => c.GetMethodInfo().Name == "DeleteDirectoryRobustAsync"));

                // Assert
                await _mockDeletionService.Received(1).DeleteDirectoryRobustAsync(testPath, Arg.Any<CancellationToken>());
            }
            finally
            {
                if (Directory.Exists(testPath)) Directory.Delete(testPath, true);
            }
        }

        [Fact]
        public void RefreshPathValidity_ShouldUpdateProperties()
        {
            // Arrange
            var vm = CreateViewModel();
            _mockPathService.GetGamePath().Returns(@"C:\Game");
            _mockPathService.GetConfigPath().Returns(@"C:\Config");
            _mockPathService.GetModsPath().Returns(@"C:\Mods");

            // Act
            vm.RefreshPathValidity();

            // Assert
            vm.HasValidPaths.Should().BeTrue();
        }

        private async Task WaitUntil(Func<bool> condition, int timeoutMs = 2000)
        {
            var start = DateTime.Now;
            while (!condition() && (DateTime.Now - start).TotalMilliseconds < timeoutMs)
            {
                await Task.Delay(10);
            }
        }
    }
}
