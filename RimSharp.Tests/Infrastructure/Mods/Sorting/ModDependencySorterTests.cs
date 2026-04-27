using System;
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

            var result = _sorter.TopologicalSort(new List<ModItem>());

            result.IsSuccess.Should().BeTrue();
            result.SortedMods.Should().BeEmpty();
        }

        [Fact]
        public void TopologicalSort_WhenOneMod_ShouldReturnIt()
        {

            var mod = new ModItem { Name = "Mod 1", PackageId = "mod1" };

            var result = _sorter.TopologicalSort(new List<ModItem> { mod });

            result.IsSuccess.Should().BeTrue();
            result.SortedMods.Should().ContainSingle().Which.Should().Be(mod);
        }

        [Fact]
        public void TopologicalSort_CoreShouldBeFirst()
        {

            var coreMod = new ModItem { Name = "Core", PackageId = "Ludeon.RimWorld", ModType = ModType.Core };
            var mod1 = new ModItem { Name = "Mod 1", PackageId = "mod1", ModType = ModType.Workshop };
            var mods = new List<ModItem> { mod1, coreMod };

            var result = _sorter.TopologicalSort(mods);

            result.IsSuccess.Should().BeTrue();
            result.SortedMods[0].Should().Be(coreMod);
            result.SortedMods[1].Should().Be(mod1);
        }

        [Fact]
        public void TopologicalSort_WhenLoadAfterIsPresent_ShouldRespectIt()
        {

            var modA = new ModItem { Name = "Mod A", PackageId = "modA" };
            var modB = new ModItem { Name = "Mod B", PackageId = "modB", LoadAfter = new List<string> { "modA" } };
            var mods = new List<ModItem> { modB, modA };

            var result = _sorter.TopologicalSort(mods);

            result.IsSuccess.Should().BeTrue();
            result.SortedMods.IndexOf(modA).Should().BeLessThan(result.SortedMods.IndexOf(modB));
        }

        [Fact]
        public void TopologicalSort_TierOneMods_WithLoadBeforeCore_ShouldBeFirst()
        {

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

            var result = _sorter.TopologicalSort(mods);

            result.IsSuccess.Should().BeTrue();
            result.SortedMods[0].Should().Be(harmony);
            result.SortedMods[1].Should().Be(core);
        }

        [Fact]
        public void TopologicalSort_CycleDetected_ShouldReportFailure()
        {

            var modA = new ModItem { Name = "Mod A", PackageId = "modA", LoadAfter = new List<string> { "modB" } };
            var modB = new ModItem { Name = "Mod B", PackageId = "modB", LoadAfter = new List<string> { "modA" } };
            var mods = new List<ModItem> { modA, modB };

            var result = _sorter.TopologicalSort(mods);

            result.IsSuccess.Should().BeFalse();
            result.CyclicDependencies.Should().NotBeEmpty();
        }

        [Fact]
        public void TopologicalSort_TierThreeMods_ShouldBeAtBottom()
        {

            var rocketman = new ModItem { Name = "RocketMan", PackageId = "krkr.rocketman", ModType = ModType.Workshop };
            var modA = new ModItem { Name = "Mod A", PackageId = "modA", ModType = ModType.Workshop };
            var mods = new List<ModItem> { rocketman, modA };

            var result = _sorter.TopologicalSort(mods);

            result.IsSuccess.Should().BeTrue();
            result.SortedMods.Last().Should().Be(rocketman);
        }

        [Fact]
        public void TopologicalSort_ChainDependencies_ShouldRespectFullChain()
        {
            // A must come before B, B must come before C
            var modA = new ModItem { Name = "Mod A", PackageId = "modA" };
            var modB = new ModItem { Name = "Mod B", PackageId = "modB", LoadAfter = new List<string> { "modA" } };
            var modC = new ModItem { Name = "Mod C", PackageId = "modC", LoadAfter = new List<string> { "modB" } };
            var mods = new List<ModItem> { modC, modB, modA };

            var result = _sorter.TopologicalSort(mods);

            result.IsSuccess.Should().BeTrue();
            var sorted = result.SortedMods;
            sorted.IndexOf(modA).Should().BeLessThan(sorted.IndexOf(modB));
            sorted.IndexOf(modB).Should().BeLessThan(sorted.IndexOf(modC));
        }

        [Fact]
        public void TopologicalSort_LoadBeforeConstraint_ShouldBeRespected()
        {
            // modA declares it should load before modB
            var modA = new ModItem { Name = "Mod A", PackageId = "modA", LoadBefore = new List<string> { "modB" } };
            var modB = new ModItem { Name = "Mod B", PackageId = "modB" };
            var mods = new List<ModItem> { modB, modA };

            var result = _sorter.TopologicalSort(mods);

            result.IsSuccess.Should().BeTrue();
            result.SortedMods.IndexOf(modA).Should().BeLessThan(result.SortedMods.IndexOf(modB));
        }

        [Fact]
        public void TopologicalSort_MultipleTierOneMods_ShouldAllPrecedeCore()
        {
            var harmony = new ModItem { Name = "Harmony", PackageId = "brrainz.harmony", ModType = ModType.Workshop, LoadBefore = new List<string> { "Ludeon.RimWorld" } };
            var hugsLib = new ModItem { Name = "HugsLib", PackageId = "unlimitedhugs.hugslib", ModType = ModType.Workshop, LoadBefore = new List<string> { "Ludeon.RimWorld" } };
            var core = new ModItem { Name = "Core", PackageId = "Ludeon.RimWorld", ModType = ModType.Core };
            var mods = new List<ModItem> { core, hugsLib, harmony };

            var result = _sorter.TopologicalSort(mods);

            result.IsSuccess.Should().BeTrue();
            var coreIndex = result.SortedMods.IndexOf(core);
            result.SortedMods.IndexOf(harmony).Should().BeLessThan(coreIndex);
            result.SortedMods.IndexOf(hugsLib).Should().BeLessThan(coreIndex);
        }

        [Fact]
        public void TopologicalSort_LoadAfterAndLoadBeforeCombined_ShouldProduceCorrectOrder()
        {
            // modB loads after modA AND before modC
            var modA = new ModItem { Name = "Mod A", PackageId = "modA" };
            var modB = new ModItem { Name = "Mod B", PackageId = "modB", LoadAfter = new List<string> { "modA" }, LoadBefore = new List<string> { "modC" } };
            var modC = new ModItem { Name = "Mod C", PackageId = "modC" };
            var mods = new List<ModItem> { modC, modA, modB };

            var result = _sorter.TopologicalSort(mods);

            result.IsSuccess.Should().BeTrue();
            var sorted = result.SortedMods;
            sorted.IndexOf(modA).Should().BeLessThan(sorted.IndexOf(modB));
            sorted.IndexOf(modB).Should().BeLessThan(sorted.IndexOf(modC));
        }

        [Fact]
        public void TopologicalSort_WhenCancelled_ShouldThrowOrReturnEarly()
        {
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();

            var mods = new List<ModItem>
            {
                new ModItem { Name = "Mod A", PackageId = "modA" },
                new ModItem { Name = "Mod B", PackageId = "modB" }
            };

            Action act = () => _sorter.TopologicalSort(mods, cts.Token);

            act.Should().Throw<OperationCanceledException>();
        }

        [Fact]
        public void TopologicalSort_UnrelatedMods_ShouldAllAppearInResult()
        {
            var modA = new ModItem { Name = "Mod A", PackageId = "modA" };
            var modB = new ModItem { Name = "Mod B", PackageId = "modB" };
            var modC = new ModItem { Name = "Mod C", PackageId = "modC" };
            var mods = new List<ModItem> { modA, modB, modC };

            var result = _sorter.TopologicalSort(mods);

            result.IsSuccess.Should().BeTrue();
            result.SortedMods.Should().HaveCount(3);
            result.SortedMods.Should().Contain(new[] { modA, modB, modC });
        }
    }
}

