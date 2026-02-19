using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.Incompatibilities;
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs
{
    public class ModIncompatibilityDialogViewModelTests
    {
        public ModIncompatibilityDialogViewModelTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();
        }

        [Fact]
        public void ApplyResolutionsCommand_ShouldInvokeCallbackWithSelectedMods()
        {
            // Arrange
            var mod1 = new ModItem { Name = "M1", PackageId = "id1" };
            var mod2 = new ModItem { Name = "M2", PackageId = "id2" };
            
            var group = new IncompatibilityGroup();
            // Use [Hard] to ensure it doesn't add "Keep All" option
            group.AddIncompatibilityRelation(new ModIncompatibilityRelation(mod1, mod2, "[Hard] Conflict"));
            
            List<ModItem>? removedMods = null;
            var vm = new ModIncompatibilityDialogViewModel(
                new List<IncompatibilityGroup> { group },
                mods => removedMods = mods,
                () => { });

            // vm.IncompatibilityGroups[0].ResolutionOptions
            // In a hard conflict between 1 and 2, keeping 1 removes 2, keeping 2 removes 1.
            // Let's select the option that keeps M2 (and thus removes M1)
            var optionToRemoveM1 = vm.IncompatibilityGroups[0].ResolutionOptions.FirstOrDefault(o => o.ModToKeep == mod2);
            if (optionToRemoveM1 != null) optionToRemoveM1.IsSelected = true;

            // Act
            vm.ApplyResolutionsCommand.Execute(null);

            // Assert
            removedMods.Should().NotBeNull();
            removedMods.Should().Contain(mod1);
        }

        [Fact]
        public void CancelCommand_ShouldInvokeCancelCallback()
        {
            // Arrange
            bool cancelled = false;
            var vm = new ModIncompatibilityDialogViewModel(
                new List<IncompatibilityGroup>(),
                _ => { },
                () => cancelled = true);

            // Act
            vm.CancelCommand.Execute(null);

            // Assert
            cancelled.Should().BeTrue();
        }
    }
}
