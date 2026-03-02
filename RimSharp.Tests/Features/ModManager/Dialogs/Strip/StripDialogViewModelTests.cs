using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.Strip;
using RimSharp.Shared.Models;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs.Strip
{
    public class StripDialogViewModelTests
    {
        public StripDialogViewModelTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaFact]
        public void SelectionPropagation_ShouldWorkBothWays()
        {

            var modVm = new StrippableModViewModel(new ModItem { Name = "Test Mod" });
            var item1 = new StrippableItemViewModel(modVm, "File1", "path1", "full1", 100, StrippableItemType.File);
            var item2 = new StrippableItemViewModel(modVm, "File2", "path2", "full2", 200, StrippableItemType.File);
            modVm.Children.Add(item1);
            modVm.Children.Add(item2);

            // Act - Set mod selection to false
            modVm.IsSelected = false;

            // Assert - Children should be deselected
            item1.IsSelected.Should().BeFalse();
            item2.IsSelected.Should().BeFalse();
            modVm.SelectedSize.Should().Be(0);

            // Act - Select one child
            item1.IsSelected = true;

            // Assert - Mod should be indeterminate
            modVm.IsSelected.Should().BeNull();
            modVm.SelectedSize.Should().Be(100);

            // Act - Select all children
            item2.IsSelected = true;

            // Assert - Mod should be selected
            modVm.IsSelected.Should().BeTrue();
            modVm.SelectedSize.Should().Be(300);
        }

        [AvaloniaFact]
        public void TotalSelectedSize_ShouldUpdateCorrectly()
        {

            var mod1 = new StrippableModViewModel(new ModItem());
            mod1.Children.Add(new StrippableItemViewModel(mod1, "f1", "p1", "full1", 1000, StrippableItemType.File));
            
            var mod2 = new StrippableModViewModel(new ModItem());
            mod2.Children.Add(new StrippableItemViewModel(mod2, "f2", "p2", "full2", 2000, StrippableItemType.File));

            var vm = new StripDialogViewModel(new List<StrippableModViewModel> { mod1, mod2 });

            // Assert initial
            vm.TotalSelectedSize.Should().Be(3000);

            // Act - Deselect one mod
            mod1.IsSelected = false;

            vm.TotalSelectedSize.Should().Be(2000);
        }

        [AvaloniaFact]
        public void ExecuteStrip_ShouldReturnAllSelectedPaths()
        {

            var mod1 = new StrippableModViewModel(new ModItem());
            mod1.Children.Add(new StrippableItemViewModel(mod1, "f1", "p1", "full1", 100, StrippableItemType.File));
            
            var vm = new StripDialogViewModel(new List<StrippableModViewModel> { mod1 });
            
            (bool Strip, IEnumerable<string>? Paths) result = (false, null);
            vm.RequestCloseDialog += (s, e) => result = vm.DialogResult;

            vm.StripCommand.Execute(null);

            result.Strip.Should().BeTrue();
            result.Paths.Should().NotBeNull();
            result.Paths!.Should().Contain("full1");
        }
    }
}

