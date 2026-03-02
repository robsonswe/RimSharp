using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.ModSelector;
using RimSharp.Shared.Models;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs.ModSelector
{
    public class ModSelectorDialogViewModelTests
    {
        public ModSelectorDialogViewModelTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaFact]
        public void Filter_ShouldFilterByName()
        {

            var mods = new List<ModItem>
            {
                new ModItem { Name = "Harmony", PackageId = "brrainz.harmony" },
                new ModItem { Name = "Core", PackageId = "ludeon.rimworld" },
                new ModItem { Name = "HugsLib", PackageId = "unlimitedhugs.hugslib" }
            };
            var vm = new ModSelectorDialogViewModel(mods);

            vm.SearchText = "Har";

            vm.FilteredMods.Should().HaveCount(1);
            vm.FilteredMods.First().Name.Should().Be("Harmony");
        }

        [AvaloniaFact]
        public void Filter_ShouldFilterByPackageId()
        {

            var mods = new List<ModItem>
            {
                new ModItem { Name = "Harmony", PackageId = "brrainz.harmony" }
            };
            var vm = new ModSelectorDialogViewModel(mods);

            vm.SearchText = "brrainz";

            vm.FilteredMods.Should().HaveCount(1);
        }

        [AvaloniaFact]
        public void ConfirmCommand_ShouldReturnSelectedMod()
        {

            var mod = new ModItem { Name = "Harmony" };
            var vm = new ModSelectorDialogViewModel(new List<ModItem> { mod });
            vm.SelectedMod = mod;

            ModItem? result = null;
            vm.RequestCloseDialog += (s, e) => result = vm.DialogResult;

            vm.ConfirmCommand.Execute(null);

            result.Should().Be(mod);
        }
    }
}

