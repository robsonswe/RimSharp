using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using RimSharp.Features.WorkshopDownloader.Dialogs.Collection;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Features.WorkshopDownloader.Dialogs.Collection
{
    public class CollectionDialogViewModelTests
    {
        public CollectionDialogViewModelTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaFact]
        public void SelectAll_ShouldSelectAllItems()
        {
            // Arrange
            var items = new List<CollectionItemInfo>
            {
                new CollectionItemInfo { SteamId = "1", Name = "Mod 1" },
                new CollectionItemInfo { SteamId = "2", Name = "Mod 2" }
            };
            var vm = new CollectionDialogViewModel(items);
            vm.SelectNoneCommand.Execute(null);

            // Act
            vm.SelectAllCommand.Execute(null);

            // Assert
            vm.Items.Should().AllSatisfy(i => i.IsSelected.Should().BeTrue());
            vm.AddSelectedCommand.CanExecute(null).Should().BeTrue();
        }

        [AvaloniaFact]
        public void AddSelected_ShouldReturnSelectedIds()
        {
            // Arrange
            var items = new List<CollectionItemInfo>
            {
                new CollectionItemInfo { SteamId = "1", Name = "Mod 1" },
                new CollectionItemInfo { SteamId = "2", Name = "Mod 2" }
            };
            var vm = new CollectionDialogViewModel(items);
            vm.Items.First(i => i.SteamId == "1").IsSelected = true;
            vm.Items.First(i => i.SteamId == "2").IsSelected = false;

            List<string>? result = null;
            vm.RequestCloseDialog += (s, e) => result = vm.DialogResult;

            // Act
            vm.AddSelectedCommand.Execute(null);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result.Should().Contain("1");
            vm.DialogResultForWindow.Should().BeTrue();
        }
    }
}
