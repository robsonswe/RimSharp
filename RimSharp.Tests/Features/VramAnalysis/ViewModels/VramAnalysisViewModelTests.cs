using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.AppDir.Dialogs;
using RimSharp.Core.Services;
using RimSharp.Features.VramAnalysis.ViewModels;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.VramAnalysis.ViewModels
{
    public class VramAnalysisViewModelTests
    {
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService;
        private readonly ILoggerService _logger;
        private readonly IPathService _pathService;
        private readonly ISystemInfoService _systemInfoService;

        public VramAnalysisViewModelTests()
        {
            _modListManager = Substitute.For<IModListManager>();
            _dialogService = Substitute.For<IDialogService>();
            _logger = Substitute.For<ILoggerService>();
            _pathService = Substitute.For<IPathService>();
            _systemInfoService = Substitute.For<ISystemInfoService>();
        }

        private VramAnalysisViewModel CreateViewModel()
        {
            return new VramAnalysisViewModel(_modListManager, _dialogService, _logger, _pathService, _systemInfoService);
        }

        [Fact]
        public async Task Constructor_ShouldLoadMods_InBackground()
        {
            // Arrange
            var mods = new List<ModItem>
            {
                new ModItem { Name = "Mod A", ModType = ModType.Workshop, Textures = true },
                new ModItem { Name = "Mod B", ModType = ModType.Git, Textures = true }
            };
            _modListManager.GetAllMods().Returns(mods);

            // Act
            var vm = CreateViewModel();
            
            // Assert
            // Wait for VramMods to be populated using SpinWait because it runs on a background task
            bool loaded = await Task.Run(() => SpinWait.SpinUntil(() => vm.VramMods.Count == 2, 5000));
            loaded.Should().BeTrue("Mods should be loaded within timeout");
            
            vm.VramMods.Should().HaveCount(2);
            vm.VramMods.Select(m => m.Mod.Name).Should().Contain(new[] { "Mod A", "Mod B" });
        }

        [Fact]
        public async Task CalculateVramCommand_ShouldShowProgressDialog_AndExecute()
        {
            // Arrange
            var mods = new List<ModItem>
            {
                new ModItem { Name = "Mod A", ModType = ModType.Workshop, Textures = true, Path = @"C:\Mods\ModA", PackageId = "ModA" }
            };
            _modListManager.GetAllMods().Returns(mods);
            _modListManager.VirtualActiveMods.Returns(new List<(ModItem, int)>());
            
            var vm = CreateViewModel();
            await Task.Run(() => SpinWait.SpinUntil(() => vm.VramMods.Count == 1, 2000));

            // Act
            if (vm.CalculateVramCommand.CanExecute(null))
            {
                await vm.CalculateVramCommand.ExecuteAsync(null);
            }

            // Assert
            _dialogService.Received().ShowProgressDialog(Arg.Any<string>(), Arg.Any<string>(), true, false, Arg.Any<CancellationTokenSource>());
        }

        [Fact]
        public async Task ToggleShowMaxVram_ShouldNotReorder_ButUpdateVisibility()
        {
            // Arrange
            var mods = new List<ModItem>
            {
                new ModItem { Name = "Mod A", ModType = ModType.Workshop, Textures = true }
            };
            _modListManager.GetAllMods().Returns(mods);
            var vm = CreateViewModel();
            await Task.Run(() => SpinWait.SpinUntil(() => vm.VramMods.Count == 1, 2000));

            // Act
            var initialCollection = vm.VramMods;
            vm.ShowMaxVram = !vm.ShowMaxVram;

            // Assert
            vm.ShowMaxVram.Should().BeTrue();
            // Verify that the collection instance hasn't changed (implies no re-sort)
            vm.VramMods.Should().BeSameAs(initialCollection);
        }
        
        [Fact]
        public async Task SortCommand_ShouldSortList()
        {
             // Arrange
            var mods = new List<ModItem>
            {
                new ModItem { Name = "Z Mod", ModType = ModType.Workshop, Textures = true },
                new ModItem { Name = "A Mod", ModType = ModType.Workshop, Textures = true }
            };
            _modListManager.GetAllMods().Returns(mods);
            var vm = CreateViewModel();
            bool loaded = await Task.Run(() => SpinWait.SpinUntil(() => vm.VramMods.Count == 2, 5000));
            loaded.Should().BeTrue("Mods should be loaded within timeout");

            // Initial State: Ascending ("A Mod", "Z Mod")
            vm.VramMods.First().Mod.Name.Should().Be("A Mod");

            // Act - Click "Mod.Name" -> Should toggle to Descending
            vm.SortCommand.Execute("Mod.Name"); 
            
            // Assert - Descending ("Z Mod", "A Mod")
            vm.VramMods.First().Mod.Name.Should().Be("Z Mod");
            
            // Act - Click "Mod.Name" again -> Should toggle to Ascending
            vm.SortCommand.Execute("Mod.Name");
            
            // Assert - Ascending ("A Mod", "Z Mod")
            vm.VramMods.First().Mod.Name.Should().Be("A Mod");
        }
        
        [Fact]
        public async Task Filter_ShouldShowOnlyActiveMods()
        {
            // Arrange
            var mods = new List<ModItem>
            {
                new ModItem { Name = "Mod Active", ModType = ModType.Workshop, Textures = true, IsActive = true },
                new ModItem { Name = "Mod Inactive", ModType = ModType.Workshop, Textures = true, IsActive = false }
            };
            _modListManager.GetAllMods().Returns(mods);
            var vm = CreateViewModel();
            await Task.Run(() => SpinWait.SpinUntil(() => vm.VramMods.Count == 2, 2000));

            // Act
            vm.ShowOnlyActive = true;
            
            // Assert
            vm.VramMods.Should().HaveCount(1);
            vm.VramMods.First().Mod.Name.Should().Be("Mod Active");
        }
    }
}
