using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.Dependencies;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs
{
    public class DependencyResolutionDialogViewModelTests
    {
        public DependencyResolutionDialogViewModelTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();
        }

        [Fact]
        public void SelectAllCommand_ShouldSelectAllSelectableItems()
        {
            // Arrange
            var data = new List<(string displayName, string packageId, string steamUrl, List<string> requiredBy)>
            {
                ("M1", "id1", "https://steamcommunity.com/sharedfiles/filedetails/?id=1", new List<string>()),
                ("M2", "id2", "https://steamcommunity.com/sharedfiles/filedetails/?id=2", new List<string>()),
                ("M3 (No Steam)", "id3", "", new List<string>()) // Not selectable
            };
            var vm = new DependencyResolutionDialogViewModel(data);

            // Act
            vm.SelectAllCommand.Execute(null);

            // Assert
            vm.MissingDependencies.Count(i => i.IsSelected).Should().Be(2);
            vm.SelectedCount.Should().Be(2);
        }

        [Fact]
        public void SelectNoneCommand_ShouldClearSelection()
        {
            // Arrange
            var data = new List<(string displayName, string packageId, string steamUrl, List<string> requiredBy)>
            {
                ("M1", "id1", "https://steamcommunity.com/sharedfiles/filedetails/?id=1", new List<string>())
            };
            var vm = new DependencyResolutionDialogViewModel(data);
            vm.MissingDependencies[0].IsSelected = true;

            // Act
            vm.SelectNoneCommand.Execute(null);

            // Assert
            vm.MissingDependencies[0].IsSelected.Should().BeFalse();
            vm.SelectedCount.Should().Be(0);
        }

        [Fact]
        public void GetSelectedSteamIds_ShouldOnlyReturnIdsOfSelectedAndSelectableItems()
        {
            // Arrange
            var data = new List<(string displayName, string packageId, string steamUrl, List<string> requiredBy)>
            {
                ("M1", "id1", "https://steamcommunity.com/sharedfiles/filedetails/?id=1", new List<string>()),
                ("M2", "id2", "https://steamcommunity.com/sharedfiles/filedetails/?id=2", new List<string>())
            };
            var vm = new DependencyResolutionDialogViewModel(data);
            vm.MissingDependencies[0].IsSelected = true;
            vm.MissingDependencies[1].IsSelected = false;

            // Act
            var ids = vm.GetSelectedSteamIds();

            // Assert
            ids.Should().HaveCount(1);
            ids.Should().Contain("1");
        }
    }
}
