using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Mods.Sorting;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Mods.Sorting
{
    public class ModDependencySorterTests
    {
        private readonly ModDependencySorter _sorter;
        private readonly ILogger _mockLogger;

        public ModDependencySorterTests()
        {
            _mockLogger = Substitute.For<ILogger>();
            _sorter = new ModDependencySorter(_mockLogger);
        }

        [Fact]
        public void TopologicalSort_WhenEmpty_ShouldBeSuccess()
        {
            // Act
            var result = _sorter.TopologicalSort(new List<ModItem>());

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.SortedMods.Should().BeEmpty();
        }

        [Fact]
        public void TopologicalSort_WhenOneMod_ShouldReturnIt()
        {
            // Arrange
            var mod = new ModItem { Name = "Mod 1", PackageId = "mod1" };

            // Act
            var result = _sorter.TopologicalSort(new List<ModItem> { mod });

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.SortedMods.Should().ContainSingle().Which.Should().Be(mod);
        }

        [Fact]
        public void TopologicalSort_CoreShouldBeFirst()
        {
            // Arrange
            var coreMod = new ModItem { Name = "Core", PackageId = "Ludeon.RimWorld", ModType = ModType.Core };
            var mod1 = new ModItem { Name = "Mod 1", PackageId = "mod1", ModType = ModType.Workshop };
            var mods = new List<ModItem> { mod1, coreMod };

            // Act
            var result = _sorter.TopologicalSort(mods);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.SortedMods[0].Should().Be(coreMod);
            result.SortedMods[1].Should().Be(mod1);
        }

        [Fact]
        public void TopologicalSort_WhenLoadAfterIsPresent_ShouldRespectIt()
        {
            // Arrange
            var modA = new ModItem { Name = "Mod A", PackageId = "modA" };
            var modB = new ModItem { Name = "Mod B", PackageId = "modB", LoadAfter = new List<string> { "modA" } };
            var mods = new List<ModItem> { modB, modA };

            // Act
            var result = _sorter.TopologicalSort(mods);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.SortedMods.IndexOf(modA).Should().BeLessThan(result.SortedMods.IndexOf(modB));
        }

        [Fact]
        public void TopologicalSort_TierOneMods_WithLoadBeforeCore_ShouldBeFirst()
        {
            // Arrange
            var harmony = new ModItem 
            { 
                Name = "Harmony", 
                PackageId = "brrainz.harmony", 
                ModType = ModType.Workshop,
                LoadBefore = new List<string> { "Ludeon.RimWorld" }
            };
            var core = new ModItem 
            { 
                Name = "Core", 
                PackageId = "Ludeon.RimWorld", 
                ModType = ModType.Core 
            };
            var mods = new List<ModItem> { core, harmony };

            // Act
            var result = _sorter.TopologicalSort(mods);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.SortedMods[0].Should().Be(harmony);
            result.SortedMods[1].Should().Be(core);
        }

        [Fact]
        public void TopologicalSort_CycleDetected_ShouldReportFailure()
        {
            // Arrange
            var modA = new ModItem { Name = "Mod A", PackageId = "modA", LoadAfter = new List<string> { "modB" } };
            var modB = new ModItem { Name = "Mod B", PackageId = "modB", LoadAfter = new List<string> { "modA" } };
            var mods = new List<ModItem> { modA, modB };

            // Act
            var result = _sorter.TopologicalSort(mods);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.CyclicDependencies.Should().NotBeEmpty();
        }

        [Fact]
        public void TopologicalSort_TierThreeMods_ShouldBeAtBottom()
        {
            // Arrange
            var rocketman = new ModItem { Name = "RocketMan", PackageId = "krkr.rocketman", ModType = ModType.Workshop };
            var modA = new ModItem { Name = "Mod A", PackageId = "modA", ModType = ModType.Workshop };
            var mods = new List<ModItem> { rocketman, modA };

            // Act
            var result = _sorter.TopologicalSort(mods);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.SortedMods.Last().Should().Be(rocketman);
        }
    }
}
