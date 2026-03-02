using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using RimSharp.Features.WorkshopDownloader.Dialogs.UpdateCheck;
using RimSharp.Shared.Models;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Features.WorkshopDownloader.Dialogs.UpdateCheck
{
    public class UpdateCheckDialogViewModelTests
    {
        public UpdateCheckDialogViewModelTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaFact]
        public void SelectActiveOnly_ShouldSelectOnlyActiveMods()
        {

            var mods = new List<ModItem>
            {
                new ModItem { Name = "Active Mod", IsActive = true },
                new ModItem { Name = "Inactive Mod", IsActive = false }
            };
            var vm = new UpdateCheckDialogViewModel(mods);

            vm.SelectActiveCommand.Execute(null);

            vm.GetSelectedMods().Should().HaveCount(1);
            vm.GetSelectedMods().First().Name.Should().Be("Active Mod");
        }

        [AvaloniaFact]
        public void UpdateCommand_WhenNoModsSelected_ShouldBeDisabled()
        {

            var vm = new UpdateCheckDialogViewModel(new List<ModItem> { new ModItem() });

            vm.SelectNoneCommand.Execute(null);

            vm.UpdateCommand.CanExecute(null).Should().BeFalse();
        }

        [AvaloniaFact]
        public void UpdateCommand_Execute_ShouldSetResult()
        {

            var vm = new UpdateCheckDialogViewModel(new List<ModItem> { new ModItem() });
            UpdateCheckDialogResult? result = null;
            vm.RequestCloseDialog += (s, e) => result = vm.DialogResult;

            vm.UpdateCommand.Execute(null);

            result.Should().Be(UpdateCheckDialogResult.CheckUpdates);
        }
    }
}

