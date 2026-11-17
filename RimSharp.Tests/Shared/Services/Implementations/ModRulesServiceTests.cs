using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;
using Xunit;

namespace RimSharp.Tests.Shared.Services.Implementations
{
    public class ModRulesServiceTests
    {
        private readonly IModRulesRepository _mockRepository;
        private readonly ModRulesService _service;

        public ModRulesServiceTests()
        {
            _mockRepository = Substitute.For<IModRulesRepository>();
            _service = new ModRulesService(_mockRepository);
        }

        [Fact]
        public void ApplyRulesToMods_ShouldNotPopulateSupportedVersionsFromRules()
        {
            // Version suppression moved out of the rules schema: the rules
            // layer must no longer produce VersionSource.Database entries.
            var mod = new ModItem { PackageId = "mod1", SupportedVersions = new List<VersionSupport>() };
            var rule = new ModRule { SupportedVersions = new List<string> { "1.5" } };
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", rule } });

            _service.ApplyRulesToMods(new[] { mod });

            mod.SupportedVersions.Should().BeEmpty();
        }

        [Fact]
        public void ApplyRulesToMods_ShouldBeCaseInsensitive()
        {

            var mod = new ModItem { PackageId = "MOD1" };
            var rule = new ModRule
            {
                LoadBefore = new Dictionary<string, ModDependencyRule> { { "otherMod", new ModDependencyRule() } }
            };
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", rule } });

            _service.ApplyRulesToMods(new[] { mod });

            mod.LoadBefore.Should().Contain("otherMod");
        }

        [Fact]
        public void ApplyRulesToMods_ShouldApplyLoadOrderRules()
        {

            var mod = new ModItem { PackageId = "mod1" };
            var rule = new ModRule 
            { 
                LoadBefore = new Dictionary<string, ModDependencyRule> { { "otherMod", new ModDependencyRule() } },
                LoadAfter = new Dictionary<string, ModDependencyRule> { { "baseMod", new ModDependencyRule() } },
                LoadBottom = new LoadBottomRule { Value = true }
            };
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", rule } });

            _service.ApplyRulesToMods(new[] { mod });

            mod.LoadBefore.Should().Contain("otherMod");
            mod.LoadAfter.Should().Contain("baseMod");
            mod.LoadBottom.Should().BeTrue();
        }

        [Fact]
        public void ApplyRulesToMods_ShouldApplyIncompatibilities()
        {

            var mod = new ModItem { PackageId = "mod1" };
            var incompatibility = new ModIncompatibilityRule { HardIncompatibility = true };
            var rule = new ModRule 
            { 
                Incompatibilities = new Dictionary<string, ModIncompatibilityRule> { { "badMod", incompatibility } }
            };
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", rule } });

            _service.ApplyRulesToMods(new[] { mod });

            mod.IncompatibleWith.Should().ContainKey("badMod");
            mod.IncompatibleWith["badMod"].HardIncompatibility.Should().BeTrue();
        }

        [Fact]
        public void GetRulesForMod_WhenNotFound_ShouldReturnEmptyRule()
        {

            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule>());

            var result = _service.GetRulesForMod("nonexistent");

            result.Should().NotBeNull();
            result.SupportedVersions.Should().BeEmpty();
            result.LoadBefore.Should().BeEmpty();
        }

        [Fact]
        public void ApplyRulesToMods_ShouldApplyLoadTop()
        {
            var mod = new ModItem { PackageId = "mod1" };
            var rule = new ModRule { LoadTop = new LoadBottomRule { Value = true } };
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", rule } });

            _service.ApplyRulesToMods(new[] { mod });

            mod.LoadTop.Should().BeTrue();
        }

        [Fact]
        public void ApplyRulesToMods_ShouldBeIdempotent_WhenAppliedTwice()
        {
            var mod = new ModItem { PackageId = "mod1" };
            var rule = new ModRule
            {
                LoadBefore = new Dictionary<string, ModDependencyRule> { { "otherMod", new ModDependencyRule() } },
                LoadAfter = new Dictionary<string, ModDependencyRule> { { "baseMod", new ModDependencyRule() } },
                LoadTop = new LoadBottomRule { Value = true },
                LoadBottom = new LoadBottomRule { Value = true },
                Incompatibilities = new Dictionary<string, ModIncompatibilityRule> { { "badMod", new ModIncompatibilityRule() } }
            };
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", rule } });

            _service.ApplyRulesToMods(new[] { mod });
            _service.ApplyRulesToMods(new[] { mod });
            _service.ApplyRulesToMods(new[] { mod });

            mod.LoadBefore.Should().ContainSingle().Which.Should().Be("otherMod");
            mod.LoadAfter.Should().ContainSingle().Which.Should().Be("baseMod");
            mod.IncompatibleWith.Should().ContainSingle();
            mod.LoadTop.Should().BeTrue();
            mod.LoadBottom.Should().BeTrue();
        }

        [Fact]
        public void ApplyRulesToMods_ShouldStillStripLegacyDatabaseVersionEntries()
        {
            // The strip path is retained (idempotency of re-application): a
            // legacy VersionSource.Database entry - e.g. written by an older
            // build or seeded by hand - is removed even though the rules layer
            // no longer produces them.
            var mod = new ModItem
            {
                PackageId = "mod1",
                SupportedVersions = new List<VersionSupport>
                {
                    new("1.5", VersionSource.Official),
                    new("1.6", VersionSource.Database, unofficial: true)
                }
            };
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule>());

            _service.ApplyRulesToMods(new[] { mod });

            mod.SupportedVersions.Should().ContainSingle(v => v.Version == "1.5" && v.Source == VersionSource.Official);
        }

        [Fact]
        public void ApplyRulesToMods_WhenSourceRemoved_ShouldStripItsContributions()
        {
            // First application with the source enabled.
            var mod = new ModItem { PackageId = "mod1" };
            var rule = new ModRule
            {
                LoadBefore = new Dictionary<string, ModDependencyRule> { { "otherMod", new ModDependencyRule() } },
                LoadBottom = new LoadBottomRule { Value = true },
                Incompatibilities = new Dictionary<string, ModIncompatibilityRule> { { "badMod", new ModIncompatibilityRule() } }
            };
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", rule } });

            _service.ApplyRulesToMods(new[] { mod });
            mod.LoadBefore.Should().Contain("otherMod");

            // The source is disabled/removed: the effective rules no longer
            // carry the entry. The live re-merge refreshes the cache and
            // re-applies, which must strip the stale contributions.
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule>());
            _service.RefreshRules();
            _service.ApplyRulesToMods(new[] { mod });

            mod.LoadBefore.Should().NotContain("otherMod");
            mod.LoadBottom.Should().BeFalse();
            mod.IncompatibleWith.Should().NotContainKey("badMod");
        }

        [Fact]
        public void ApplyRulesToMods_WhenReEnabled_ShouldRestoreContributions()
        {
            var mod = new ModItem { PackageId = "mod1" };
            var rule = new ModRule
            {
                LoadBefore = new Dictionary<string, ModDependencyRule> { { "otherMod", new ModDependencyRule() } }
            };
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", rule } });

            _service.ApplyRulesToMods(new[] { mod });
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule>());
            _service.RefreshRules();
            _service.ApplyRulesToMods(new[] { mod });
            mod.LoadBefore.Should().NotContain("otherMod");

            // Re-enabled: the same rules come back.
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", rule } });
            _service.RefreshRules();
            _service.ApplyRulesToMods(new[] { mod });

            mod.LoadBefore.Should().Contain("otherMod");
        }

        [Fact]
        public void ApplyRulesToMods_ShouldNotStripAboutXmlContributions()
        {
            // A target that About.xml already contributed is skipped by the
            // rules layer, so it must survive a re-merge untouched.
            var mod = new ModItem { PackageId = "mod1", LoadBefore = new List<string> { "about.mod" } };
            var rule = new ModRule
            {
                LoadBefore = new Dictionary<string, ModDependencyRule> { { "about.mod", new ModDependencyRule() } }
            };
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", rule } });

            _service.ApplyRulesToMods(new[] { mod });
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule>());
            _service.RefreshRules();
            _service.ApplyRulesToMods(new[] { mod });

            mod.LoadBefore.Should().Contain("about.mod");
        }

        [Fact]
        public void RefreshRules_ShouldInvalidateRepositoryAndOwnCache()
        {
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", new ModRule() } });
            _service.GetRules().Should().ContainKey("mod1");

            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod2", new ModRule() } });
            _service.RefreshRules();

            _mockRepository.Received(1).Invalidate();
            _service.GetRules().Should().ContainKey("mod2");
            _service.GetRules().Should().NotContainKey("mod1");
        }
    }
}

