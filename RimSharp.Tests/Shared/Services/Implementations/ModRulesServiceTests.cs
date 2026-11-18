using System;
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
            // layer must never produce supported-version entries.
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

        [Fact]
        public void ApplyRulesToMods_ShouldRecordTheClaimingSourceIdPerTarget()
        {
            // Per-target provenance: every recorded Database* entry carries the
            // source Id that claimed it, flags included.
            var mod = new ModItem { PackageId = "mod1" };
            var rule = new ModRule
            {
                LoadBefore = new Dictionary<string, ModDependencyRule> { { "otherMod", new ModDependencyRule() } },
                LoadAfter = new Dictionary<string, ModDependencyRule> { { "baseMod", new ModDependencyRule() } },
                LoadTop = new LoadBottomRule { Value = true },
                LoadBottom = new LoadBottomRule { Value = true },
                Incompatibilities = new Dictionary<string, ModIncompatibilityRule> { { "badMod", new ModIncompatibilityRule() } }
            };
            var provenance = new Dictionary<string, RuleProvenance>(StringComparer.OrdinalIgnoreCase);
            var claims = new RuleProvenance();
            claims.LoadBeforeSource["otherMod"] = "rimsharp-db";
            claims.LoadAfterSource["baseMod"] = "community-rules";
            claims.IncompatibleSource["badMod"] = "community-rules";
            claims.LoadTopSource = "rimsharp-db";
            claims.LoadBottomSource = "community-rules";
            provenance["mod1"] = claims;

            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", rule } });
            _mockRepository.GetAllRuleProvenance().Returns(provenance);

            _service.ApplyRulesToMods(new[] { mod });

            mod.DatabaseLoadBefore.Should().ContainKey("otherMod");
            mod.DatabaseLoadBefore["otherMod"].Should().Be("rimsharp-db");
            mod.DatabaseLoadAfter["baseMod"].Should().Be("community-rules");
            mod.DatabaseIncompatibleWith["badMod"].Should().Be("community-rules");
            mod.DatabaseLoadTop.Should().BeTrue();
            mod.DatabaseLoadTopSource.Should().Be("rimsharp-db");
            mod.DatabaseLoadBottom.Should().BeTrue();
            mod.DatabaseLoadBottomSource.Should().Be("community-rules");
        }

        [Fact]
        public void ApplyRulesToMods_WhenProvenanceHasNoClaim_ShouldRecordANonEmptySourceId()
        {
            // Repositories without merge provenance (legacy bare repositories /
            // test doubles) still produce a non-null, non-empty source id for
            // every recorded contribution - origin labels must never be blank.
            var mod = new ModItem { PackageId = "mod1" };
            var rule = new ModRule
            {
                LoadBefore = new Dictionary<string, ModDependencyRule> { { "otherMod", new ModDependencyRule() } },
                LoadTop = new LoadBottomRule { Value = true },
                LoadBottom = new LoadBottomRule { Value = true },
                Incompatibilities = new Dictionary<string, ModIncompatibilityRule> { { "badMod", new ModIncompatibilityRule() } }
            };
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", rule } });
            // GetAllRuleProvenance() intentionally left unconfigured.

            _service.ApplyRulesToMods(new[] { mod });

            mod.DatabaseLoadBefore["otherMod"].Should().Be(ModRulesService.UnknownSourceId);
            mod.DatabaseIncompatibleWith["badMod"].Should().Be(ModRulesService.UnknownSourceId);
            mod.DatabaseLoadTopSource.Should().Be(ModRulesService.UnknownSourceId);
            mod.DatabaseLoadBottomSource.Should().Be(ModRulesService.UnknownSourceId);
        }

        [Fact]
        public void ApplyRulesToMods_MixedOrigins_ShouldSurviveStripAndReapply()
        {
            // Three origins on one mod: About.xml (mod-declared), custom
            // (untracked user edit) and the rules database (source-attributed).
            var mod = new ModItem { PackageId = "mod1" };
            mod.LoadBefore.Add("about.target");
            mod.AboutLoadBefore.Add("about.target");
            mod.IncompatibleWith.Add("about.bad", new ModIncompatibilityRule { HardIncompatibility = true });
            mod.AboutIncompatibleWith.Add("about.bad");
            mod.LoadAfter.Add("custom.target");

            var dbRuleV1 = new ModRule
            {
                LoadBefore = new Dictionary<string, ModDependencyRule> { { "db.target", new ModDependencyRule() } },
                Incompatibilities = new Dictionary<string, ModIncompatibilityRule> { { "db.bad", new ModIncompatibilityRule() } },
                LoadBottom = new LoadBottomRule { Value = true }
            };
            var provenanceV1 = new Dictionary<string, RuleProvenance>(StringComparer.OrdinalIgnoreCase);
            var claimsV1 = new RuleProvenance();
            claimsV1.LoadBeforeSource["db.target"] = "rimsharp-db";
            claimsV1.IncompatibleSource["db.bad"] = "rimsharp-db";
            claimsV1.LoadBottomSource = "rimsharp-db";
            provenanceV1["mod1"] = claimsV1;

            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", dbRuleV1 } });
            _mockRepository.GetAllRuleProvenance().Returns(provenanceV1);

            _service.ApplyRulesToMods(new[] { mod });

            mod.DatabaseLoadBefore["db.target"].Should().Be("rimsharp-db");
            mod.DatabaseIncompatibleWith["db.bad"].Should().Be("rimsharp-db");
            mod.DatabaseLoadBottomSource.Should().Be("rimsharp-db");

            // Live re-merge: the previous source is gone and a different source
            // contributes different entries.
            var dbRuleV2 = new ModRule
            {
                LoadAfter = new Dictionary<string, ModDependencyRule> { { "db2.after", new ModDependencyRule() } },
                LoadTop = new LoadBottomRule { Value = true }
            };
            var provenanceV2 = new Dictionary<string, RuleProvenance>(StringComparer.OrdinalIgnoreCase);
            var claimsV2 = new RuleProvenance();
            claimsV2.LoadAfterSource["db2.after"] = "community-rules";
            claimsV2.LoadTopSource = "community-rules";
            provenanceV2["mod1"] = claimsV2;

            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", dbRuleV2 } });
            _mockRepository.GetAllRuleProvenance().Returns(provenanceV2);
            _service.RefreshRules();
            _service.ApplyRulesToMods(new[] { mod });

            // Stale database contributions - and their source ids - are gone...
            mod.LoadBefore.Should().NotContain("db.target");
            mod.DatabaseLoadBefore.Should().BeEmpty();
            mod.IncompatibleWith.Should().NotContainKey("db.bad");
            mod.DatabaseIncompatibleWith.Should().BeEmpty();
            mod.LoadBottom.Should().BeFalse();
            mod.DatabaseLoadBottom.Should().BeFalse();
            mod.DatabaseLoadBottomSource.Should().BeNull();

            // ...the fresh ones are attributed to their own source...
            mod.LoadAfter.Should().Contain("db2.after");
            mod.DatabaseLoadAfter["db2.after"].Should().Be("community-rules");
            mod.DatabaseLoadTop.Should().BeTrue();
            mod.DatabaseLoadTopSource.Should().Be("community-rules");

            // ...and About.xml + custom contributions kept their own origins.
            mod.LoadBefore.Should().Contain("about.target");
            mod.AboutLoadBefore.Should().BeEquivalentTo(new[] { "about.target" });
            mod.IncompatibleWith.Should().ContainKey("about.bad");
            mod.IncompatibleWith["about.bad"].HardIncompatibility.Should().BeTrue();
            mod.AboutIncompatibleWith.Should().BeEquivalentTo(new[] { "about.bad" });
            mod.LoadAfter.Should().Contain("custom.target");
            mod.DatabaseLoadAfter.Should().NotContainKey("custom.target");
        }
    }
}

