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
    public class ModListManagerTests
    {
        private readonly IModDictionaryService _mockDictionaryService;
        private readonly ModListManager _manager;

        public ModListManagerTests()
        {
            _mockDictionaryService = Substitute.For<IModDictionaryService>();
            _manager = new ModListManager(_mockDictionaryService);
        }

        [Fact]
        public void Initialize_ShouldPopulateActiveAndInactiveLists()
        {

            var mod1 = new ModItem { Name = "Mod 1", PackageId = "mod1" };
            var mod2 = new ModItem { Name = "Mod 2", PackageId = "mod2" };
            var allMods = new List<ModItem> { mod1, mod2 };
            var activeIds = new List<string> { "mod1" };

            _manager.Initialize(allMods, activeIds);

            _manager.VirtualActiveMods.Should().HaveCount(1);
            _manager.VirtualActiveMods[0].Mod.PackageId.Should().Be("mod1");
            _manager.AllInactiveMods.Should().HaveCount(1);
            _manager.AllInactiveMods[0].PackageId.Should().Be("mod2");
        }

        [Fact]
        public void ActivateMod_ShouldMoveFromInactiveToActive()
        {

            var mod1 = new ModItem { Name = "Mod 1", PackageId = "mod1" };
            _manager.Initialize(new[] { mod1 }, new string[0]);

            _manager.ActivateMod(mod1);

            _manager.VirtualActiveMods.Should().HaveCount(1);
            _manager.AllInactiveMods.Should().BeEmpty();
        }

        [Fact]
        public void DeactivateMod_ShouldMoveFromActiveToInactive()
        {

            var mod1 = new ModItem { Name = "Mod 1", PackageId = "mod1", ModType = ModType.Workshop };
            _manager.Initialize(new[] { mod1 }, new[] { "mod1" });

            _manager.DeactivateMod(mod1);

            _manager.VirtualActiveMods.Should().BeEmpty();
            _manager.AllInactiveMods.Should().HaveCount(1);
        }

        [Fact]
        public void RecalculateActiveModIssues_ShouldDetectMissingDependency()
        {

            var mod1 = new ModItem 
            { 
                Name = "Mod 1", 
                PackageId = "mod1", 
                ModDependencies = new List<ModDependency> { new ModDependency { PackageId = "missing" } } 
            };
            _manager.Initialize(new[] { mod1 }, new[] { "mod1" });

_manager.HasAnyActiveModIssues.Should().BeTrue();
            mod1.HasIssues.Should().BeTrue();
            mod1.IssueTooltipText.Should().Contain("Dependency missing");
        }

        [Fact]
        public void RecalculateActiveModIssues_ShouldDetectLoadOrderViolation()
        {

            var modA = new ModItem { Name = "Mod A", PackageId = "modA" };
            var modB = new ModItem 
            { 
                Name = "Mod B", 
                PackageId = "modB", 
                LoadAfter = new List<string> { "modA" } 
            };

            _manager.Initialize(new[] { modA, modB }, new[] { "modB", "modA" });

            modB.HasIssues.Should().BeTrue();
            modB.IssueTooltipText.Should().Contain("Load order: Should load after 'Mod A', but loads before.");
        }

        [Fact]
        public void ResolveDependencies_WhenUrlIsInvalidSteamProtocol_ShouldLookupInDictionary()
        {

            var mod = new ModItem 
            { 
                Name = "Mod With Bad Dep", 
                PackageId = "mod.bad", 
                ModDependencies = new List<ModDependency> 
                { 
                    new ModDependency { PackageId = "missing.dep", SteamWorkshopUrl = "steam://12345" } 
                } 
            };
            _manager.Initialize(new[] { mod }, new[] { "mod.bad" });

            var dictionaryEntry = new ModDictionaryEntry { PackageId = "missing.dep", SteamId = "99999", Name = "Real Name" };
            _mockDictionaryService.GetEntryByPackageId("missing.dep").Returns(dictionaryEntry);

            var result = _manager.ResolveDependencies();

            result.missingDependencies.Should().HaveCount(1);
            var missing = result.missingDependencies[0];
            missing.packageId.Should().Be("missing.dep");
            missing.steamUrl.Should().Be("https://steamcommunity.com/workshop/filedetails/?id=99999");
            missing.displayName.Should().Be("Real Name");
        }

        [Fact]
        public void ResolveDependencies_ShouldConsolidateMultipleRequesters()
        {

            var dep = new ModDependency { PackageId = "shared.dep" };
            var modA = new ModItem { Name = "Mod A", PackageId = "mod.a", ModDependencies = new List<ModDependency> { dep } };
            var modB = new ModItem { Name = "Mod B", PackageId = "mod.b", ModDependencies = new List<ModDependency> { dep } };
            
            _manager.Initialize(new[] { modA, modB }, new[] { "mod.a", "mod.b" });

            var result = _manager.ResolveDependencies();

            result.missingDependencies.Should().HaveCount(1);
            result.missingDependencies[0].requiredBy.Should().HaveCount(2);
            result.missingDependencies[0].requiredBy.Should().Contain(new[] { "Mod A", "Mod B" });
        }

        [Fact]
        public void ResolveDependencies_WhenDuplicateInactiveModsExist_ShouldNotCrash()
        {

            var mod = new ModItem 
            { 
                Name = "Mod", 
                PackageId = "mod", 
                ModDependencies = new List<ModDependency> { new ModDependency { PackageId = "dup" } } 
            };
            var dup1 = new ModItem { Name = "Dup 1", PackageId = "dup" };
            var dup2 = new ModItem { Name = "Dup 2", PackageId = "dup" };

            _manager.Initialize(new[] { mod, dup1, dup2 }, new[] { "mod" });

            var result = _manager.ResolveDependencies();

            result.addedMods.Should().Contain(dup1);
            result.addedMods.Should().NotContain(dup2);
            _manager.IsModActive(dup1).Should().BeTrue();
        }
    }
}


