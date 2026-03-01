using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using RimSharp.Core.Helpers;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Core.Helpers
{
    public class ListBoxSelectionHelperTests
    {
        public ListBoxSelectionHelperTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaFact]
        public void SelectedItemsProperty_ShouldSyncWithListBoxSelection()
        {
            // Arrange
            var listBox = new ListBox
            {
                SelectionMode = SelectionMode.Multiple,
                ItemsSource = new List<string> { "Item1", "Item2", "Item3" }
            };
            
            var targetList = new List<object>();
            ListBoxSelectionHelper.SetSelectedItems(listBox, targetList);

            // Act
            listBox.SelectedItems!.Add("Item1");
            listBox.SelectedItems!.Add("Item3");

            // Assert
            var selected = ListBoxSelectionHelper.GetSelectedItems(listBox) as IEnumerable<object>;
            selected.Should().NotBeNull();
            selected.Should().HaveCount(2);
            selected.Should().Contain("Item1");
            selected.Should().Contain("Item3");
        }
    }
}
