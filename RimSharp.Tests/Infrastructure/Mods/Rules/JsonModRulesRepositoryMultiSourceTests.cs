using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Mods.Rules
{
    /// <summary>
    /// Composite repository tests: multi-source merge policies, dual-key
    /// incompatibility normalization, loadTop parsing and the upstream
    /// community fixture.
    /// </summary>
    public class JsonModRulesRepositoryMultiSourceTests : IDisposable
    {
        private readonly string _dataRoot;
        private readonly string _configPath;
        private readonly IDataUpdateService _dataUpdateService;
        private readonly ModRuleSourceRegistry _registry;

        public JsonModRulesRepositoryMultiSourceTests()
        {
            _dataRoot = Path.Combine(Path.GetTempPath(), "RimSharp_CompositeRules_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dataRoot);

            _configPath = Path.Combine(_dataRoot, "config.cfg");
            var configService = new ConfigService(_configPath);
            _registry = new ModRuleSourceRegistry(configService);

            _dataUpdateService = Substitute.For<IDataUpdateService>();
            _dataUpdateService
                .GetExternalFilePath(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(ci => Path.Combine(
                    _dataRoot,
                    ci.ArgAt<string>(0),
                    ci.ArgAt<string>(1),
                    ci.ArgAt<string>(2)));
        }

        public void Dispose()
        {
            if (Directory.Exists(_dataRoot))
            {
                Directory.Delete(_dataRoot, recursive: true);
            }
        }

        private void WriteSourceFile(string folder, string fileName, string json)
        {
            var path = Path.Combine(_dataRoot, "Rules", folder, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }

        private JsonModRulesRepository CreateRepository() =>
            new(_dataUpdateService, _registry);

        [Fact]
        public void GetAllRules_ShouldMergeSourcesInPriorityOrder()
        {
            // Curated (higher priority) and community (lower priority) both
            // carry mod.a; the merge policies decide the effective rule.
            WriteSourceFile("RimSharp", "rules.json", @"{
              ""timestamp"": 100,
              ""rules"": {
                ""mod.a"": {
                  ""loadBefore"": { ""mod.curated"": { ""name"": [""Curated Target""] } },
                  ""loadBottom"": { ""value"": true, ""comment"": [""curated bottom""] },
                  ""incompatibilities"": { ""mod.bad"": { ""hardIncompatibility"": true, ""comment"": [""curated says no""] } },
                  ""supportedVersions"": [""1.5""]
                }
              }
            }");
            WriteSourceFile("community-rules", "communityRules.json", @"{
              ""timestamp"": 500,
              ""rules"": {
                ""mod.a"": {
                  ""loadBefore"": { ""mod.curated"": { ""name"": [""Community Target""] }, ""mod.community"": { ""name"": [""Community Only""] } },
                  ""loadTop"": { ""value"": true, ""comment"": [""tier 1 please""] },
                  ""incompatibleWith"": { ""mod.bad"": { ""name"": ""Community Name"", ""comment"": ""community says no"" }, ""mod.other"": { ""name"": ""Other"", ""comment"": ""also no"" } },
                  ""supportedVersions"": [""1.5"", ""1.6""]
                },
                ""mod.b"": { ""loadAfter"": { ""mod.a"": { ""name"": [""A""] } } }
              }
            }");

            var repo = CreateRepository();
            var rules = repo.GetAllRules();

            rules.Should().HaveCount(2);

            var modA = rules["mod.a"];
            // loadBefore: target-key union; per-target Name/Comment from the
            // HIGHEST-priority source carrying that target.
            modA.LoadBefore.Should().ContainKeys("mod.curated", "mod.community");
            modA.LoadBefore["mod.curated"].Name.Should().Contain("Curated Target");
            modA.LoadBefore["mod.community"].Name.Should().Contain("Community Only");

            // loadTop/loadBottom: OR across sources.
            modA.LoadTop.Should().NotBeNull();
            modA.LoadTop!.Value.Should().BeTrue();
            modA.LoadBottom!.Value.Should().BeTrue();

            // incompatibleWith: first-source-wins per target; the community-only
            // target is still added.
            modA.Incompatibilities.Should().ContainKeys("mod.bad", "mod.other");
            modA.Incompatibilities["mod.bad"].HardIncompatibility.Should().BeTrue("the curated entry's explicit flag is honored");
            modA.Incompatibilities["mod.bad"].Comment.Should().Contain("curated says no");
            modA.Incompatibilities["mod.other"].HardIncompatibility.Should().BeFalse("database-layer entries default to soft/advisory");

            // supportedVersions: NOT merged (version suppression left the rules
            // schema) — the claiming (highest-priority) source's value stays.
            modA.SupportedVersions.Should().BeEquivalentTo(new[] { "1.5" });

            // A packageId only the lower-priority source carries still lands.
            rules["mod.b"].LoadAfter.Should().ContainKey("mod.a");
        }

        [Fact]
        public void GetAllRules_ShouldSkipDisabledSources()
        {
            WriteSourceFile("RimSharp", "rules.json", @"{ ""rules"": { ""mod.a"": {} } }");
            WriteSourceFile("community-rules", "communityRules.json", @"{ ""rules"": { ""mod.b"": {} } }");

            _registry.SetEnabled(ModRuleSourceRegistry.CommunitySourceId, false);

            var rules = CreateRepository().GetAllRules();

            rules.Should().ContainKey("mod.a");
            rules.Should().NotContainKey("mod.b");
        }

        [Fact]
        public void GetAllRules_ShouldIsolateBrokenSourceFiles()
        {
            WriteSourceFile("RimSharp", "rules.json", "{ this is not valid json }");
            WriteSourceFile("community-rules", "communityRules.json", @"{ ""rules"": { ""mod.b"": {} } }");

            var rules = CreateRepository().GetAllRules();

            rules.Should().ContainKey("mod.b");
        }

        [Fact]
        public void GetAllRules_WhenNoSourceFileExists_ShouldReturnEmpty()
        {
            var rules = CreateRepository().GetAllRules();

            rules.Should().BeEmpty();
        }

        [Fact]
        public void GetAllRules_ShouldBindBothIncompatibilityKeys()
        {
            WriteSourceFile("RimSharp", "rules.json", @"{
              ""rules"": {
                ""mod.curated"": { ""incompatibilities"": { ""mod.x"": { ""comment"": [""canonical key""] } } },
                ""mod.community"": { ""incompatibleWith"": { ""mod.y"": { ""name"": ""Y"", ""comment"": ""alias key"" } } },
                ""mod.both"": { ""incompatibleWith"": { ""mod.alias"": {} }, ""incompatibilities"": { ""mod.canonical"": {} } }
              }
            }");

            var rules = CreateRepository().GetAllRules();

            rules["mod.curated"].Incompatibilities.Should().ContainKey("mod.x");
            rules["mod.community"].Incompatibilities.Should().ContainKey("mod.y");
            rules["mod.community"].Incompatibilities["mod.y"].Name.Should().Contain("Y");
            // Both keys present: the canonical one wins, the alias is ignored.
            rules["mod.both"].Incompatibilities.Should().ContainKey("mod.canonical");
            rules["mod.both"].Incompatibilities.Should().NotContainKey("mod.alias");
        }

        [Fact]
        public void GetAllRules_ShouldParseLoadTop()
        {
            WriteSourceFile("community-rules", "communityRules.json", @"{
              ""rules"": { ""imranfish.xmlextensions"": { ""loadTop"": { ""value"": true, ""comment"": ""Should be Treated as Tier-1 mod"" } } }
            }");

            var rules = CreateRepository().GetAllRules();

            var rule = rules["imranfish.xmlextensions"];
            rule.LoadTop.Should().NotBeNull();
            rule.LoadTop!.Value.Should().BeTrue();
            rule.LoadTop.Comment.Should().Contain("Should be Treated as Tier-1 mod");
        }

        [Fact]
        public void GetAllRules_ShouldParseTheUpstreamCommunityFixture()
        {
            var fixture = Path.Combine(AppContext.BaseDirectory, "communityRules.json");
            File.Exists(fixture).Should().BeTrue("the vendored upstream fixture must ship with the tests");

            WriteSourceFile("community-rules", "communityRules.json", File.ReadAllText(fixture));

            var rules = CreateRepository().GetAllRules();

            rules.Should().HaveCount(631);

            // loadTop: exactly one entry upstream.
            var loadTopRules = rules.Where(r => r.Value.LoadTop?.Value == true).ToList();
            loadTopRules.Should().ContainSingle();
            loadTopRules[0].Key.Should().Be("imranfish.xmlextensions");

            // incompatibleWith: five entries, all soft (upstream carries no
            // hardness flag).
            var withIncompatibilities = rules.Where(r => r.Value.Incompatibilities.Count > 0).ToList();
            withIncompatibilities.Should().HaveCount(5);
            withIncompatibilities.Select(r => r.Key).Should().BeEquivalentTo(new[]
            {
                "ferny.betterarchitect",
                "shilica.smallerplanet",
                "oblitus.mylittleplanet",
                "usagirei.lootgoblin",
                "ogre.ogrestack"
            });
            withIncompatibilities.Should().OnlyContain(r => r.Value.Incompatibilities.Values.All(i => !i.HardIncompatibility));

            // Name lists: the first element is the display name.
            var xmlExtensions = rules["imranfish.xmlextensions"];
            xmlExtensions.LoadBefore["unlimitedhugs.hugslib"].Name.First().Should().Be("HugsLib");
            xmlExtensions.LoadAfter["ludeon.rimworld.royalty"].Name.First().Should().Be("RimWorld - Royalty");

            // loadBottom entries parse with their comments.
            rules.Values.Count(r => r.LoadBottom?.Value == true).Should().BeGreaterThan(0);
        }

        [Fact]
        public void EffectiveTimestamp_ShouldBeMaxAcrossSources()
        {
            WriteSourceFile("RimSharp", "rules.json", @"{ ""rules"": { ""mod.a"": {} } }"); // no timestamp
            WriteSourceFile("community-rules", "communityRules.json", @"{ ""timestamp"": 1789239946, ""rules"": { ""mod.b"": {} } }");

            var repo = CreateRepository();

            repo.EffectiveTimestamp.Should().Be(1789239946);
        }

        [Fact]
        public void EffectiveTimestamp_WhenNoSourceHasOne_ShouldBeNull()
        {
            WriteSourceFile("RimSharp", "rules.json", @"{ ""rules"": { ""mod.a"": {} } }");

            CreateRepository().EffectiveTimestamp.Should().BeNull();
        }

        [Fact]
        public void Invalidate_ShouldReReadTheSourceFiles()
        {
            WriteSourceFile("RimSharp", "rules.json", @"{ ""rules"": { ""mod.a"": {} } }");

            var repo = CreateRepository();
            var first = repo.GetAllRules();
            first.Should().ContainKey("mod.a");

            File.WriteAllText(
                Path.Combine(_dataRoot, "Rules", "RimSharp", "rules.json"),
                @"{ ""rules"": { ""mod.b"": {} } }");

            // Cached: the change is not visible yet.
            repo.GetAllRules().Should().ContainKey("mod.a");

            repo.Invalidate();

            var second = repo.GetAllRules();
            second.Should().NotContainKey("mod.a");
            second.Should().ContainKey("mod.b");
        }

        [Fact]
        public void GetAllRules_ShouldCacheResults_OnSecondCall()
        {
            WriteSourceFile("RimSharp", "rules.json", @"{ ""rules"": { ""mod.a"": {} } }");

            var repo = CreateRepository();

            var first = repo.GetAllRules();
            var second = repo.GetAllRules();

            second.Should().BeSameAs(first);
        }

        [Fact]
        public void GetRulesForMod_IsCaseInsensitive()
        {
            WriteSourceFile("RimSharp", "rules.json", @"{ ""rules"": { ""Mod.A"": { ""loadBefore"": { ""mod.b"": {} } } } }");

            var rule = CreateRepository().GetRulesForMod("mod.a");

            rule.LoadBefore.Should().ContainKey("mod.b");
        }

        [Fact]
        public void GetAllRuleProvenance_ShouldRecordFirstSourceWinsPerTarget()
        {
            // The curated source (higher priority) and the community source
            // both carry mod.a. Every shared target must be claimed by the
            // FIRST source carrying it; targets only one source carries are
            // claimed by that source.
            WriteSourceFile("RimSharp", "rules.json", @"{
              ""rules"": {
                ""mod.a"": {
                  ""loadBefore"": { ""mod.shared"": {}, ""mod.curatedOnly"": {} },
                  ""loadAfter"": { ""mod.afterShared"": {} },
                  ""incompatibilities"": { ""mod.bad"": {} }
                }
              }
            }");
            WriteSourceFile("community-rules", "communityRules.json", @"{
              ""rules"": {
                ""mod.a"": {
                  ""loadBefore"": { ""mod.shared"": {}, ""mod.communityOnly"": {} },
                  ""loadAfter"": { ""mod.afterShared"": {}, ""mod.afterCommunity"": {} },
                  ""incompatibleWith"": { ""mod.bad"": {}, ""mod.otherBad"": {} }
                }
              }
            }");

            var provenance = CreateRepository().GetAllRuleProvenance();

            provenance.Should().ContainKey("mod.a");
            var claims = provenance["mod.a"];

            // loadBefore: shared target claimed by the first (curated) source.
            claims.LoadBeforeSource["mod.shared"].Should().Be(ModRuleSourceRegistry.CuratedSourceId);
            claims.LoadBeforeSource["mod.curatedOnly"].Should().Be(ModRuleSourceRegistry.CuratedSourceId);
            claims.LoadBeforeSource["mod.communityOnly"].Should().Be(ModRuleSourceRegistry.CommunitySourceId);

            // loadAfter: same first-source-wins policy.
            claims.LoadAfterSource["mod.afterShared"].Should().Be(ModRuleSourceRegistry.CuratedSourceId);
            claims.LoadAfterSource["mod.afterCommunity"].Should().Be(ModRuleSourceRegistry.CommunitySourceId);

            // incompatibilities: first-source-wins per target (both keys bind).
            claims.IncompatibleSource["mod.bad"].Should().Be(ModRuleSourceRegistry.CuratedSourceId);
            claims.IncompatibleSource["mod.otherBad"].Should().Be(ModRuleSourceRegistry.CommunitySourceId);
        }

        [Fact]
        public void GetAllRuleProvenance_ShouldRecordClaimingSourceForFlags()
        {
            // Flags merge by OR; the FIRST source providing a flag claims it.
            // Here both sources provide both flags, so the curated source wins
            // both claims.
            WriteSourceFile("RimSharp", "rules.json", @"{
              ""rules"": {
                ""mod.both"": {
                  ""loadTop"": { ""value"": true },
                  ""loadBottom"": { ""value"": true }
                }
              }
            }");
            // mod.split is carried only by the community source (the curated
            // source does not carry that packageId at all).
            WriteSourceFile("community-rules", "communityRules.json", @"{
              ""rules"": {
                ""mod.both"": {
                  ""loadTop"": { ""value"": true },
                  ""loadBottom"": { ""value"": true }
                },
                ""mod.split"": {
                  ""loadTop"": { ""value"": true },
                  ""loadBottom"": { ""value"": true }
                }
              }
            }");

            var provenance = CreateRepository().GetAllRuleProvenance();

            var both = provenance["mod.both"];
            both.LoadTopSource.Should().Be(ModRuleSourceRegistry.CuratedSourceId);
            both.LoadBottomSource.Should().Be(ModRuleSourceRegistry.CuratedSourceId);

            // A packageId only the lower-priority source carries: that source
            // claims its flags via ClaimAll.
            var split = provenance["mod.split"];
            split.LoadTopSource.Should().Be(ModRuleSourceRegistry.CommunitySourceId);
            split.LoadBottomSource.Should().Be(ModRuleSourceRegistry.CommunitySourceId);
        }

        [Fact]
        public void GetAllRuleProvenance_WhenFlagComesFromLowerPrioritySource_ShouldClaimThatSource()
        {
            // The first source carries the packageId but NOT the flag; the
            // second source provides the flag, so it claims it (OR merge).
            WriteSourceFile("RimSharp", "rules.json", @"{
              ""rules"": { ""mod.a"": { ""loadBefore"": { ""mod.x"": {} } } }
            }");
            WriteSourceFile("community-rules", "communityRules.json", @"{
              ""rules"": { ""mod.a"": { ""loadTop"": { ""value"": true } } }
            }");

            var provenance = CreateRepository().GetAllRuleProvenance();

            var claims = provenance["mod.a"];
            claims.LoadTopSource.Should().Be(ModRuleSourceRegistry.CommunitySourceId);
            claims.LoadBottomSource.Should().BeNull();
            claims.LoadBeforeSource["mod.x"].Should().Be(ModRuleSourceRegistry.CuratedSourceId);
        }

        [Fact]
        public void GetAllRuleProvenance_IsCaseInsensitiveByPackageId()
        {
            WriteSourceFile("RimSharp", "rules.json", @"{ ""rules"": { ""Mod.A"": { ""loadBefore"": { ""mod.x"": {} } } } }");

            var provenance = CreateRepository().GetAllRuleProvenance();

            provenance["mod.a"].LoadBeforeSource["mod.x"].Should().Be(ModRuleSourceRegistry.CuratedSourceId);
        }

        [Fact]
        public void Invalidate_ShouldRefreshProvenance()
        {
            WriteSourceFile("RimSharp", "rules.json", @"{
              ""rules"": { ""mod.a"": { ""loadBefore"": { ""mod.first"": {} } } }
            }");

            var repo = CreateRepository();
            repo.GetAllRuleProvenance()["mod.a"].LoadBeforeSource["mod.first"]
                .Should().Be(ModRuleSourceRegistry.CuratedSourceId);

            // Rewrite the source file: the cached provenance must not change.
            File.WriteAllText(
                Path.Combine(_dataRoot, "Rules", "RimSharp", "rules.json"),
                @"{ ""rules"": { ""mod.a"": { ""loadBefore"": { ""mod.second"": {} } } } }");

            var cached = repo.GetAllRuleProvenance()["mod.a"];
            cached.LoadBeforeSource.Should().ContainKey("mod.first");
            cached.LoadBeforeSource.Should().NotContainKey("mod.second");

            repo.Invalidate();

            var refreshed = repo.GetAllRuleProvenance()["mod.a"];
            refreshed.LoadBeforeSource.Should().ContainKey("mod.second");
            refreshed.LoadBeforeSource.Should().NotContainKey("mod.first");
            refreshed.LoadBeforeSource["mod.second"].Should().Be(ModRuleSourceRegistry.CuratedSourceId);
        }
    }
}