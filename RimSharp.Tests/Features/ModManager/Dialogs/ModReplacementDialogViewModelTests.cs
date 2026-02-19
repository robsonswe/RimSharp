using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.Replacements;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs
{
    public class ModReplacementDialogViewModelTests
    {
        public ModReplacementDialogViewModelTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();
        }

        [Fact]
        public void Constructor_ShouldIdentifyAlreadyInstalledReplacements()
        {
            // Arrange
            var original = new ModItem { Name = "Old Mod", SteamId = "1" };
            var replacementInfo = new ModReplacementInfo { ReplacementSteamId = "2", ReplacementName = "New Mod" };
            var installed = new List<ModItem> { new ModItem { SteamId = "2" } }; // Replacement already installed
            
            var data = new List<(ModItem Original, ModReplacementInfo Replacement, long OriginalUpdate, long ReplacementUpdate)>
            {
                (original, replacementInfo, 100, 200)
            };

            // Act
            var vm = new ModReplacementDialogViewModel(data, installed);

            // Assert
            vm.Replacements.Should().BeEmpty();
            vm.AlreadyInstalledReplacements.Should().HaveCount(1);
            vm.HasAlreadyInstalledReplacements.Should().BeTrue();
        }

        [Fact]
        public void SelectAll_ShouldUpdateSelectedCount()
        {
            // Arrange
            var data = new List<(ModItem Original, ModReplacementInfo Replacement, long OriginalUpdate, long ReplacementUpdate)>
            {
                (new ModItem(), new ModReplacementInfo { ReplacementSteamId = "1" }, 0, 0),
                (new ModItem(), new ModReplacementInfo { ReplacementSteamId = "2" }, 0, 0)
            };
            var vm = new ModReplacementDialogViewModel(data, new List<ModItem>());

            // Act
            vm.SelectAllCommand.Execute(null);

            // Assert
            vm.SelectedCount.Should().Be(2);
            vm.GetSelectedReplacements().Should().HaveCount(2);
        }
    }
}
