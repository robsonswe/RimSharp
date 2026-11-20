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
        private readonly IPathService _mockPathService;
        private readonly ModListManager _manager;

        public ModListManagerTests()
        {
            _mockPathService = Substitute.For<IPathService>();
            _mockPathService.GetMajorGameVersion().Returns("1.7");
            _manager = new ModListManager(_mockPathService);
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
        public void Initialize_SetsCurrentMajorGameVersionFromPathService()
        {
            _mockPathService.GetMajorGameVersion().Returns("1.7");

            _manager.Initialize(new ModItem[0], new string[0]);

            _manager.CurrentMajorGameVersion.Should().Be("1.7");
        }

        [Theory]
        [InlineData("N/A")]
        [InlineData("N/A (unknown)")]
        [InlineData("")]
        [InlineData(null)]
        public void Initialize_FallsBackToUnknown_WhenPathServiceVersionIsUnusable(string? version)
        {
            _mockPathService.GetMajorGameVersion().Returns(version!);

            _manager.Initialize(new ModItem[0], new string[0]);

            _manager.CurrentMajorGameVersion.Should().Be("Unknown");
        }

        [Fact]
        public void Initialize_ReportsTheListAsUnmodified_WhenItWasReadFromTheConfig()
        {
            var mod1 = new ModItem { Name = "Mod 1", PackageId = "mod1" };

            ModListChangedEventArgs? raised = null;
            _manager.ListChanged += (_, e) => raised = e;

            // The app's own start: the list is what ModsConfig.xml holds.
            _manager.Initialize(new[] { mod1 }, new[] { "mod1" });

            raised.Should().NotBeNull();
            raised!.ActiveListModified.Should().BeFalse();
        }

        [Fact]
        public void Initialize_ReportsTheListAsModified_WhenTheCallerSaysSo()
        {
            var mod1 = new ModItem { Name = "Mod 1", PackageId = "mod1" };

            ModListChangedEventArgs? raised = null;
            _manager.ListChanged += (_, e) => raised = e;

            // An import: the list is not what is on disk, so the user still
            // has to save it.
            _manager.Initialize(new[] { mod1 }, new[] { "mod1" }, activeListModified: true);

            raised.Should().NotBeNull();
            raised!.ActiveListModified.Should().BeTrue();
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
        public void ResolveDependencies_WhenUrlIsSteamProtocol_ShouldPreserveUrlAndKeepDeclaredName()
        {

            var mod = new ModItem 
            { 
                Name = "Mod With Bad Dep", 
                PackageId = "mod.bad", 
                ModDependencies = new List<ModDependency> 
                { 
                    new ModDependency { PackageId = "missing.dep", DisplayName = "Declared Name", SteamWorkshopUrl = "steam://url/CommunityFilePage/12345" }
                } 
            };
            _manager.Initialize(new[] { mod }, new[] { "mod.bad" });

            var result = _manager.ResolveDependencies();

            result.missingDependencies.Should().HaveCount(1);
            var missing = result.missingDependencies[0];
            missing.packageId.Should().Be("missing.dep");
            // The raw steam:// URL is preserved so the declared pfid can drive
            // live-API fallback and auto-selection downstream. It is treated as
            // "no usable URL" there and resolved via candidates.
            missing.steamUrl.Should().Be("steam://url/CommunityFilePage/12345");
            missing.displayName.Should().Be("Declared Name");
        }

        [Fact]
        public void ResolveDependencies_WhenDependencyHasNoUrl_ShouldPassThroughBlankUrlAndKeepDeclaredName()
        {

            var mod = new ModItem
            {
                Name = "Mod With Dep",
                PackageId = "mod.a",
                ModDependencies = new List<ModDependency>
                {
                    new ModDependency { PackageId = "missing.dep", DisplayName = "Declared Name" }
                }
            };
            _manager.Initialize(new[] { mod }, new[] { "mod.a" });

            var result = _manager.ResolveDependencies();

            result.missingDependencies.Should().HaveCount(1);
            var missing = result.missingDependencies[0];
            missing.packageId.Should().Be("missing.dep");
            // Left blank so ResolveMissingDependencyUrlsAsync resolves ALL candidates.
            missing.steamUrl.Should().BeEmpty();
            missing.displayName.Should().Be("Declared Name");
        }

        [Fact]
        public void ResolveDependencies_WhenDependencyHasNoUrlOrName_ShouldFallBackToPackageId()
        {

            var mod = new ModItem
            {
                Name = "Mod With Dep",
                PackageId = "mod.a",
                ModDependencies = new List<ModDependency>
                {
                    new ModDependency { PackageId = "Missing.Dep" }
                }
            };
            _manager.Initialize(new[] { mod }, new[] { "mod.a" });

            var result = _manager.ResolveDependencies();

            result.missingDependencies.Should().HaveCount(1);
            var missing = result.missingDependencies[0];
            missing.steamUrl.Should().BeEmpty();
            missing.displayName.Should().Be("missing.dep");
        }

        [Fact]
        public void ResolveDependencies_WhenDependencyHasRealWorkshopUrl_ShouldKeepUrlUnchanged()
        {
            const string url = "https://steamcommunity.com/workshop/filedetails/?id=12345";

            var mod = new ModItem
            {
                Name = "Mod With Dep",
                PackageId = "mod.a",
                ModDependencies = new List<ModDependency>
                {
                    new ModDependency { PackageId = "missing.dep", DisplayName = "Declared Name", SteamWorkshopUrl = url }
                }
            };
            _manager.Initialize(new[] { mod }, new[] { "mod.a" });

            var result = _manager.ResolveDependencies();

            result.missingDependencies.Should().HaveCount(1);
            var missing = result.missingDependencies[0];
            missing.packageId.Should().Be("missing.dep");
            // Real workshop URLs stay untouched (single-variant, existing behavior).
            missing.steamUrl.Should().Be(url);
            missing.displayName.Should().Be("Declared Name");
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


