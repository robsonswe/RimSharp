#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Mods.ModInfo;
using RimSharp.Infrastructure.Mods.Replacements;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Infrastructure.Mods.Versions;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Mods
{
    public class ExternalSourceRegistriesTests : IDisposable
    {
        private readonly string _configPath;
        private readonly IConfigService _configService;

        public ExternalSourceRegistriesTests()
        {
            _configPath = Path.Combine(Path.GetTempPath(), "RimSharp_ExtSourceTest_" + Guid.NewGuid().ToString("N") + ".cfg");
            _configService = new ConfigService(_configPath);
        }

        public void Dispose()
        {
            if (File.Exists(_configPath))
            {
                File.Delete(_configPath);
            }
        }

        [Fact]
        public void ModRuleSourceRecord_ModTypeAndLocking_ShouldWorkAsExpected()
        {
            var modRecord = new ModRuleSourceRecord
            {
                Id = "mod-test",
                Name = "Test Mod",
                Type = ModRuleSourceType.Mod,
                ModFolderId = "12345"
            };

            modRecord.Locked.Should().BeTrue();
            modRecord.ModFolderId.Should().Be("12345");

            var repoRecord = new ModRuleSourceRecord
            {
                Id = "repo-test",
                Name = "Repo Test",
                Type = ModRuleSourceType.RepoFile,
                Url = "https://example.com/test.json"
            };
            repoRecord.Locked.Should().BeFalse();

            var curatedRecord = new ModRuleSourceRecord
            {
                Id = ModRuleSourceRegistry.CuratedSourceId,
                Name = "RimSharpDB",
                Type = ModRuleSourceType.RepoFile
            };
            curatedRecord.Curated.Should().BeTrue();
            curatedRecord.Locked.Should().BeTrue();
        }

        [Fact]
        public void ModRuleSourceRecord_JsonRoundtrip_ShouldPreserveModFolderIdAndModType()
        {
            var record = new ModRuleSourceRecord
            {
                Id = "mod-use-this-instead",
                Name = "Use This Instead (Mod)",
                Type = ModRuleSourceType.Mod,
                ModFolderId = "3396308787",
                Enabled = true
            };

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };

            var json = JsonSerializer.Serialize(record, options);
            json.Should().Contain("\"type\":\"mod\"");
            json.Should().Contain("\"modFolderId\":\"3396308787\"");

            var deserialized = JsonSerializer.Deserialize<ModRuleSourceRecord>(json, options);
            deserialized.Should().NotBeNull();
            deserialized!.Id.Should().Be("mod-use-this-instead");
            deserialized.Type.Should().Be(ModRuleSourceType.Mod);
            deserialized.ModFolderId.Should().Be("3396308787");
            deserialized.Locked.Should().BeTrue();
        }

        [Fact]
        public void ModInfoSourceRegistry_DefaultsAndReset_ShouldIncludeCuratedAndSteamWorkshopDb()
        {
            var registry = new ModInfoSourceRegistry(_configService);

            registry.Sources.Should().HaveCount(2);
            var curated = registry.Sources[0];
            curated.Id.Should().Be(ModInfoSourceRegistry.CuratedSourceId);
            curated.Curated.Should().BeTrue();
            curated.Locked.Should().BeTrue();

            var steamDb = registry.Sources[1];
            steamDb.Id.Should().Be(ModInfoSourceRegistry.SteamWorkshopDbSourceId);
            steamDb.Name.Should().Be(ModInfoSourceRegistry.SteamWorkshopDbSourceName);
            steamDb.Url.Should().Be(ModInfoSourceRegistry.SteamWorkshopDbSourceUrl);
            steamDb.Type.Should().Be(ModRuleSourceType.RepoFile);
            steamDb.Enabled.Should().BeTrue();
            steamDb.Locked.Should().BeFalse("the community DB is a removable default, not locked");

            registry.Add(new ModRuleSourceRecord
            {
                Id = "custom-info",
                Name = "Custom Info",
                Type = ModRuleSourceType.RepoFile,
                Url = "https://example.com/db.json"
            });
            registry.Sources.Should().HaveCount(3);

            registry.ResetToDefaults();
            registry.Sources.Should().HaveCount(2);
            registry.Sources[0].Id.Should().Be(ModInfoSourceRegistry.CuratedSourceId);
            registry.Sources[1].Id.Should().Be(ModInfoSourceRegistry.SteamWorkshopDbSourceId);
        }

        [Fact]
        public void ModInfoSourceRegistry_Migration_AppendsSteamWorkshopDbOnce()
        {
            // Legacy persisted list: curated only, no migration flag yet.
            _configService.SetConfigValue(
                ModInfoSourceRegistry.ConfigKey,
                "[{\"id\":\"rimsharp-db\",\"name\":\"RimSharpDB\",\"type\":\"repoFile\",\"enabled\":true}]");
            _configService.SaveConfig();

            var registry = new ModInfoSourceRegistry(_configService);

            registry.Sources.Should().HaveCount(2);
            registry.Sources[0].Id.Should().Be(ModInfoSourceRegistry.CuratedSourceId);
            registry.Sources[1].Id.Should().Be(ModInfoSourceRegistry.SteamWorkshopDbSourceId);
            _configService.GetConfigValue(ModInfoSourceRegistry.SteamWorkshopDbMigrationFlagKey)
                .Should().Be("true");
        }

        [Fact]
        public void ModInfoSourceRegistry_Migration_RemovalPersistsAcrossRestarts()
        {
            _configService.SetConfigValue(
                ModInfoSourceRegistry.ConfigKey,
                "[{\"id\":\"rimsharp-db\",\"name\":\"RimSharpDB\",\"type\":\"repoFile\",\"enabled\":true}]");
            _configService.SaveConfig();

            var first = new ModInfoSourceRegistry(_configService);
            first.Sources.Should().HaveCount(2);

            // The user removes the removable default and persists the change.
            first.Remove(ModInfoSourceRegistry.SteamWorkshopDbSourceId).Should().BeTrue();
            first.Save();

            var second = new ModInfoSourceRegistry(_configService);
            second.Sources.Should().ContainSingle();
            second.Sources[0].Id.Should().Be(ModInfoSourceRegistry.CuratedSourceId);
            second.Find(ModInfoSourceRegistry.SteamWorkshopDbSourceId).Should().BeNull(
                "a one-time migration must not resurrect a source the user removed");
        }

        [Fact]
        public void ModReplacementSourceRegistry_Defaults_ShouldIncludeModRemoteAndRimSharpDb()
        {
            var registry = new ModReplacementSourceRegistry(_configService);

            registry.Sources.Should().HaveCount(3);

            var modSource = registry.Find(ModReplacementSourceRegistry.ModSourceId);
            modSource.Should().NotBeNull();
            modSource!.Type.Should().Be(ModRuleSourceType.Mod);
            modSource.ModFolderId.Should().Be("3396308787");
            modSource.Locked.Should().BeTrue();

            var remoteSource = registry.Find(ModReplacementSourceRegistry.CommunitySourceId);
            remoteSource.Should().NotBeNull();
            remoteSource!.Type.Should().Be(ModRuleSourceType.RepoFile);
            remoteSource.Locked.Should().BeFalse();

            var curatedSource = registry.Find(ModReplacementSourceRegistry.CuratedSourceId);
            curatedSource.Should().NotBeNull();
            curatedSource!.Curated.Should().BeTrue();
            curatedSource.Locked.Should().BeTrue();
        }

        [Fact]
        public void ModReplacementSourceRegistry_ReplaceAll_ShouldPreserveUninstalledModSource()
        {
            var registry = new ModReplacementSourceRegistry(_configService);
            var modBefore = registry.Find(ModReplacementSourceRegistry.ModSourceId)!;
            modBefore.Enabled = true;

            // Simulate UI omitting the mod source row because the mod was uninstalled
            var uiSources = registry.Sources
                .Where(s => s.Id != ModReplacementSourceRegistry.ModSourceId)
                .ToList();

            registry.ReplaceAll(uiSources);

            // Mod source must still be preserved in registry
            var modAfter = registry.Find(ModReplacementSourceRegistry.ModSourceId);
            modAfter.Should().NotBeNull();
            modAfter!.Enabled.Should().Be(modBefore.Enabled);
            modAfter.ModFolderId.Should().Be("3396308787");
        }

        [Fact]
        public void NoVersionWarningSourceRegistry_Defaults_ShouldIncludeModAndRemoteWithoutRimSharpDb()
        {
            var registry = new NoVersionWarningSourceRegistry(_configService);

            registry.Sources.Should().HaveCount(2);

            var modSource = registry.Find(NoVersionWarningSourceRegistry.ModSourceId);
            modSource.Should().NotBeNull();
            modSource!.Type.Should().Be(ModRuleSourceType.Mod);
            modSource.ModFolderId.Should().Be("2599504692");
            modSource.Locked.Should().BeTrue();

            var remoteSource = registry.Find(NoVersionWarningSourceRegistry.CommunitySourceId);
            remoteSource.Should().NotBeNull();
            remoteSource!.Type.Should().Be(ModRuleSourceType.RepoFile);
            remoteSource.Locked.Should().BeFalse();

            // No RimSharpDB source for NoVersionWarning initially
            registry.Find("rimsharp-db").Should().BeNull();
        }

        [Fact]
        public void NoVersionWarningSourceRegistry_ReplaceAll_ShouldPreserveUninstalledModSource()
        {
            var registry = new NoVersionWarningSourceRegistry(_configService);
            var modBefore = registry.Find(NoVersionWarningSourceRegistry.ModSourceId)!;

            // UI passes only the remote source
            var uiSources = registry.Sources
                .Where(s => s.Id != NoVersionWarningSourceRegistry.ModSourceId)
                .ToList();

            registry.ReplaceAll(uiSources);

            var modAfter = registry.Find(NoVersionWarningSourceRegistry.ModSourceId);
            modAfter.Should().NotBeNull();
            modAfter!.ModFolderId.Should().Be("2599504692");
        }
    }
}
