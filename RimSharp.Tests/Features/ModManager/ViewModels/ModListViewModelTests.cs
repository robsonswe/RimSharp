using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.ModManager.ViewModels;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.ViewModels
{
    public class ModListViewModelTests : IDisposable
    {
        private readonly IModFilterService _mockFilterService;
        private readonly IModListManager _mockModListManager;
        private readonly IModCommandService _mockCommandService;
        private readonly IDialogService _mockDialogService;
        private ModListViewModel? _vm;

        public ModListViewModelTests()
        {
            _mockFilterService = Substitute.For<IModFilterService>();
            _mockModListManager = Substitute.For<IModListManager>();
            _mockCommandService = Substitute.For<IModCommandService>();
            _mockDialogService = Substitute.For<IDialogService>();

            // Mock collections to avoid null refs
            _mockFilterService.ActiveMods.Returns(new ObservableCollection<ModItem>());
            _mockFilterService.InactiveMods.Returns(new ObservableCollection<ModItem>());
            _mockFilterService.ActiveFilterCriteria.Returns(new ModFilterCriteria());
            _mockFilterService.InactiveFilterCriteria.Returns(new ModFilterCriteria());
            _mockFilterService.ActiveSearchText.Returns(string.Empty);
            _mockFilterService.InactiveSearchText.Returns(string.Empty);
        }

        public void Dispose()
        {
            _vm?.Dispose();
        }

        private ModListViewModel CreateViewModel()
        {
            _vm = new ModListViewModel(
                _mockFilterService,
                _mockModListManager,
                _mockCommandService,
                _mockDialogService
            );
            return _vm;
        }

        [Fact]
        public void ActivateModCommand_ShouldCallManager()
        {

            var vm = CreateViewModel();
            var mod = new ModItem { Name = "Test Mod" };

            vm.ActivateModCommand.Execute(mod);

            _mockModListManager.Received(1).ActivateMod(mod);
        }

        [Fact]
        public void DeactivateModCommand_ShouldCallManager()
        {

            var vm = CreateViewModel();
            var mod = new ModItem { Name = "Test Mod", ModType = ModType.Workshop };

            vm.DeactivateModCommand.Execute(mod);

            _mockModListManager.Received(1).DeactivateMod(mod);
        }

        [Fact]
        public void ActiveSearchText_ShouldDebounceAndApplyFilter()
        {

            var vm = CreateViewModel();

            vm.ActiveSearchText = "search";

            _mockFilterService.Received(1).ApplyActiveFilter("search");
        }

        [Fact]
        public void HandleListBoxSelectionChanged_ShouldUpdateSelectedModAndRaiseEvent()
        {

            var vm = CreateViewModel();
            var mod = new ModItem { Name = "Selected Mod" };
            bool eventRaised = false;
            vm.RequestSelectionChange += (s, e) => eventRaised = true;

            vm.HandleListBoxSelectionChanged(mod);

            vm.SelectedMod.Should().Be(mod);
            eventRaised.Should().BeTrue();
        }
    }
}

