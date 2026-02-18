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
            // Arrange
            var mod1 = new ModItem { Name = "Mod 1" };
            var mod2 = new ModItem { Name = "Mod 2" };
            var initial = new[] { (mod2, 1), (mod1, 0) };

            // Act
            _service.Initialize(initial);

            // Assert
            _service.VirtualActiveMods[0].Mod.Should().Be(mod1);
            _service.VirtualActiveMods[1].Mod.Should().Be(mod2);
        }

        [Fact]
        public void AddMod_ShouldInsertAtIndex()
        {
            // Arrange
            var mod1 = new ModItem { Name = "Mod 1" };
            var mod2 = new ModItem { Name = "Mod 2" };
            var mod3 = new ModItem { Name = "Mod 3" };
            _service.AddModsAt(new[] { mod1, mod3 }, 0);

            // Act
            _service.AddMod(mod2, 1);

            // Assert
            _service.VirtualActiveMods[1].Mod.Should().Be(mod2);
            _service.VirtualActiveMods[1].LoadOrder.Should().Be(1);
        }

        [Fact]
        public void AddMod_ShouldPreventDuplicates()
        {
            // Arrange
            var mod1 = new ModItem { Name = "Mod 1" };
            _service.AddMod(mod1, 0);

            // Act
            _service.AddMod(mod1, 1);

            // Assert
            _service.VirtualActiveMods.Should().HaveCount(1);
        }

        [Fact]
        public void ReorderMods_ShouldMoveItemsCorrectly()
        {
            // Arrange
            var mods = Enumerable.Range(1, 5).Select(i => new ModItem { Name = $"Mod {i}" }).ToList();
            _service.AddModsAt(mods, 0);
            // Current: 1, 2, 3, 4, 5

            // Act: Move Mod 2 and Mod 4 to index 1 (between 1 and 3)
            // Wait, index 1 is currently Mod 2. 
            // If we move Mod 2 and Mod 4 to target index 1:
            // Relative order of moving items: 2, 4
            // Items before target (index 1): Mod 1 (index 0) is before.
            // Items being moved: 2 (index 1), 4 (index 3).
            // Count of moving items before target index 1: Mod 2 is at index 1, so NOT < 1. 0 items.
            // actualInsertionIndex = 1 - 0 = 1.
            // Final: 1, 2, 4, 3, 5
            _service.ReorderMods(new[] { mods[1], mods[3] }, 1);

            // Assert
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
            // Arrange
            var mods = Enumerable.Range(1, 3).Select(i => new ModItem { Name = $"Mod {i}" }).ToList();
            _service.AddModsAt(mods, 0);

            // Act: Move Mod 1 to the end
            _service.ReorderMods(new[] { mods[0] }, 3);

            // Assert
            var result = _service.VirtualActiveMods.Select(x => x.Mod).ToList();
            result[0].Name.Should().Be("Mod 2");
            result[1].Name.Should().Be("Mod 3");
            result[2].Name.Should().Be("Mod 1");
        }
    }
}
