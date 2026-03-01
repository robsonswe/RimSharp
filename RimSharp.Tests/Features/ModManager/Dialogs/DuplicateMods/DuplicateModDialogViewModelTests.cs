using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.DuplicateMods;
using RimSharp.Shared.Models;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs.DuplicateMods
{
    public class DuplicateModDialogViewModelTests
    {
        public DuplicateModDialogViewModelTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaFact]
        public async Task ApplyResolutions_ShouldCallApplyCallbackWithPathsToDelete()
        {
            // Arrange
            var mod1 = new ModItem { PackageId = "mod.a", Name = "Mod 1", Path = "path1", IsActive = true };
            var mod2 = new ModItem { PackageId = "mod.a", Name = "Mod 2", Path = "path2", IsActive = false };
            var group = new List<ModItem> { mod1, mod2 }.GroupBy(m => m.PackageId!).ToList();

            List<string>? pathsToDelete = null;
            var vm = new DuplicateModDialogViewModel(group, (list) => 
            {
                pathsToDelete = list;
                return Task.CompletedTask;
            }, () => { });

            // Ensure mod1 is active (to keep) and mod2 is inactive (to delete)
            vm.DuplicateGroups[0].Mods.First(m => m.Original == mod1).IsActive = true;
            vm.DuplicateGroups[0].Mods.First(m => m.Original == mod2).IsActive = false;

            // Act
            // Since ApplyResolutions is private async void, we have to invoke the command and wait a bit
            vm.ApplyResolutionsCommand.Execute(null);
            await Task.Delay(100);

            // Assert
            pathsToDelete.Should().NotBeNull();
            pathsToDelete.Should().HaveCount(1);
            pathsToDelete.Should().Contain("path2");
        }

        [AvaloniaFact]
        public async Task ApplyResolutions_WhenIgnored_ShouldNotDeleteAnything()
        {
            // Arrange
            var mod1 = new ModItem { PackageId = "mod.a", Name = "Mod 1", Path = "path1" };
            var mod2 = new ModItem { PackageId = "mod.a", Name = "Mod 2", Path = "path2" };
            var group = new List<ModItem> { mod1, mod2 }.GroupBy(m => m.PackageId!).ToList();

            List<string>? pathsToDelete = null;
            var vm = new DuplicateModDialogViewModel(group, (list) => 
            {
                pathsToDelete = list;
                return Task.CompletedTask;
            }, () => { });

            vm.DuplicateGroups[0].IsIgnored = true;

            // Act
            vm.ApplyResolutionsCommand.Execute(null);
            await Task.Delay(100);

            // Assert
            pathsToDelete.Should().BeEmpty();
        }
    }
}
