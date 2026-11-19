using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Mods.Replacements;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;
using Xunit;

namespace RimSharp.Tests.Shared.Services.Implementations
{
    public class ModReplacementServiceTests : IDisposable
    {
        private readonly IPathService _mockPathService;
        private readonly IDataUpdateService _mockDataUpdateService;
        private readonly ILoggerService _mockLogger;
        private readonly string _testTempDir;
        private readonly string _tempDbPath;
        private readonly string _tempRepoPath;
        private readonly ModReplacementService _service;

        public ModReplacementServiceTests()
        {
            _mockPathService = Substitute.For<IPathService>();
            _mockDataUpdateService = Substitute.For<IDataUpdateService>();
            _mockLogger = Substitute.For<ILoggerService>();

            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_Replace_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);

            _tempDbPath = Path.Combine(_testTempDir, "replacements.json");
            _mockDataUpdateService.GetExternalFilePath("Replacements", "RimSharp", "replacements.json").Returns(_tempDbPath);

            _tempRepoPath = Path.Combine(_testTempDir, "repo", "replacements.json");
            _mockDataUpdateService.GetExternalFilePath("Replacements", "UseThisInstead", "replacements.json").Returns(_tempRepoPath);

            _service = new ModReplacementService(_mockPathService, _mockDataUpdateService, _mockLogger);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        private void WriteJsonDb(string json)
        {
            File.WriteAllText(_tempDbPath, json);
        }

        private void WriteRepositorySnapshot(string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_tempRepoPath)!);
            File.WriteAllText(_tempRepoPath, json);
        }

        private ModReplacementService CreateServiceWithRegistry(params ModRuleSourceRecord[] sources)
        {
            var configService = Substitute.For<IConfigService>();
            configService.GetConfigValue(ModReplacementSourceRegistry.ConfigKey).Returns((string?)null);
            var registry = new ModReplacementSourceRegistry(configService);
            foreach (var s in registry.Sources) s.Enabled = false; // isolate the sources under test
            foreach (var s in sources) registry.Add(s);
            return new ModReplacementService(_mockPathService, _mockDataUpdateService, _mockLogger, registry);
        }

        private ModRuleSourceRecord CreateLocalSource(string id, string name, string json)
        {
            var path = Path.Combine(_testTempDir, id + ".json");
            File.WriteAllText(path, json);
            return new ModRuleSourceRecord
            {
                Id = id, Name = name, Type = ModRuleSourceType.LocalFile,
                LocalPath = path, Enabled = true
            };
        }

        /// <summary>
        /// Creates an indexed source with one curated UseAlternative rule for a
        /// source workshop id, targeting another workshop id with an optional reason.
        /// </summary>
        private ModRuleSourceRecord CreateIndexedSource(
            string id, string name, long sourceWorkshopId, long targetWorkshopId, string? reason)
        {
            var dir = Path.Combine(_testTempDir, "indexed-" + id);
            Directory.CreateDirectory(dir);
            var indexPath = Path.Combine(dir, "index.json");
            File.WriteAllText(indexPath, @"{ ""rec"": 1 }");
            var reasonJson = reason == null ? "null" : $"\"{reason}\"";
            File.WriteAllText(Path.Combine(dir, "1.json"), $@"{{
                ""notices"": [ {{ ""notice"": {{ ""UseAlternative"": [""Target"", {targetWorkshopId}, {reasonJson}] }},
                                 ""source"": {{ ""JumliDataset"": ""curated"" }} }} ],
                ""identifiers"": [ {{ ""WorkshopId"": {sourceWorkshopId} }} ]
            }}");
            _mockDataUpdateService.GetExternalFilePath("Replacements", id, "index.json").Returns(indexPath);
            return new ModRuleSourceRecord
            {
                Id = id, Name = name, Type = ModRuleSourceType.IndexedRecords,
                Url = "https://example.invalid/", Enabled = true
            };
        }

        [Fact]
        public void GetAllReplacements_ShouldLoadFromJson()
        {

            WriteJsonDb(@"{
                ""mods"": {
                    ""rule1"": { ""steamId"": ""123"", ""modId"": ""old.mod"", ""replacementModId"": ""new.mod"" }
                }
            }");

            var result = _service.GetAllReplacements();

            result.Should().ContainKey("123");
            result["123"].Should().HaveCount(1);
            result["123"][0].ModId.Should().Be("old.mod");
        }

        [Fact]
        public void GetReplacementByPackageId_ShouldOnlyMatchNonSteamRules()
        {

            WriteJsonDb(@"{
                ""mods"": {
                    ""steamRule"": { ""steamId"": ""123"", ""modId"": ""steam.mod"", ""replacementModId"": ""rep1"" },
                    ""nonSteamRule"": { ""steamId"": null, ""modId"": ""nonsteam.mod"", ""replacementModId"": ""rep2"" }
                }
            }");

            var resultSteam = _service.GetReplacementByPackageId("steam.mod");
            var resultNonSteam = _service.GetReplacementByPackageId("nonsteam.mod");

            resultSteam.Should().BeNull();
            resultNonSteam.Should().NotBeNull();
            resultNonSteam!.ReplacementModId.Should().Be("rep2");
        }

        [Fact]
        public void LoadFromUseThisInsteadXml_ShouldLoadValidXmls()
        {

            var modsPath = Path.Combine(_testTempDir, "Mods");
            Directory.CreateDirectory(modsPath);
            _mockPathService.GetModsPath().Returns(modsPath);

            var utiModPath = Path.Combine(modsPath, "3396308787");
            var replacementsPath = Path.Combine(utiModPath, "Replacements");
            Directory.CreateDirectory(replacementsPath);

            var xmlContent = @"
<ModReplacement>
    <Author>Author1</Author>
    <ModId>old.xml.mod</ModId>
    <SteamId>999</SteamId>
    <ReplacementModId>new.xml.mod</ReplacementModId>
</ModReplacement>";
            File.WriteAllText(Path.Combine(replacementsPath, "test.xml"), xmlContent);
            WriteJsonDb(@"{ ""mods"": {} }"); // Empty DB

            var result = _service.GetAllReplacements();

            result.Should().ContainKey("999");
            result["999"][0].ModId.Should().Be("old.xml.mod");
            result["999"][0].Source.Should().Be(ReplacementSource.UseThisInstead);
        }

        [Fact]
        public void UseThisInsteadMod_ShouldComeFirstAndBePrimaryOverJsonDb()
        {
            // Different targets: both suggestions are kept, ordered by source
            // priority (mod folder before curated DB). The primary lookup returns
            // the higher-priority one.
            var modsPath = Path.Combine(_testTempDir, "Mods");
            Directory.CreateDirectory(modsPath);
            _mockPathService.GetModsPath().Returns(modsPath);
            var utiModPath = Path.Combine(modsPath, "3396308787");
            var replacementsPath = Path.Combine(utiModPath, "Replacements");
            Directory.CreateDirectory(replacementsPath);

            File.WriteAllText(Path.Combine(replacementsPath, "test.xml"), @"
<ModReplacement>
    <ModId>mod1</ModId>
    <SteamId>123</SteamId>
    <ReplacementModId>xml-replacement</ReplacementModId>
</ModReplacement>");

            WriteJsonDb(@"{
                ""mods"": {
                    ""rule1"": { ""steamId"": ""123"", ""modId"": ""mod1"", ""replacementModId"": ""json-replacement"" }
                }
            }");

            var result = _service.GetAllReplacements();

            result.Should().ContainKey("123");
            result["123"].Should().HaveCount(2);
            result["123"][0].ReplacementModId.Should().Be("xml-replacement");
            result["123"][0].Source.Should().Be(ReplacementSource.UseThisInstead);
            result["123"][1].ReplacementModId.Should().Be("json-replacement");
            result["123"][1].Source.Should().Be(ReplacementSource.Database);
            _service.GetReplacementBySteamId("123")!.ReplacementModId.Should().Be("xml-replacement");
        }

        [Fact]
        public void RepositorySnapshot_ShouldComeFirstAndBePrimaryOverJsonDb()
        {
            WriteJsonDb(@"{
                ""mods"": {
                    ""rule1"": { ""steamId"": ""123"", ""modId"": ""mod1"", ""replacementModId"": ""json-replacement"" }
                }
            }");
            WriteRepositorySnapshot(@"{
                ""rules"": [
                    { ""oldWorkshopId"": ""123"", ""newWorkshopId"": ""456"", ""oldPackageId"": ""mod1"", ""newPackageId"": ""repo.replacement"" }
                ]
            }");

            var result = _service.GetAllReplacements();

            result.Should().ContainKey("123");
            result["123"].Should().HaveCount(2);
            result["123"][0].ReplacementModId.Should().Be("repo.replacement");
            result["123"][0].Source.Should().Be(ReplacementSource.UseThisInsteadRepo);
            result["123"][1].ReplacementModId.Should().Be("json-replacement");
            result["123"][1].Source.Should().Be(ReplacementSource.Database);
            _service.GetReplacementBySteamId("123")!.ReplacementModId.Should().Be("repo.replacement");
        }

        [Fact]
        public void UseThisInsteadMod_ShouldComeFirstAndBePrimaryOverRepositorySnapshot()
        {
            var modsPath = Path.Combine(_testTempDir, "Mods");
            Directory.CreateDirectory(modsPath);
            _mockPathService.GetModsPath().Returns(modsPath);
            var utiModPath = Path.Combine(modsPath, "3396308787");
            var replacementsPath = Path.Combine(utiModPath, "Replacements");
            Directory.CreateDirectory(replacementsPath);

            File.WriteAllText(Path.Combine(replacementsPath, "test.xml"), @"
<ModReplacement>
    <ModId>mod1</ModId>
    <SteamId>123</SteamId>
    <ReplacementModId>xml-replacement</ReplacementModId>
</ModReplacement>");
            WriteJsonDb(@"{ ""mods"": {} }");
            WriteRepositorySnapshot(@"{
                ""rules"": [
                    { ""oldWorkshopId"": ""123"", ""newWorkshopId"": ""456"", ""oldPackageId"": ""mod1"", ""newPackageId"": ""repo.replacement"" }
                ]
            }");

            var result = _service.GetAllReplacements();

            result.Should().ContainKey("123");
            result["123"].Should().HaveCount(2);
            result["123"][0].ReplacementModId.Should().Be("xml-replacement");
            result["123"][0].Source.Should().Be(ReplacementSource.UseThisInstead);
            result["123"][1].ReplacementModId.Should().Be("repo.replacement");
            result["123"][1].Source.Should().Be(ReplacementSource.UseThisInsteadRepo);
            _service.GetReplacementBySteamId("123")!.ReplacementModId.Should().Be("xml-replacement");
        }

        [Fact]
        public void RepositorySnapshot_ShouldFillGapsWhenJsonDbHasNoEntry()
        {
            WriteJsonDb(@"{ ""mods"": {} }");
            WriteRepositorySnapshot(@"{
                ""rules"": [
                    { ""oldWorkshopId"": ""789"", ""newWorkshopId"": ""1010"", ""oldPackageId"": ""other.mod"", ""newPackageId"": ""repo.replacement"" }
                ]
            }");

            var result = _service.GetAllReplacements();

            result.Should().ContainKey("789");
            result["789"][0].ReplacementModId.Should().Be("repo.replacement");
        }

        [Fact]
        public void RepositorySnapshot_MissingFile_ShouldNotThrowAndFallThroughToJsonDb()
        {
            WriteJsonDb(@"{
                ""mods"": {
                    ""rule1"": { ""steamId"": ""123"", ""modId"": ""mod1"", ""replacementModId"": ""json-replacement"" }
                }
            }");

            var result = _service.GetAllReplacements();

            result.Should().ContainKey("123");
            result["123"][0].ReplacementModId.Should().Be("json-replacement");
            result["123"][0].Source.Should().Be(ReplacementSource.Database);
        }

        [Fact]
        public void LoadFromLocalFile_ShouldStampLocalFileSourceWithRegistryName()
        {
            var localPath = Path.Combine(_testTempDir, "local-rules.json");
            File.WriteAllText(localPath, @"{
                ""rules"": [
                    { ""oldWorkshopId"": ""555"", ""oldPackageId"": ""local.old"", ""newPackageId"": ""local.new"" }
                ]
            }");
            var service = CreateServiceWithRegistry(new ModRuleSourceRecord
            {
                Id = "my-local",
                Name = "My Local Rules",
                Type = ModRuleSourceType.LocalFile,
                LocalPath = localPath,
                Enabled = true
            });

            var result = service.GetAllReplacements();

            result.Should().ContainKey("555");
            result["555"][0].Source.Should().Be(ReplacementSource.LocalFile);
            result["555"][0].SourceName.Should().Be("My Local Rules");
        }

        [Fact]
        public void LoadFromIndexedRecords_ShouldEmitCuratedRulesWithReasonAndSource()
        {
            var indexedDir = Path.Combine(_testTempDir, "indexed");
            Directory.CreateDirectory(indexedDir);
            var indexPath = Path.Combine(indexedDir, "index.json");
            File.WriteAllText(indexPath, @"{ ""unlimitedhugs.allowtool"": 2760, ""hams.EternalYouthTraits"": 1088 }");
            File.WriteAllText(Path.Combine(indexedDir, "2760.json"), @"{
                ""notices"": [ { ""notice"": { ""UseAlternative"": [""Keyz' Allow Utilities"", 3524716849, ""Better performance, same features.""] },
                                 ""source"": { ""JumliDataset"": ""Common Performance Advice"" } } ],
                ""identifiers"": [ { ""PackageId"": ""unlimitedhugs.allowtool"" }, { ""WorkshopId"": 761421485 } ]
            }");
            File.WriteAllText(Path.Combine(indexedDir, "1088.json"), @"{
                ""notices"": [ { ""notice"": { ""UseAlternative"": [""Eternal Youth 1.6 Fix"", 3788130822, null] },
                                  ""source"": ""UseThisInsteadDatabase"" } ],
                ""identifiers"": [ { ""PackageId"": ""hams.EternalYouthTraits"" }, { ""WorkshopId"": 2106011986 } ]
            }");
            _mockDataUpdateService.GetExternalFilePath("Replacements", "indexed-source", "index.json").Returns(indexPath);

            var service = CreateServiceWithRegistry(new ModRuleSourceRecord
            {
                Id = "indexed-source",
                Name = "Community Index (Reasons)",
                Type = ModRuleSourceType.IndexedRecords,
                Url = "https://example.invalid/",
                Enabled = true
            });

            var result = service.GetAllReplacements();

            result.Should().ContainKey("761421485");
            var rule = result["761421485"][0];
            rule.ModId.Should().Be("unlimitedhugs.allowtool");
            rule.ReplacementName.Should().Be("Keyz' Allow Utilities");
            rule.ReplacementSteamId.Should().Be("3524716849");
            rule.Reason.Should().Be("Better performance, same features.");
            rule.Source.Should().Be(ReplacementSource.IndexedRecords);
            rule.SourceName.Should().Be("Community Index (Reasons)");
            result.Should().NotContainKey("2106011986", "UTI-sourced records contribute nothing");
        }

        [Fact]
        public void MultipleSources_ShouldKeepAllSuggestionsWithProvenance()
        {
            // Different targets: both suggestions survive, ordered by source
            // priority, each keeping its own Source/SourceName.
            var localPath = Path.Combine(_testTempDir, "local-rules.json");
            File.WriteAllText(localPath, @"{
                ""rules"": [ { ""oldWorkshopId"": ""777"", ""newPackageId"": ""local.win"" } ]
            }");
            WriteRepositorySnapshot(@"{
                ""rules"": [ { ""oldWorkshopId"": ""777"", ""newPackageId"": ""repo.lose"" } ]
            }");
            var service = CreateServiceWithRegistry(
                new ModRuleSourceRecord
                {
                    Id = "my-local", Name = "My Local Rules",
                    Type = ModRuleSourceType.LocalFile, LocalPath = localPath, Enabled = true
                },
                new ModRuleSourceRecord
                {
                    Id = "UseThisInstead", Name = "Repo",
                    Type = ModRuleSourceType.RepoFile, Url = "http://example.com/replacements.json", Enabled = true
                });

            var result = service.GetAllReplacements();

            result["777"].Should().HaveCount(2);
            result["777"][0].ReplacementModId.Should().Be("local.win");
            result["777"][0].Source.Should().Be(ReplacementSource.LocalFile);
            result["777"][0].SourceName.Should().Be("My Local Rules");
            result["777"][1].ReplacementModId.Should().Be("repo.lose");
            result["777"][1].Source.Should().Be(ReplacementSource.UseThisInsteadRepo);
        }

        [Fact]
        public void SameTarget_ReasonCarryingSuggestionWins()
        {
            // Lower-priority source carries the reason: it must win over the
            // higher-priority source that has no reason.
            var service = CreateServiceWithRegistry(
                CreateIndexedSource("a", "Source A", 111, 900, reason: null),
                CreateIndexedSource("b", "Source B", 111, 900, reason: "Curated reason"));

            var result = service.GetAllReplacements();

            result["111"].Should().HaveCount(1);
            result["111"][0].Reason.Should().Be("Curated reason");
            result["111"][0].SourceName.Should().Be("Source B");
        }

        [Fact]
        public void SameTarget_BothHaveReason_HigherPriorityWins()
        {
            var service = CreateServiceWithRegistry(
                CreateIndexedSource("a", "Source A", 111, 900, reason: "A reason"),
                CreateIndexedSource("b", "Source B", 111, 900, reason: "B reason"));

            var result = service.GetAllReplacements();

            result["111"].Should().HaveCount(1);
            result["111"][0].Reason.Should().Be("A reason");
            result["111"][0].SourceName.Should().Be("Source A");
        }

        [Fact]
        public void SameTarget_NeitherHasReason_HigherPriorityWins()
        {
            var service = CreateServiceWithRegistry(
                CreateIndexedSource("a", "Source A", 111, 900, reason: null),
                CreateIndexedSource("b", "Source B", 111, 900, reason: null));

            var result = service.GetAllReplacements();

            result["111"].Should().HaveCount(1);
            result["111"][0].SourceName.Should().Be("Source A");
        }

        [Fact]
        public void SingleSuggestionLookup_ReturnsReasonCarryingPrimary()
        {
            // Different targets: both kept. The reason-carrying suggestion is
            // the primary even though it came from the lower-priority source.
            var service = CreateServiceWithRegistry(
                CreateIndexedSource("a", "Source A", 111, 900, reason: null),
                CreateIndexedSource("b", "Source B", 111, 901, reason: "Preferred"));

            var primary = service.GetReplacementBySteamId("111");
            var all = service.GetReplacementsBySteamId("111");

            all.Should().HaveCount(2);
            primary.Should().NotBeNull();
            primary!.ReplacementSteamId.Should().Be("901");
            primary.Reason.Should().Be("Preferred");
        }

        [Fact]
        public void CaseInsensitiveTargetMatching_ShouldCollapseSameTarget()
        {
            var service = CreateServiceWithRegistry(
                CreateLocalSource("a", "Source A", @"{ ""rules"": [
                    { ""oldWorkshopId"": ""333"", ""newPackageId"": ""New.Mod"" } ] }"),
                CreateLocalSource("b", "Source B", @"{ ""rules"": [
                    { ""oldWorkshopId"": ""333"", ""newPackageId"": ""new.mod"" } ] }"));

            var result = service.GetAllReplacements();

            result["333"].Should().HaveCount(1);
            result["333"][0].ReplacementModId.Should().Be("New.Mod");
            result["333"][0].SourceName.Should().Be("Source A");
        }

        [Fact]
        public void GetReplacementsByPackageId_ReturnsAllSuggestions()
        {
            var service = CreateServiceWithRegistry(
                CreateLocalSource("a", "Source A", @"{ ""rules"": [
                    { ""oldPackageId"": ""source.mod"", ""newPackageId"": ""target.a"" } ] }"),
                CreateLocalSource("b", "Source B", @"{ ""rules"": [
                    { ""oldPackageId"": ""source.mod"", ""newPackageId"": ""target.b"" } ] }"));

            var all = service.GetReplacementsByPackageId("SOURCE.MOD");
            var primary = service.GetReplacementByPackageId("source.mod");

            all.Should().HaveCount(2);
            all[0].ReplacementModId.Should().Be("target.a");
            all[1].ReplacementModId.Should().Be("target.b");
            primary.Should().NotBeNull();
            primary!.ReplacementModId.Should().Be("target.a");
        }
    }
}
