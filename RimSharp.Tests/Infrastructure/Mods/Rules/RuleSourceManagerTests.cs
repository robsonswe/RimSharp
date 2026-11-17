using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Mods.Rules
{
    /// <summary>
    /// Live re-merge proof: a registry change (disable/remove/reorder) or a
    /// source refresh invalidates the merged rules cache and re-applies the
    /// rules onto the loaded mods, so a removed/disabled source's
    /// contributions actually disappear — and come back when re-enabled.
    /// </summary>
    public class RuleSourceManagerTests : IDisposable
    {
        private readonly string _dataRoot;
        private readonly string _configPath;
        private readonly ModRuleSourceRegistry _registry;
        private readonly IDataUpdateService _mockDataUpdateService;
        private readonly JsonModRulesRepository _repository;
        private readonly ModRulesService _rulesService;
        private readonly IModService _mockModService;
        private readonly IDataUpdateService _mockSyncService;
        private readonly ILoggerService _mockLogger;
        private readonly RuleSourceManager _manager;

        public RuleSourceManagerTests()
        {
            _dataRoot = Path.Combine(Path.GetTempPath(), "RimSharp_RuleSourceManager_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dataRoot);

            _configPath = Path.Combine(_dataRoot, "config.cfg");
            var configService = new ConfigService(_configPath);
            _registry = new ModRuleSourceRegistry(configService);

            _mockDataUpdateService = Substitute.For<IDataUpdateService>();
            _mockDataUpdateService
                .GetExternalFilePath(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(ci => Path.Combine(
                    _dataRoot,
                    ci.ArgAt<string>(0),
                    ci.ArgAt<string>(1),
                    ci.ArgAt<string>(2)));

            _repository = new JsonModRulesRepository(_mockDataUpdateService, _registry);
            _rulesService = new ModRulesService(_repository);
            _mockModService = Substitute.For<IModService>();
            _mockSyncService = Substitute.For<IDataUpdateService>();
            _mockLogger = Substitute.For<ILoggerService>();

            _manager = new RuleSourceManager(
                _registry, _repository, _mockModService, _mockSyncService, _mockLogger);

            WriteSourceFile("RimSharp", "rules.json", @"{
              ""rules"": {
                ""mod.a"": { ""loadBefore"": { ""mod.curated"": { ""name"": [""Curated""] } } },
                ""mod.b"": { ""loadBottom"": { ""value"": true } }
              }
            }");
            WriteSourceFile("community-rules", "communityRules.json", @"{
              ""timestamp"": 1789239946,
              ""rules"": {
                ""mod.a"": {
                  ""loadBefore"": { ""mod.curated"": { ""name"": [""Community""] } },
                  ""loadAfter"": { ""mod.community"": { ""name"": [""Community""] } }
                },
                ""mod.c"": { ""loadTop"": { ""value"": true } }
              }
            }");
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

        private List<ModItem> ApplyOnce()
        {
            var mods = new List<ModItem>
            {
                new() { PackageId = "mod.a" },
                new() { PackageId = "mod.b" },
                new() { PackageId = "mod.c" }
            };
            _rulesService.ApplyRulesToMods(mods);
            return mods;
        }

        [Fact]
        public void CommitRegistryChanges_ShouldPersistInvalidateAndReapply()
        {
            _registry.SetEnabled(ModRuleSourceRegistry.CommunitySourceId, false);

            _manager.CommitRegistryChanges();

            // Persisted: a fresh registry instance reads the change back.
            var reloaded = new ModRuleSourceRegistry(new ConfigService(_configPath));
            reloaded.Find(ModRuleSourceRegistry.CommunitySourceId)!.Enabled.Should().BeFalse();

            // The merged cache was invalidated and the re-merge was triggered.
            _mockModService.Received(1).ReapplyRulesAsync();
        }

        [Fact]
        public void DisablingASource_ShouldRemoveItsContributionsFromMods()
        {
            var mods = ApplyOnce();

            // Both sources contributed.
            mods[0].LoadBefore.Should().Contain("mod.curated");
            mods[0].LoadAfter.Should().Contain("mod.community");
            mods[1].LoadBottom.Should().BeTrue();
            mods[2].LoadTop.Should().BeTrue();

            // Disable the community source and run the live re-merge path.
            _registry.SetEnabled(ModRuleSourceRegistry.CommunitySourceId, false);
            _manager.CommitRegistryChanges();
            _rulesService.RefreshRules();
            _rulesService.ApplyRulesToMods(mods);

            mods[0].LoadBefore.Should().Contain("mod.curated", "the curated source stays enabled");
            mods[0].LoadAfter.Should().NotContain("mod.community", "the disabled source's contribution must disappear");
            mods[2].LoadTop.Should().BeFalse("the disabled source's loadTop must disappear");

            // Re-enable: the contributions come back.
            _registry.SetEnabled(ModRuleSourceRegistry.CommunitySourceId, true);
            _manager.CommitRegistryChanges();
            _rulesService.RefreshRules();
            _rulesService.ApplyRulesToMods(mods);

            mods[0].LoadAfter.Should().Contain("mod.community");
            mods[2].LoadTop.Should().BeTrue();
        }

        [Fact]
        public void RemovingASource_ShouldRemoveItsContributionsFromMods()
        {
            var mods = ApplyOnce();
            mods[0].LoadAfter.Should().Contain("mod.community");

            _registry.Remove(ModRuleSourceRegistry.CommunitySourceId);
            _manager.CommitRegistryChanges();
            _rulesService.RefreshRules();
            _rulesService.ApplyRulesToMods(mods);

            mods[0].LoadAfter.Should().NotContain("mod.community");
        }

        [Fact]
        public void ReorderingSources_ShouldChangePerTargetProvenance()
        {
            // Default order: the curated source claims the shared target.
            var rule = _rulesService.GetRulesForMod("mod.a");
            rule.LoadBefore["mod.curated"].Name.Should().Contain("Curated");

            // Community first: its Name/Comment claim the shared target.
            _registry.MoveUp(ModRuleSourceRegistry.CommunitySourceId);
            _manager.CommitRegistryChanges();
            _rulesService.RefreshRules();

            rule = _rulesService.GetRulesForMod("mod.a");
            rule.LoadBefore["mod.curated"].Name.Should().Contain("Community");
        }

        [Fact]
        public async Task RefreshAllAsync_ShouldSyncThenInvalidateAndReapply()
        {
            await _manager.RefreshAllAsync(TestContext.Current.CancellationToken);

            await _mockSyncService.Received(1).RefreshAllRuleSourcesAsync(Arg.Any<System.Threading.CancellationToken>());
            await _mockModService.Received(1).ReapplyRulesAsync();
        }

        [Fact]
        public async Task RefreshSourceAsync_ShouldSyncOnlyThatSourceThenReapply()
        {
            await _manager.RefreshSourceAsync(ModRuleSourceRegistry.CommunitySourceId, TestContext.Current.CancellationToken);

            await _mockSyncService.Received(1).RefreshRuleSourceAsync(
                ModRuleSourceRegistry.CommunitySourceId, Arg.Any<System.Threading.CancellationToken>());
            await _mockModService.Received(1).ReapplyRulesAsync();
        }

        [Fact]
        public void EffectiveTimestamp_ShouldComeFromTheNewestSource()
        {
            _manager.EffectiveTimestamp.Should().Be(1789239946);
        }
    }
}