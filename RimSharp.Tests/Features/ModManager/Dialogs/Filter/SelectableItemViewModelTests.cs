using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.Filter;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs.Filter
{
    public class SelectableItemViewModelTests
    {
        [Fact]
        public void Constructor_ShouldSetItem()
        {
            var vm = new SelectableItemViewModel<string>("hello", "Display");

            vm.Item.Should().Be("hello");
        }

        [Fact]
        public void Constructor_ShouldSetDisplayName()
        {
            var vm = new SelectableItemViewModel<string>("hello", "My Display");

            vm.DisplayName.Should().Be("My Display");
        }

        [Fact]
        public void Constructor_DefaultIsSelected_ShouldBeFalse()
        {
            var vm = new SelectableItemViewModel<string>("hello", "Display");

            vm.IsSelected.Should().BeFalse();
        }

        [Fact]
        public void Constructor_WhenIsSelectedTrue_ShouldBeSelected()
        {
            var vm = new SelectableItemViewModel<string>("hello", "Display", isSelected: true);

            vm.IsSelected.Should().BeTrue();
        }

        [Fact]
        public void IsSelected_SetProperty_ShouldRaisePropertyChanged()
        {
            var vm = new SelectableItemViewModel<string>("hello", "Display");
            var raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(vm.IsSelected)) raised = true; };

            vm.IsSelected = true;

            raised.Should().BeTrue();
        }

        [Fact]
        public void IsSelected_CanBeToggled()
        {
            var vm = new SelectableItemViewModel<int>(42, "Display", isSelected: true);

            vm.IsSelected = false;

            vm.IsSelected.Should().BeFalse();
        }

        [Fact]
        public void Constructor_WorksWithNullableItem()
        {
            var vm = new SelectableItemViewModel<string?>(null, "Null Item");

            vm.Item.Should().BeNull();
            vm.DisplayName.Should().Be("Null Item");
        }
    }
}
