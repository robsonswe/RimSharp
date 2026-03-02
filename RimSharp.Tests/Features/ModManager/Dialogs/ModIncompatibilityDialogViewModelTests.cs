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

            var mod1 = new ModItem { Name = "M1", PackageId = "id1" };
            var mod2 = new ModItem { Name = "M2", PackageId = "id2" };
            
            var group = new IncompatibilityGroup();

            group.AddIncompatibilityRelation(new ModIncompatibilityRelation(mod1, mod2, "[Hard] Conflict"));
            
            List<ModItem>? removedMods = null;
            var vm = new ModIncompatibilityDialogViewModel(
                new List<IncompatibilityGroup> { group },
                mods => removedMods = mods,
                () => { });

            // vm.IncompatibilityGroups[0].ResolutionOptions

var optionToRemoveM1 = vm.IncompatibilityGroups[0].ResolutionOptions.FirstOrDefault(o => o.ModToKeep == mod2);
            if (optionToRemoveM1 != null) optionToRemoveM1.IsSelected = true;

            vm.ApplyResolutionsCommand.Execute(null);

            removedMods.Should().NotBeNull();
            removedMods.Should().Contain(mod1);
        }

        [Fact]
        public void CancelCommand_ShouldInvokeCancelCallback()
        {

            bool cancelled = false;
            var vm = new ModIncompatibilityDialogViewModel(
                new List<IncompatibilityGroup>(),
                _ => { },
                () => cancelled = true);

            vm.CancelCommand.Execute(null);

            cancelled.Should().BeTrue();
        }
    }
}


