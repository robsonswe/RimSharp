using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.ModManager.Services.Management;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Services.Management
{
    public class ModRemovalTests
    {
        private readonly IModDictionaryService _mockDictionaryService;
        private readonly ModListManager _manager;

        public ModRemovalTests()
        {
            _mockDictionaryService = Substitute.For<IModDictionaryService>();
            _manager = new ModListManager(_mockDictionaryService);
        }

        [Fact]
        public void RemoveMods_ShouldRemoveFromAllLists()
        {

            var mod1 = new ModItem { Name = "Mod 1", PackageId = "mod1" };
            var mod2 = new ModItem { Name = "Mod 2", PackageId = "mod2" };
            _manager.Initialize(new[] { mod1, mod2 }, new[] { "mod1" });

            _manager.RemoveMods(new[] { mod1 });

            _manager.GetAllMods().Should().NotContain(mod1);
            _manager.VirtualActiveMods.Should().BeEmpty();
            _manager.AllInactiveMods.Should().ContainSingle().Which.Should().Be(mod2);
        }

        [Fact]
        public void RemoveMods_WhenActiveModRemoved_ShouldReturnInstanceRemovedTrue()
        {

            var mod1 = new ModItem { Name = "Mod 1", PackageId = "mod1" };
            _manager.Initialize(new[] { mod1 }, new[] { "mod1" });

            var result = _manager.RemoveMods(new[] { mod1 });

            result.InstanceRemoved.Should().BeTrue();
            result.ActivePackageIdLost.Should().BeTrue(); // No duplicate, so ID lost
        }

        [Fact]
        public void RemoveMods_WhenInactiveModRemoved_ShouldReturnFalse()
        {

            var mod1 = new ModItem { Name = "Mod 1", PackageId = "mod1" };
            _manager.Initialize(new[] { mod1 }, new string[0]);

            var result = _manager.RemoveMods(new[] { mod1 });

            result.InstanceRemoved.Should().BeFalse();
            result.ActivePackageIdLost.Should().BeFalse();
        }

        [Fact]
        public void RemoveMods_WithDuplicates_ShouldHandoverLookupEntryAndPreservePackageId()
        {

            var mod1V1 = new ModItem { Name = "Mod 1 (v1)", PackageId = "mod1", Path = "path1" };
            var mod1V2 = new ModItem { Name = "Mod 1 (v2)", PackageId = "mod1", Path = "path2" };
            var requester = new ModItem 
            { 
                Name = "Requester", 
                PackageId = "req", 
                ModDependencies = new List<ModDependency> { new ModDependency { PackageId = "mod1" } } 
            };

            _manager.Initialize(new[] { mod1V1, mod1V2, requester }, new[] { "req", "mod1" });

            if (!_manager.IsModActive(mod1V1))
            {

                 _manager.DeactivateMod(mod1V2);
                 _manager.ActivateMod(mod1V1);
            }

            _manager.IsModActive(mod1V1).Should().BeTrue();

            // Act: Remove mod1V1 (the active one)
            var result = _manager.RemoveMods(new[] { mod1V1 });

            _manager.GetAllMods().Should().NotContain(mod1V1);
            _manager.GetAllMods().Should().Contain(mod1V2);

            // Result checks
            result.InstanceRemoved.Should().BeTrue("The specific instance was removed");
            result.ActivePackageIdLost.Should().BeFalse("The PackageId should be preserved via swap");

            _manager.IsModActive(mod1V2).Should().BeTrue("The duplicate should have been activated");
            _manager.VirtualActiveMods.Should().Contain(x => x.Mod == mod1V2);
        }

        [Fact]
        public void RemoveMods_WhenInactiveModRemoved_ShouldRaiseListChanged()
        {

            var mod1 = new ModItem { Name = "Mod 1", PackageId = "mod1" };
            _manager.Initialize(new[] { mod1 }, new string[0]);
            bool eventRaised = false;
            _manager.ListChanged += (s, e) => eventRaised = true;

            _manager.RemoveMods(new[] { mod1 });

            eventRaised.Should().BeTrue("ListChanged event should be raised even for inactive mod removal to update the UI");
        }

        [Fact]
        public void RemoveMods_BatchDeletion_ShouldWork()
        {

            var mod1 = new ModItem { Name = "Mod 1", PackageId = "mod1" };
            var mod2 = new ModItem { Name = "Mod 2", PackageId = "mod2" };
            var mod3 = new ModItem { Name = "Mod 3", PackageId = "mod3" };
            _manager.Initialize(new[] { mod1, mod2, mod3 }, new[] { "mod1", "mod2" });

            var result = _manager.RemoveMods(new[] { mod1, mod3 });

            _manager.GetAllMods().Should().ContainSingle().Which.Should().Be(mod2);
            _manager.VirtualActiveMods.Should().ContainSingle().Which.Mod.Should().Be(mod2);
            _manager.AllInactiveMods.Should().BeEmpty();

            result.InstanceRemoved.Should().BeTrue(); 
            result.ActivePackageIdLost.Should().BeTrue(); // mod1 had no duplicate
        }
    }
}


