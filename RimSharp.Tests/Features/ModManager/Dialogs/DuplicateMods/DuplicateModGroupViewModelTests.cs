using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.DuplicateMods;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs.DuplicateMods
{
    public class DuplicateModGroupViewModelTests
    {
        private static IGrouping<string, ModItem> CreateGroup(string packageId, params ModItem[] items)
        {
            foreach (var item in items) item.PackageId = packageId;
            return items.GroupBy(m => m.PackageId!).First();
        }

        [Fact]
        public void Constructor_ShouldSetPackageId()
        {
            var mod1 = new ModItem { Name = "Mod 1", Path = "path1" };
            var mod2 = new ModItem { Name = "Mod 2", Path = "path2" };
            var group = CreateGroup("test.mod", mod1, mod2);

            var vm = new DuplicateModGroupViewModel(group);

            vm.PackageId.Should().Be("test.mod");
        }

        [Fact]
        public void Constructor_ShouldPopulateMods()
        {
            var mod1 = new ModItem { Name = "Mod 1", Path = "path1" };
            var mod2 = new ModItem { Name = "Mod 2", Path = "path2" };
            var group = CreateGroup("test.mod", mod1, mod2);

            var vm = new DuplicateModGroupViewModel(group);

            vm.Mods.Should().HaveCount(2);
        }

        [Fact]
        public void Constructor_IsIgnored_ShouldBeFalseByDefault()
        {
            var mod1 = new ModItem { Name = "Mod 1" };
            var mod2 = new ModItem { Name = "Mod 2" };
            var group = CreateGroup("test.mod", mod1, mod2);

            var vm = new DuplicateModGroupViewModel(group);

            vm.IsIgnored.Should().BeFalse();
        }

        [Fact]
        public void IsIgnored_SetToTrue_ShouldDeactivateAllMods()
        {
            var mod1 = new ModItem { Name = "Mod 1" };
            var mod2 = new ModItem { Name = "Mod 2" };
            var group = CreateGroup("test.mod", mod1, mod2);
            var vm = new DuplicateModGroupViewModel(group);

            vm.IsIgnored = true;

            vm.Mods.Should().AllSatisfy(m => m.IsActive.Should().BeFalse());
        }

        [Fact]
        public void IsIgnored_SetPropertyShouldRaisePropertyChanged()
        {
            var mod1 = new ModItem { Name = "Mod 1" };
            var mod2 = new ModItem { Name = "Mod 2" };
            var group = CreateGroup("test.mod", mod1, mod2);
            var vm = new DuplicateModGroupViewModel(group);
            var raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(vm.IsIgnored)) raised = true; };

            vm.IsIgnored = true;

            raised.Should().BeTrue();
        }

        [Fact]
        public void GetSelectedModToKeep_WhenOneModIsActive_ShouldReturnThatMod()
        {
            var mod1 = new ModItem { Name = "Mod 1", Path = "path1", IsActive = false };
            var mod2 = new ModItem { Name = "Mod 2", Path = "path2", IsActive = false };
            var group = CreateGroup("test.mod", mod1, mod2);
            var vm = new DuplicateModGroupViewModel(group);

            vm.Mods.First(m => m.Original == mod1).IsActive = true;
            vm.Mods.First(m => m.Original == mod2).IsActive = false;

            var selected = vm.GetSelectedModToKeep();

            selected.Should().Be(mod1);
        }

        [Fact]
        public void GetSelectedModToKeep_WhenAllModsInactive_ShouldReturnNull()
        {
            var mod1 = new ModItem { Name = "Mod 1", Path = "path1", IsActive = false };
            var mod2 = new ModItem { Name = "Mod 2", Path = "path2", IsActive = false };
            var group = CreateGroup("test.mod", mod1, mod2);
            var vm = new DuplicateModGroupViewModel(group);

            foreach (var wrapper in vm.Mods)
                wrapper.IsActive = false;

            var selected = vm.GetSelectedModToKeep();

            selected.Should().BeNull();
        }

        [Fact]
        public void UpdateSelection_ShouldSetIsIgnoredToFalse()
        {
            var mod1 = new ModItem { Name = "Mod 1", Path = "path1" };
            var mod2 = new ModItem { Name = "Mod 2", Path = "path2" };
            var group = CreateGroup("test.mod", mod1, mod2);
            var vm = new DuplicateModGroupViewModel(group);
            vm.IsIgnored = true;

            var selected = vm.Mods.First();
            vm.UpdateSelection(selected);

            vm.IsIgnored.Should().BeFalse();
        }

        [Fact]
        public void UpdateSelection_ShouldDeactivateOtherMods()
        {
            var mod1 = new ModItem { Name = "Mod 1", Path = "path1" };
            var mod2 = new ModItem { Name = "Mod 2", Path = "path2" };
            var group = CreateGroup("test.mod", mod1, mod2);
            var vm = new DuplicateModGroupViewModel(group);

            var selected = vm.Mods[0];
            var other = vm.Mods[1];
            other.IsActive = true;

            vm.UpdateSelection(selected);

            other.IsActive.Should().BeFalse();
        }

        [Fact]
        public void Constructor_WithSteamIds_ShouldMarkLowestSteamIdAsOriginal()
        {
            var mod1 = new ModItem { Name = "Original", SteamId = "100" };
            var mod2 = new ModItem { Name = "Later Upload", SteamId = "200" };
            var group = CreateGroup("test.mod", mod1, mod2);

            var vm = new DuplicateModGroupViewModel(group);

            var originalWrapper = vm.Mods.FirstOrDefault(m => m.IsOriginal);
            originalWrapper.Should().NotBeNull();
            originalWrapper!.Original.Should().Be(mod1);
        }
    }
}
