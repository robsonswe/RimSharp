using System;
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
    public class ModRuleSourceRegistryTests : IDisposable
    {
        private readonly string _configPath;
        private readonly IConfigService _configService;
        private readonly ModRuleSourceRegistry _registry;

        public ModRuleSourceRegistryTests()
        {
            _configPath = Path.Combine(Path.GetTempPath(), "RimSharp_RegistryTest_" + Guid.NewGuid().ToString("N") + ".cfg");
            _configService = new ConfigService(_configPath);
            _registry = new ModRuleSourceRegistry(_configService);
        }

        public void Dispose()
        {
            if (File.Exists(_configPath))
            {
                File.Delete(_configPath);
            }
        }

        [Fact]
        public void Constructor_WhenKeyAbsent_ShouldUseCompiledDefaults()
        {
            var defaults = _registry.Sources;

            defaults.Should().HaveCount(2);
            defaults[0].Id.Should().Be(ModRuleSourceRegistry.CuratedSourceId);
            defaults[1].Id.Should().Be(ModRuleSourceRegistry.CommunitySourceId);
            defaults[1].Url.Should().Be(ModRuleSourceRegistry.CommunitySourceUrl);
            defaults[1].Type.Should().Be(ModRuleSourceType.RepoFile);
            defaults.Should().OnlyContain(s => s.Enabled && !s.Bundled);
        }

        [Fact]
        public void Save_ShouldRoundTripThroughConfig()
        {
            _registry.Add(new ModRuleSourceRecord
            {
                Id = "my-source",
                Name = "My Source",
                Type = ModRuleSourceType.LocalFile,
                LocalPath = @"D:\rules\my.json",
                Enabled = false
            });
            _registry.Save();

            var reloaded = new ModRuleSourceRegistry(_configService);

            reloaded.Sources.Should().HaveCount(3);
            var added = reloaded.Find("my-source");
            added.Should().NotBeNull();
            added!.Type.Should().Be(ModRuleSourceType.LocalFile);
            added.LocalPath.Should().Be(@"D:\rules\my.json");
            added.Enabled.Should().BeFalse();
        }

        [Fact]
        public void Constructor_WhenKeyMalformed_ShouldFallBackToDefaults()
        {
            _configService.SetConfigValue(ModRuleSourceRegistry.ConfigKey, "{ this is not json ");
            _configService.SaveConfig();

            var registry = new ModRuleSourceRegistry(_configService);

            registry.Sources.Should().HaveCount(2);
            registry.Sources[0].Id.Should().Be(ModRuleSourceRegistry.CuratedSourceId);
        }

        [Fact]
        public void Constructor_WhenKeyIsJsonNull_ShouldFallBackToDefaults()
        {
            _configService.SetConfigValue(ModRuleSourceRegistry.ConfigKey, "null");
            _configService.SaveConfig();

            var registry = new ModRuleSourceRegistry(_configService);

            registry.Sources.Should().HaveCount(2);
        }

        [Fact]
        public void Constructor_WhenEntriesHaveNoId_ShouldDropThem()
        {
            _configService.SetConfigValue(
                ModRuleSourceRegistry.ConfigKey,
                "[{\"name\":\"no id\"},{\"id\":\"ok\",\"name\":\"OK\"}]");
            _configService.SaveConfig();

            var registry = new ModRuleSourceRegistry(_configService);

            registry.Sources.Should().ContainSingle();
            registry.Sources[0].Id.Should().Be("ok");
        }

        [Fact]
        public void MoveUpAndMoveDown_ShouldReorderPriority()
        {
            _registry.MoveUp(ModRuleSourceRegistry.CommunitySourceId);

            _registry.Sources[0].Id.Should().Be(ModRuleSourceRegistry.CommunitySourceId);
            _registry.Sources[1].Id.Should().Be(ModRuleSourceRegistry.CuratedSourceId);

            _registry.MoveDown(ModRuleSourceRegistry.CommunitySourceId);

            _registry.Sources[0].Id.Should().Be(ModRuleSourceRegistry.CuratedSourceId);
        }

        [Fact]
        public void MoveUp_WhenAlreadyFirst_ShouldReturnFalse()
        {
            _registry.MoveUp(ModRuleSourceRegistry.CuratedSourceId).Should().BeFalse();
        }

        [Fact]
        public void SetEnabled_ShouldFilterGetEnabledSources()
        {
            _registry.SetEnabled(ModRuleSourceRegistry.CommunitySourceId, false);

            _registry.GetEnabledSources().Should().ContainSingle();
            _registry.GetEnabledSources()[0].Id.Should().Be(ModRuleSourceRegistry.CuratedSourceId);
        }

        [Fact]
        public void Add_WhenIdAlreadyExists_ShouldReturnFalse()
        {
            _registry.Add(new ModRuleSourceRecord { Id = ModRuleSourceRegistry.CuratedSourceId })
                .Should().BeFalse();
        }

        [Fact]
        public void Remove_WhenUnknown_ShouldReturnFalse()
        {
            _registry.Remove("does-not-exist").Should().BeFalse();
        }

        [Fact]
        public void ResetToDefaults_ShouldRestoreCompiledDefaults()
        {
            _registry.Remove(ModRuleSourceRegistry.CommunitySourceId);
            _registry.Add(new ModRuleSourceRecord { Id = "extra" });

            _registry.ResetToDefaults();

            _registry.Sources.Select(s => s.Id).Should().Equal(
                ModRuleSourceRegistry.CuratedSourceId,
                ModRuleSourceRegistry.CommunitySourceId);
        }

        [Fact]
        public void ReplaceAll_ShouldReplaceTheWholeList()
        {
            _registry.ReplaceAll(new[]
            {
                new ModRuleSourceRecord { Id = "only" }
            });

            _registry.Sources.Should().ContainSingle();
            _registry.Sources[0].Id.Should().Be("only");
        }

        [Fact]
        public void SerializedValue_ShouldBeSingleLineJson()
        {
            _registry.Save();

            var raw = _configService.GetConfigValue(ModRuleSourceRegistry.ConfigKey);
            raw.Should().NotBeNullOrWhiteSpace();
            raw.Should().NotContain("\n");
            raw.Should().Contain("community-rules");
            raw.Should().Contain("repoFile");
        }
    }
}