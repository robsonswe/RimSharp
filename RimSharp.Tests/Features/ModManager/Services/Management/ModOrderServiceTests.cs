using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RimSharp.Features.ModManager.Services.Management;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Services.Management
{
    public class ModOrderServiceTests
    {
        private readonly ModOrderService _service;

        public ModOrderServiceTests()
        {
            _service = new ModOrderService();
        }

        [Fact]
        public void Initialize_ShouldSetCorrectOrder()
        {

            var mod1 = new ModItem { Name = "Mod 1" };
            var mod2 = new ModItem { Name = "Mod 2" };
            var initial = new[] { (mod2, 1), (mod1, 0) };

            _service.Initialize(initial);

            _service.VirtualActiveMods[0].Mod.Should().Be(mod1);
            _service.VirtualActiveMods[1].Mod.Should().Be(mod2);
        }

        [Fact]
        public void AddMod_ShouldInsertAtIndex()
        {

            var mod1 = new ModItem { Name = "Mod 1" };
            var mod2 = new ModItem { Name = "Mod 2" };
            var mod3 = new ModItem { Name = "Mod 3" };
            _service.AddModsAt(new[] { mod1, mod3 }, 0);

            _service.AddMod(mod2, 1);

            _service.VirtualActiveMods[1].Mod.Should().Be(mod2);
            _service.VirtualActiveMods[1].LoadOrder.Should().Be(1);
        }

        [Fact]
        public void AddMod_ShouldPreventDuplicates()
        {

            var mod1 = new ModItem { Name = "Mod 1" };
            _service.AddMod(mod1, 0);

            _service.AddMod(mod1, 1);

            _service.VirtualActiveMods.Should().HaveCount(1);
        }

        [Fact]
        public void ReorderMods_ShouldMoveItemsCorrectly()
        {

            var mods = Enumerable.Range(1, 5).Select(i => new ModItem { Name = $"Mod {i}" }).ToList();
            _service.AddModsAt(mods, 0);
            // Current: 1, 2, 3, 4, 5

            // Wait, index 1 is currently Mod 2. 

            // Relative order of moving items: 2, 4

// actualInsertionIndex = 1 - 0 = 1.
            // Final: 1, 2, 4, 3, 5
            _service.ReorderMods(new[] { mods[1], mods[3] }, 1);

            var result = _service.VirtualActiveMods.Select(x => x.Mod).ToList();
            result[0].Name.Should().Be("Mod 1");
            result[1].Name.Should().Be("Mod 2");
            result[2].Name.Should().Be("Mod 4");
            result[3].Name.Should().Be("Mod 3");
            result[4].Name.Should().Be("Mod 5");
        }

        [Fact]
        public void ReorderMods_MoveToBottom_ShouldWork()
        {

            var mods = Enumerable.Range(1, 3).Select(i => new ModItem { Name = $"Mod {i}" }).ToList();
            _service.AddModsAt(mods, 0);

            // Act: Move Mod 1 to the end
            _service.ReorderMods(new[] { mods[0] }, 3);

            var result = _service.VirtualActiveMods.Select(x => x.Mod).ToList();
            result[0].Name.Should().Be("Mod 2");
            result[1].Name.Should().Be("Mod 3");
            result[2].Name.Should().Be("Mod 1");
        }

        [Fact]
        public void RemoveMod_ShouldRemoveFromList()
        {

            var mod1 = new ModItem { Name = "Mod 1" };
            var mod2 = new ModItem { Name = "Mod 2" };
            _service.AddModsAt(new[] { mod1, mod2 }, 0);

            _service.RemoveMod(mod1);

            _service.VirtualActiveMods.Should().HaveCount(1);
            _service.VirtualActiveMods[0].Mod.Should().Be(mod2);
        }

        [Fact]
        public void RemoveMod_WhenModNotPresent_ShouldNotThrow()
        {

            var mod1 = new ModItem { Name = "Mod 1" };
            var notAdded = new ModItem { Name = "Not Added" };
            _service.AddMod(mod1, 0);

            Action act = () => _service.RemoveMod(notAdded);

            act.Should().NotThrow();
        }

        [Fact]
        public void RemoveMods_ShouldRemoveMultipleAtOnce()
        {

            var mod1 = new ModItem { Name = "Mod 1" };
            var mod2 = new ModItem { Name = "Mod 2" };
            var mod3 = new ModItem { Name = "Mod 3" };
            _service.AddModsAt(new[] { mod1, mod2, mod3 }, 0);

            _service.RemoveMods(new[] { mod1, mod3 });

            _service.VirtualActiveMods.Should().HaveCount(1);
            _service.VirtualActiveMods[0].Mod.Should().Be(mod2);
        }

        [Fact]
        public void Clear_ShouldEmptyTheList()
        {

            var mod1 = new ModItem { Name = "Mod 1" };
            _service.AddMod(mod1, 0);

            _service.Clear();

            _service.VirtualActiveMods.Should().BeEmpty();
        }

        [Fact]
        public void AddMod_ShouldClampIndexOutOfRange()
        {

            var mod1 = new ModItem { Name = "Mod 1" };
            _service.AddMod(mod1, 0);

            var mod2 = new ModItem { Name = "Mod 2" };
            _service.AddMod(mod2, 999); // Out of range — should be clamped to end

            _service.VirtualActiveMods.Last().Mod.Should().Be(mod2);
        }
    }
}


