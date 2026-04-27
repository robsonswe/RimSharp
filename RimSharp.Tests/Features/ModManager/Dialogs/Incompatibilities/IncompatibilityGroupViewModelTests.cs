using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.Incompatibilities;
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs.Incompatibilities
{
    public class IncompatibilityGroupViewModelTests
    {
        private static IncompatibilityGroup CreateGroup(ModItem source, ModItem target, string reason = "Soft incompatibility")
        {
            var group = new IncompatibilityGroup();
            group.AddIncompatibilityRelation(new ModIncompatibilityRelation(source, target, reason));
            return group;
        }

        [Fact]
        public void Constructor_ShouldSetGroup()
        {
            var modA = new ModItem { Name = "Mod A", PackageId = "mod.a" };
            var modB = new ModItem { Name = "Mod B", PackageId = "mod.b" };
            var group = CreateGroup(modA, modB);

            var vm = new IncompatibilityGroupViewModel(group);

            vm.Group.Should().BeSameAs(group);
        }

        [Fact]
        public void ContainsHardIncompatibility_WhenReasonStartsWithHard_ShouldBeTrue()
        {
            var modA = new ModItem { Name = "Mod A", PackageId = "mod.a" };
            var modB = new ModItem { Name = "Mod B", PackageId = "mod.b" };
            var group = CreateGroup(modA, modB, "[Hard] These mods conflict");

            var vm = new IncompatibilityGroupViewModel(group);

            vm.ContainsHardIncompatibility.Should().BeTrue();
        }

        [Fact]
        public void ContainsHardIncompatibility_WhenReasonIsSoft_ShouldBeFalse()
        {
            var modA = new ModItem { Name = "Mod A", PackageId = "mod.a" };
            var modB = new ModItem { Name = "Mod B", PackageId = "mod.b" };
            var group = CreateGroup(modA, modB, "Soft incompatibility");

            var vm = new IncompatibilityGroupViewModel(group);

            vm.ContainsHardIncompatibility.Should().BeFalse();
        }

        [Fact]
        public void ResolutionOptions_WhenSoftIncompatibility_ShouldIncludeKeepAllOption()
        {
            var modA = new ModItem { Name = "Mod A", PackageId = "mod.a" };
            var modB = new ModItem { Name = "Mod B", PackageId = "mod.b" };
            var group = CreateGroup(modA, modB, "Soft incompatibility");

            var vm = new IncompatibilityGroupViewModel(group);

            // First option should be "keep all" (ModToKeep is null)
            vm.ResolutionOptions.Should().NotBeEmpty();
            var keepAllOption = vm.ResolutionOptions.FirstOrDefault(o => o.ModToKeep == null);
            keepAllOption.Should().NotBeNull();
        }

        [Fact]
        public void ResolutionOptions_WhenSoftIncompatibility_FirstOptionShouldBeSelected()
        {
            var modA = new ModItem { Name = "Mod A", PackageId = "mod.a" };
            var modB = new ModItem { Name = "Mod B", PackageId = "mod.b" };
            var group = CreateGroup(modA, modB, "Soft incompatibility");

            var vm = new IncompatibilityGroupViewModel(group);

            vm.ResolutionOptions.First().IsSelected.Should().BeTrue();
        }

        [Fact]
        public void ResolutionOptions_WhenHardIncompatibility_ShouldNotIncludeKeepAllOption()
        {
            var modA = new ModItem { Name = "Mod A", PackageId = "mod.a" };
            var modB = new ModItem { Name = "Mod B", PackageId = "mod.b" };
            var group = CreateGroup(modA, modB, "[Hard] These mods conflict");

            var vm = new IncompatibilityGroupViewModel(group);

            var keepAllOption = vm.ResolutionOptions.FirstOrDefault(o => o.ModToKeep == null);
            keepAllOption.Should().BeNull();
        }

        [Fact]
        public void GetSelectedModToKeep_WhenKeepAllSelected_ShouldReturnNull()
        {
            var modA = new ModItem { Name = "Mod A", PackageId = "mod.a" };
            var modB = new ModItem { Name = "Mod B", PackageId = "mod.b" };
            var group = CreateGroup(modA, modB, "Soft incompatibility");

            var vm = new IncompatibilityGroupViewModel(group);
            // First option (keep all) should be selected by default
            vm.ResolutionOptions.First().IsSelected = true;

            var result = vm.GetSelectedModToKeep();

            result.Should().BeNull();
        }

        [Fact]
        public void GetSelectedModsToRemove_WhenNoOptionSelected_ShouldReturnEmpty()
        {
            var modA = new ModItem { Name = "Mod A", PackageId = "mod.a" };
            var modB = new ModItem { Name = "Mod B", PackageId = "mod.b" };
            var group = CreateGroup(modA, modB, "Soft incompatibility");

            var vm = new IncompatibilityGroupViewModel(group);
            foreach (var option in vm.ResolutionOptions)
                option.IsSelected = false;

            var result = vm.GetSelectedModsToRemove();

            result.Should().BeEmpty();
        }

        [Fact]
        public void GroupName_ShouldBeNonEmpty()
        {
            var modA = new ModItem { Name = "Mod A", PackageId = "mod.a" };
            var modB = new ModItem { Name = "Mod B", PackageId = "mod.b" };
            var group = CreateGroup(modA, modB);

            var vm = new IncompatibilityGroupViewModel(group);

            vm.GroupName.Should().NotBeNullOrEmpty();
        }
    }
}
