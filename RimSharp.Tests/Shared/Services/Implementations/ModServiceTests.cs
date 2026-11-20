using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;
using Xunit;

namespace RimSharp.Tests.Shared.Services.Implementations
{
    public class ModServiceTests : IDisposable
    {
        private readonly IPathService _mockPathService;
        private readonly IModRulesService _mockRulesService;
        private readonly IModCustomService _mockCustomService;
        private readonly IMlieVersionService _mockMlieVersionService;
        private readonly ILoggerService _mockLogger;
        private readonly string _testTempDir;
        private readonly string _gamePath;
        private readonly string _modsPath;
        private readonly string _configPath;

        public ModServiceTests()
        {
            _mockPathService = Substitute.For<IPathService>();
            _mockRulesService = Substitute.For<IModRulesService>();
            _mockCustomService = Substitute.For<IModCustomService>();
            _mockMlieVersionService = Substitute.For<IMlieVersionService>();
            _mockLogger = Substitute.For<ILoggerService>();

            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharp_ModServiceTest_" + Guid.NewGuid().ToString("N"));
            _gamePath = Path.Combine(_testTempDir, "Game");
            _modsPath = Path.Combine(_testTempDir, "Mods");
            _configPath = Path.Combine(_testTempDir, "Config");

            Directory.CreateDirectory(_gamePath);
            Directory.CreateDirectory(_modsPath);
            Directory.CreateDirectory(_configPath);

            _mockPathService.GetGamePath().Returns(_gamePath);
            _mockPathService.GetModsPath().Returns(_modsPath);
            _mockPathService.GetConfigPath().Returns(_configPath);
            _mockPathService.GetMajorGameVersion().Returns("1.5");
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        private ModService CreateService()
        {
            return new ModService(_mockPathService, _mockRulesService, _mockCustomService, _mockMlieVersionService, _mockLogger);
        }

        [Fact]
        public async Task LoadModsAsync_ShouldParseValidMod()
        {

            var modDir = Path.Combine(_modsPath, "TestMod");
            var aboutDir = Path.Combine(modDir, "About");
            Directory.CreateDirectory(aboutDir);
            
            var aboutXml = @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>Test Mod Name</name>
    <packageId>test.mod.id</packageId>
    <author>Test Author</author>
    <supportedVersions>
        <li>1.5</li>
    </supportedVersions>
</ModMetaData>";
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"), aboutXml);

            var service = CreateService();

            await service.LoadModsAsync();
            var mods = service.GetLoadedMods().ToList();

            // Virtual-list invariant: Core is ALWAYS present (sentinel when
            // the game's Data folder has none).
            mods.Should().Contain(m => m.PackageId == "Ludeon.RimWorld" && m.ModType == ModType.Core && m.IsActive);

            var parsed = mods.Single(m => m.PackageId == "test.mod.id");
            parsed.Name.Should().Be("Test Mod Name");
            parsed.SupportedVersionStrings.Should().Contain("1.5");
        }

        [Fact]
        public async Task LoadModsAsync_WhenNameMissingInXml_ShouldUseFolderName()
        {

            var modDir = Path.Combine(_modsPath, "MyFolderName");
            var aboutDir = Path.Combine(modDir, "About");
            Directory.CreateDirectory(aboutDir);
            
            // Missing <name> element
            var aboutXml = @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <packageId>test.mod.id</packageId>
</ModMetaData>";
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"), aboutXml);

            var service = CreateService();

            await service.LoadModsAsync();
            var mods = service.GetLoadedMods().ToList();

            mods.Should().Contain(m => m.PackageId == "Ludeon.RimWorld"); // Core sentinel invariant
            mods.Single(m => m.PackageId == "test.mod.id").Name.Should().Be("MyFolderName");
        }

        [Fact]
        public async Task LoadModsAsync_WhenXmlIsMalformed_ShouldSkipMod()
        {

            var modDir = Path.Combine(_modsPath, "BadMod");
            var aboutDir = Path.Combine(modDir, "About");
            Directory.CreateDirectory(aboutDir);
            
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"), "NOT XML");

            var service = CreateService();

            await service.LoadModsAsync();
            var mods = service.GetLoadedMods().ToList();

            // The malformed mod is skipped, but the Core sentinel constant
            // of the virtual list remains.
            mods.Should().ContainSingle(m => m.PackageId == "Ludeon.RimWorld");
            mods.Should().NotContain(m => m.PackageId == "bad.mod" || m.Name == "BadMod");
        }

        [Fact]
        public async Task LoadModsAsync_ReadsSteamTagsFromCacheAndSeedsTagsString()
        {
            var modDir = Path.Combine(_modsPath, "TranslatedMod");
            var aboutDir = Path.Combine(modDir, "About");
            Directory.CreateDirectory(aboutDir);

            var aboutXml = @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>Translated Mod</name>
    <packageId>test.translated</packageId>
    <author>Someone</author>
    <supportedVersions>
        <li>1.5</li>
    </supportedVersions>
</ModMetaData>";
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"), aboutXml);

            // RimSharp.json lives in the mod ROOT (next to About/), as written
            // by WorkshopDescriptionService.
            var cacheJson = @"{
    ""SteamId"": ""2557451735"",
    ""SteamName"": ""Translated Mod"",
    ""SteamDescription"": ""A translation."",
    ""Source"": ""steamapi"",
    ""Tags"": [ ""Translation"", ""Scenario"" ]
}";
            File.WriteAllText(Path.Combine(modDir, "RimSharp.json"), cacheJson);

            var service = CreateService();
            await service.LoadModsAsync();
            var mods = service.GetLoadedMods().ToList();

            var parsed = mods.Single(m => m.PackageId == "test.translated");
            parsed.SteamTags.Should().BeEquivalentTo(new[] { "Translation", "Scenario" });
            // Tags is seeded from the Steam tags so the details panel and the
            // tag filter see them even without user-defined tags.
            parsed.Tags.Should().Be("Translation, Scenario");
            parsed.TagList.Should().BeEquivalentTo(new[] { "Translation", "Scenario" });
        }

        [Fact]
        public async Task LoadModsAsync_WhenCacheHasNoTagsProperty_LeavesTagsEmpty()
        {
            var modDir = Path.Combine(_modsPath, "NoTagsMod");
            var aboutDir = Path.Combine(modDir, "About");
            Directory.CreateDirectory(aboutDir);

            var aboutXml = @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>No Tags Mod</name>
    <packageId>test.notags</packageId>
</ModMetaData>";
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"), aboutXml);

            // Cache without a Tags array (older schema) must be handled gracefully.
            File.WriteAllText(Path.Combine(modDir, "RimSharp.json"),
                @"{ ""SteamId"": ""123"", ""SteamName"": ""No Tags Mod"" }");

            var service = CreateService();
            await service.LoadModsAsync();
            var mods = service.GetLoadedMods().ToList();

            var parsed = mods.Single(m => m.PackageId == "test.notags");
            parsed.SteamTags.Should().BeEmpty();
            parsed.Tags.Should().BeEmpty();
        }

        [Fact]
        public async Task ReapplyRulesAsync_ShouldRefreshAndReapplyRulesAndCustomInfo()
        {
            var modDir = Path.Combine(_modsPath, "TestMod");
            var aboutDir = Path.Combine(modDir, "About");
            Directory.CreateDirectory(aboutDir);
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"), @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>Test Mod Name</name>
    <packageId>test.mod.id</packageId>
    <supportedVersions>
        <li>1.5</li>
    </supportedVersions>
</ModMetaData>");

            var service = CreateService();
            await service.LoadModsAsync();
            var loadedBefore = service.GetLoadedMods().Count();

            await service.ReapplyRulesAsync();

            // The rules cache is refreshed (registry re-read) and both layers
            // are re-applied onto the SAME mod instances - no rescan.
            _mockRulesService.Received(1).RefreshRules();
            _mockRulesService.Received(2).ApplyRulesToMods(Arg.Any<IEnumerable<ModItem>>());
            _mockCustomService.Received(2).ApplyCustomInfoToMods(Arg.Any<IEnumerable<ModItem>>());
            service.GetLoadedMods().Should().HaveCount(loadedBefore);
        }

        [Fact]
        public async Task ReapplySyncedDataAsync_ShouldReReadVersionsAndReapplyRules()
        {
            var modDir = Path.Combine(_modsPath, "SyncedMod");
            var aboutDir = Path.Combine(modDir, "About");
            Directory.CreateDirectory(aboutDir);
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"), @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>Synced Mod</name>
    <packageId>test.synced</packageId>
    <supportedVersions>
        <li>1.5</li>
    </supportedVersions>
</ModMetaData>");

            // Initially there is no external version for the mod. The lambda
            // lets the test mutate the returned data to simulate a sync that
            // happened after the initial load.
            var externalVersions = new Dictionary<string, List<ExternalVersionInfo>>();
            _mockMlieVersionService.GetExternalVersions().Returns(_ => externalVersions);

            var service = CreateService();
            await service.LoadModsAsync();

            var mod = service.GetLoadedMods().Single(m => m.PackageId == "test.synced");
            mod.SupportedVersions.Should().NotContain(v => v.Source == VersionSource.External);

            // Simulate a successful data sync that added a new external version.
            externalVersions["test.synced"] = new List<ExternalVersionInfo>
            {
                new("1.6", "Community NVW")
            };

            await service.ReapplySyncedDataAsync();

            // The freshly synced version was re-read and applied to the SAME
            // mod instance (no rescan).
            var external = mod.SupportedVersions.Single(v => v.Source == VersionSource.External);
            external.Version.Should().Be("1.6");
            external.ExternalSourceName.Should().Be("Community NVW");
            external.Unofficial.Should().BeTrue();

            // GetExternalVersions was re-read (once at load, once on reapply).
            _mockMlieVersionService.Received(2).GetExternalVersions();

            // The rules cache was refreshed and both layers were re-applied:
            // once during LoadModsAsync, once via ReapplyRulesAsync.
            _mockRulesService.Received(1).RefreshRules();
            _mockRulesService.Received(2).ApplyRulesToMods(Arg.Any<IEnumerable<ModItem>>());
            _mockCustomService.Received(2).ApplyCustomInfoToMods(Arg.Any<IEnumerable<ModItem>>());
        }

        [Fact]
        public async Task ReapplySyncedDataAsync_ShouldStripStaleExternalVersionsBeforeApplyingFreshOnes()
        {
            var modDir = Path.Combine(_modsPath, "StaleVersionMod");
            var aboutDir = Path.Combine(modDir, "About");
            Directory.CreateDirectory(aboutDir);
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"), @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>Stale Version Mod</name>
    <packageId>test.stale.version</packageId>
    <supportedVersions>
        <li>1.5</li>
    </supportedVersions>
</ModMetaData>");

            var externalVersions = new Dictionary<string, List<ExternalVersionInfo>>
            {
                ["test.stale.version"] = new List<ExternalVersionInfo> { new("1.6", "Old Source") }
            };
            _mockMlieVersionService.GetExternalVersions().Returns(_ => externalVersions);

            var service = CreateService();
            await service.LoadModsAsync();
            var mod = service.GetLoadedMods().Single(m => m.PackageId == "test.stale.version");
            mod.SupportedVersions.Single(v => v.Source == VersionSource.External).Version.Should().Be("1.6");

            // The source was updated: 1.6 disappeared, 1.7 was added by a
            // different community source.
            externalVersions["test.stale.version"] = new List<ExternalVersionInfo> { new("1.7", "New Source") };
            await service.ReapplySyncedDataAsync();

            mod.SupportedVersions.Should().NotContain(v => v.Version == "1.6", "the removed version must not linger");
            var fresh = mod.SupportedVersions.Single(v => v.Source == VersionSource.External);
            fresh.Version.Should().Be("1.7");
            fresh.ExternalSourceName.Should().Be("New Source");
            // The About.xml (Official) entry is never stripped.
            mod.SupportedVersions.Should().Contain(v => v.Version == "1.5" && v.Source == VersionSource.Official);
        }

        [Fact]
        public async Task ReapplySyncedDataAsync_WhenExternalSetBecomesEmpty_ShouldRemoveAllExternalVersions()
        {
            var modDir = Path.Combine(_modsPath, "EmptyExternalMod");
            var aboutDir = Path.Combine(modDir, "About");
            Directory.CreateDirectory(aboutDir);
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"), @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>Empty External Mod</name>
    <packageId>test.empty.external</packageId>
    <supportedVersions>
        <li>1.5</li>
    </supportedVersions>
</ModMetaData>");

            var externalVersions = new Dictionary<string, List<ExternalVersionInfo>>
            {
                ["test.empty.external"] = new List<ExternalVersionInfo> { new("1.6", "Removed Source") }
            };
            _mockMlieVersionService.GetExternalVersions().Returns(_ => externalVersions);

            var service = CreateService();
            await service.LoadModsAsync();
            var mod = service.GetLoadedMods().Single(m => m.PackageId == "test.empty.external");
            mod.SupportedVersions.Should().Contain(v => v.Source == VersionSource.External);

            // The only external source was removed/disabled -> empty fresh set.
            externalVersions.Clear();
            await service.ReapplySyncedDataAsync();

            mod.SupportedVersions.Should().NotContain(v => v.Source == VersionSource.External,
                "an empty fresh set must strip the stale external versions (strip runs before the early return)");
            mod.SupportedVersions.Should().ContainSingle(v => v.Version == "1.5" && v.Source == VersionSource.Official);
        }

        [Fact]
        public async Task LoadModsAsync_ShouldTrackAboutXmlLoadOrderRules()
        {
            var modDir = Path.Combine(_modsPath, "AboutRulesMod");
            var aboutDir = Path.Combine(modDir, "About");
            Directory.CreateDirectory(aboutDir);
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"), @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>About Rules Mod</name>
    <packageId>test.about.rules</packageId>
    <loadBefore>
        <li>about.before.one</li>
        <li>about.before.two</li>
    </loadBefore>
    <loadAfter>
        <li>about.after.one</li>
    </loadAfter>
    <incompatibleWith>
        <li>about.bad.one</li>
        <li>about.bad.two</li>
    </incompatibleWith>
</ModMetaData>");

            var service = CreateService();
            await service.LoadModsAsync();

            var mod = service.GetLoadedMods().Single(m => m.PackageId == "test.about.rules");

            mod.LoadBefore.Should().BeEquivalentTo(new[] { "about.before.one", "about.before.two" });
            mod.LoadAfter.Should().BeEquivalentTo(new[] { "about.after.one" });
            mod.IncompatibleWith.Should().ContainKeys("about.bad.one", "about.bad.two");

            // The About.xml declarations are tracked as their own origin.
            mod.AboutLoadBefore.Should().BeEquivalentTo(new[] { "about.before.one", "about.before.two" });
            mod.AboutLoadAfter.Should().BeEquivalentTo(new[] { "about.after.one" });
            mod.AboutIncompatibleWith.Should().BeEquivalentTo(new[] { "about.bad.one", "about.bad.two" });
            mod.IncompatibleWith.Values.Should().OnlyContain(r => r.HardIncompatibility);
        }

        [Fact]
        public async Task ReapplyRulesAsync_ShouldKeepAboutXmlProvenanceSets()
        {
            var modDir = Path.Combine(_modsPath, "AboutRulesMod");
            var aboutDir = Path.Combine(modDir, "About");
            Directory.CreateDirectory(aboutDir);
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"), @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>About Rules Mod</name>
    <packageId>test.about.rules</packageId>
    <loadBefore>
        <li>about.before.one</li>
    </loadBefore>
    <loadAfter>
        <li>about.after.one</li>
    </loadAfter>
    <incompatibleWith>
        <li>about.bad.one</li>
    </incompatibleWith>
</ModMetaData>");

            var service = CreateService();
            await service.LoadModsAsync();

            var mod = service.GetLoadedMods().Single(m => m.PackageId == "test.about.rules");
            mod.AboutLoadBefore.Should().BeEquivalentTo(new[] { "about.before.one" });
            mod.AboutLoadAfter.Should().BeEquivalentTo(new[] { "about.after.one" });
            mod.AboutIncompatibleWith.Should().BeEquivalentTo(new[] { "about.bad.one" });

            // A live re-merge re-applies the rules database onto the SAME mod
            // instances. The About.xml provenance sets are never touched by
            // that layer, so they must survive unchanged.
            await service.ReapplyRulesAsync();

            mod.AboutLoadBefore.Should().BeEquivalentTo(new[] { "about.before.one" });
            mod.AboutLoadAfter.Should().BeEquivalentTo(new[] { "about.after.one" });
            mod.AboutIncompatibleWith.Should().BeEquivalentTo(new[] { "about.bad.one" });
            mod.LoadBefore.Should().Contain("about.before.one");
            mod.LoadAfter.Should().Contain("about.after.one");
            mod.IncompatibleWith.Should().ContainKey("about.bad.one");
        }

        [Fact]
        public async Task LoadModsAsync_ShouldApplyExternalVersionsWithSourceName()
        {
            var modDir = Path.Combine(_modsPath, "ExternalVersionMod");
            var aboutDir = Path.Combine(modDir, "About");
            Directory.CreateDirectory(aboutDir);
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"), @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>External Version Mod</name>
    <packageId>test.external.version</packageId>
    <supportedVersions>
        <li>1.5</li>
    </supportedVersions>
</ModMetaData>");

            _mockMlieVersionService.GetExternalVersions().Returns(new Dictionary<string, List<ExternalVersionInfo>>
            {
                ["test.external.version"] = new List<ExternalVersionInfo>
                {
                    new("1.6", "Community NVW")
                }
            });

            var service = CreateService();
            await service.LoadModsAsync();

            var mod = service.GetLoadedMods().Single(m => m.PackageId == "test.external.version");
            var external = mod.SupportedVersions.Single(v => v.Source == VersionSource.External);
            external.Version.Should().Be("1.6");
            external.ExternalSourceName.Should().Be("Community NVW");
            external.Unofficial.Should().BeTrue();
        }

        [Fact]
        public async Task LoadModsAsync_ShouldNotDuplicateOfficialVersionAsExternal()
        {
            var modDir = Path.Combine(_modsPath, "DedupeVersionMod");
            var aboutDir = Path.Combine(modDir, "About");
            Directory.CreateDirectory(aboutDir);
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"), @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>Dedupe Version Mod</name>
    <packageId>test.dedupe.version</packageId>
    <supportedVersions>
        <li>1.5</li>
    </supportedVersions>
</ModMetaData>");

            _mockMlieVersionService.GetExternalVersions().Returns(new Dictionary<string, List<ExternalVersionInfo>>
            {
                ["test.dedupe.version"] = new List<ExternalVersionInfo>
                {
                    new("1.5", "Community NVW")
                }
            });

            var service = CreateService();
            await service.LoadModsAsync();

            var mod = service.GetLoadedMods().Single(m => m.PackageId == "test.dedupe.version");
            mod.SupportedVersions.Where(v => v.Version == "1.5").Should().ContainSingle();
            mod.SupportedVersions.Single(v => v.Version == "1.5").Source.Should().Be(VersionSource.Official);
            mod.SupportedVersions.Should().NotContain(v => v.Source == VersionSource.External);
        }

        // ------------------------------------------------------------------
        // *ByVersion About.xml support
        // ------------------------------------------------------------------

        private async Task<ModItem> LoadVersionedModAsync(string aboutXml, string majorVersion)
        {
            _mockPathService.GetMajorGameVersion().Returns(majorVersion);

            var modDir = Path.Combine(_modsPath, "VersionedMod");
            var aboutDir = Path.Combine(modDir, "About");
            Directory.CreateDirectory(aboutDir);
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"), aboutXml);

            var service = CreateService();
            await service.LoadModsAsync();
            return service.GetLoadedMods().Single(m => m.PackageId == "test.versioned");
        }

        [Fact]
        public async Task LoadModsAsync_MatchingVersionBlock_ReplacesAllPlainRules()
        {
            var aboutXml = @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>Versioned Mod</name>
    <packageId>test.versioned</packageId>
    <modDependencies>
        <li><packageId>plain.dep</packageId><displayName>Plain Dep</displayName></li>
    </modDependencies>
    <loadBefore><li>plain.before</li></loadBefore>
    <loadAfter><li>plain.after</li></loadAfter>
    <incompatibleWith><li>plain.bad</li></incompatibleWith>
    <modDependenciesByVersion>
        <v1.7>
            <li><packageId>version.dep</packageId><displayName>Version Dep</displayName><steamWorkshopUrl>https://example.com/dep</steamWorkshopUrl></li>
        </v1.7>
    </modDependenciesByVersion>
    <loadBeforeByVersion><v1.7><li>version.before</li></v1.7></loadBeforeByVersion>
    <loadAfterByVersion><v1.7><li>version.after</li></v1.7></loadAfterByVersion>
    <incompatibleWithByVersion><v1.7><li>version.bad</li></v1.7></incompatibleWithByVersion>
</ModMetaData>";

            var mod = await LoadVersionedModAsync(aboutXml, "1.7");

            mod.ModDependencies.Should().ContainSingle().Which.PackageId.Should().Be("version.dep");
            mod.ModDependencies.Should().NotContain(d => d.PackageId == "plain.dep");
            mod.LoadBefore.Should().BeEquivalentTo(new[] { "version.before" });
            mod.LoadAfter.Should().BeEquivalentTo(new[] { "version.after" });
            mod.IncompatibleWith.Should().ContainKey("version.bad").And.NotContainKey("plain.bad");

            // Provenance is rebuilt from the RESOLVED entries, not the plain ones.
            mod.AboutLoadBefore.Should().BeEquivalentTo(new[] { "version.before" });
            mod.AboutLoadAfter.Should().BeEquivalentTo(new[] { "version.after" });
            mod.AboutIncompatibleWith.Should().BeEquivalentTo(new[] { "version.bad" });
            mod.IncompatibleWith["version.bad"].HardIncompatibility.Should().BeTrue();
            mod.IncompatibleWith["version.bad"].Comment.Should().ContainSingle()
                .Which.Should().Be("Incompatible according to 'Versioned Mod' authors");
        }

        [Fact]
        public async Task LoadModsAsync_SelfClosingVersionBlock_CancelsPlainRules()
        {
            var aboutXml = @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>Versioned Mod</name>
    <packageId>test.versioned</packageId>
    <modDependencies><li><packageId>plain.dep</packageId></li></modDependencies>
    <loadBefore><li>plain.before</li></loadBefore>
    <loadAfter><li>plain.after</li></loadAfter>
    <incompatibleWith><li>plain.bad</li></incompatibleWith>
    <modDependenciesByVersion><v1.0 /></modDependenciesByVersion>
    <loadBeforeByVersion><v1.0 /></loadBeforeByVersion>
    <loadAfterByVersion><v1.0 /></loadAfterByVersion>
    <incompatibleWithByVersion><v1.0 /></incompatibleWithByVersion>
</ModMetaData>";

            var mod = await LoadVersionedModAsync(aboutXml, "1.0");

            mod.ModDependencies.Should().BeEmpty();
            mod.LoadBefore.Should().BeEmpty();
            mod.LoadAfter.Should().BeEmpty();
            mod.IncompatibleWith.Should().BeEmpty();
            mod.AboutLoadBefore.Should().BeEmpty();
            mod.AboutLoadAfter.Should().BeEmpty();
            mod.AboutIncompatibleWith.Should().BeEmpty();
        }

        [Theory]
        [InlineData("1.8")]
        [InlineData("1.6")]
        public async Task LoadModsAsync_NoMatchingVersionBlock_KeepsPlainRules(string majorVersion)
        {
            var aboutXml = @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>Versioned Mod</name>
    <packageId>test.versioned</packageId>
    <modDependencies><li><packageId>plain.dep</packageId></li></modDependencies>
    <loadBefore><li>plain.before</li></loadBefore>
    <loadAfter><li>plain.after</li></loadAfter>
    <incompatibleWith><li>plain.bad</li></incompatibleWith>
    <modDependenciesByVersion><v1.7><li><packageId>version.dep</packageId></li></v1.7></modDependenciesByVersion>
    <loadBeforeByVersion><v1.7><li>version.before</li></v1.7></loadBeforeByVersion>
    <loadAfterByVersion><v1.7><li>version.after</li></v1.7></loadAfterByVersion>
    <incompatibleWithByVersion><v1.7><li>version.bad</li></v1.7></incompatibleWithByVersion>
</ModMetaData>";

            var mod = await LoadVersionedModAsync(aboutXml, majorVersion);

            mod.ModDependencies.Should().ContainSingle().Which.PackageId.Should().Be("plain.dep");
            mod.LoadBefore.Should().BeEquivalentTo(new[] { "plain.before" });
            mod.LoadAfter.Should().BeEquivalentTo(new[] { "plain.after" });
            mod.IncompatibleWith.Should().ContainKey("plain.bad").And.NotContainKey("version.bad");
            mod.AboutLoadBefore.Should().BeEquivalentTo(new[] { "plain.before" });
            mod.AboutLoadAfter.Should().BeEquivalentTo(new[] { "plain.after" });
            mod.AboutIncompatibleWith.Should().BeEquivalentTo(new[] { "plain.bad" });

            // No matching version -> raw ByVersion fields stay empty.
            mod.ModDependenciesByVersion.Should().BeEmpty();
            mod.LoadBeforeByVersion.Should().BeEmpty();
            mod.LoadAfterByVersion.Should().BeEmpty();
            mod.IncompatibleWithByVersion.Should().BeEmpty();
        }

        [Fact]
        public async Task LoadModsAsync_ByVersionFields_ContainOnlyMatchingVersionEntries()
        {
            var aboutXml = @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>Versioned Mod</name>
    <packageId>test.versioned</packageId>
    <modDependenciesByVersion>
        <v1.6><li><packageId>old.dep</packageId></li></v1.6>
        <v1.7><li><packageId>new.dep</packageId></li></v1.7>
    </modDependenciesByVersion>
    <loadBeforeByVersion>
        <v1.6><li>old.before</li></v1.6>
        <v1.7><li>new.before</li></v1.7>
    </loadBeforeByVersion>
    <loadAfterByVersion>
        <v1.6><li>old.after</li></v1.6>
        <v1.7><li>new.after</li></v1.7>
    </loadAfterByVersion>
    <incompatibleWithByVersion>
        <v1.6><li>old.bad</li></v1.6>
        <v1.7><li>new.bad</li></v1.7>
    </incompatibleWithByVersion>
</ModMetaData>";

            var mod = await LoadVersionedModAsync(aboutXml, "1.7");

            mod.ModDependenciesByVersion.Should().ContainSingle().Which.PackageId.Should().Be("new.dep");
            mod.LoadBeforeByVersion.Should().BeEquivalentTo(new[] { "new.before" });
            mod.LoadAfterByVersion.Should().BeEquivalentTo(new[] { "new.after" });
            mod.IncompatibleWithByVersion.Should().BeEquivalentTo(new[] { "new.bad" });

            // With no plain rules, the resolved fields mirror the matching block.
            mod.ModDependencies.Should().ContainSingle().Which.PackageId.Should().Be("new.dep");
            mod.LoadBefore.Should().BeEquivalentTo(new[] { "new.before" });
            mod.LoadAfter.Should().BeEquivalentTo(new[] { "new.after" });
            mod.IncompatibleWith.Should().ContainKey("new.bad").And.NotContainKey("old.bad");
        }

        [Fact]
        public async Task LoadModsAsync_ForceRules_AreNotVersioned()
        {
            var aboutXml = @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>Versioned Mod</name>
    <packageId>test.versioned</packageId>
    <forceLoadBefore><li>force.before</li></forceLoadBefore>
    <forceLoadAfter><li>force.after</li></forceLoadAfter>
    <loadBefore><li>plain.before</li></loadBefore>
    <loadBeforeByVersion><v1.7><li>version.before</li></v1.7></loadBeforeByVersion>
</ModMetaData>";

            var mod = await LoadVersionedModAsync(aboutXml, "1.7");

            mod.ForceLoadBefore.Should().BeEquivalentTo(new[] { "force.before" });
            mod.ForceLoadAfter.Should().BeEquivalentTo(new[] { "force.after" });
            // The versioned non-force rule still applies.
            mod.LoadBefore.Should().BeEquivalentTo(new[] { "version.before" });
        }

        [Theory]
        [InlineData("Unknown")]
        [InlineData("N/A")]
        [InlineData("")]
        public async Task LoadModsAsync_FallbackVersion_MatchesNothing_KeepsPlainRules(string majorVersion)
        {
            var aboutXml = @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>Versioned Mod</name>
    <packageId>test.versioned</packageId>
    <loadBefore><li>plain.before</li></loadBefore>
    <loadBeforeByVersion><v1.7><li>version.before</li></v1.7></loadBeforeByVersion>
</ModMetaData>";

            var mod = await LoadVersionedModAsync(aboutXml, majorVersion);

            mod.LoadBefore.Should().BeEquivalentTo(new[] { "plain.before" });
            mod.LoadBeforeByVersion.Should().BeEmpty();
        }
    }

    public class ModServiceMacOSBundleTests : IDisposable
    {
        private readonly IPathService _mockPathService;
        private readonly IModRulesService _mockRulesService;
        private readonly IModCustomService _mockCustomService;
        private readonly IMlieVersionService _mockMlieVersionService;
        private readonly ILoggerService _mockLogger;
        private readonly string _testTempDir;

        public ModServiceMacOSBundleTests()
        {
            _mockPathService = Substitute.For<IPathService>();
            _mockRulesService = Substitute.For<IModRulesService>();
            _mockCustomService = Substitute.For<IModCustomService>();
            _mockMlieVersionService = Substitute.For<IMlieVersionService>();
            _mockLogger = Substitute.For<ILoggerService>();

            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharp_ModServiceBundleTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        private ModService CreateService(string gamePath, string modsPath, string configPath)
        {
            _mockPathService.GetGamePath().Returns(gamePath);
            _mockPathService.GetModsPath().Returns(modsPath);
            _mockPathService.GetConfigPath().Returns(configPath);
            _mockPathService.GetMajorGameVersion().Returns("1.5");
            // GetGameDataPathFor will be called by LoadCoreMods - set up bundle-aware behavior
            _mockPathService.GetGameDataPathFor(Arg.Any<string>())
                .Returns(callInfo =>
                {
                    var gp = callInfo.Arg<string>();
                    if (string.IsNullOrEmpty(gp)) return string.Empty;
                    var fileName = Path.GetFileName(gp);
                    if (!string.IsNullOrEmpty(fileName) && fileName.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(gp))
                    {
                        // Bundle: check candidates in order
                        var contentsData = Path.Combine(gp, "Contents", "Data");
                        if (Directory.Exists(contentsData)) return contentsData;
                        var contentsResourcesData = Path.Combine(gp, "Contents", "Resources", "Data");
                        if (Directory.Exists(contentsResourcesData)) return contentsResourcesData;
                        var rootData = Path.Combine(gp, "Data");
                        if (Directory.Exists(rootData)) return rootData;
                        return Path.Combine(gp, "Data"); // legacy fallback
                    }
                    return Path.Combine(gp, "Data"); // non-bundle legacy
                });

            return new ModService(_mockPathService, _mockRulesService, _mockCustomService, _mockMlieVersionService, _mockLogger);
        }

        [Fact]
        public async Task LoadModsAsync_BundleWithContentsResourcesData_ShouldDiscoverCoreMod()
        {
            // Arrange: synthetic .app bundle with Data at Contents/Resources/Data (Unity macOS build)
            var bundlePath = Path.Combine(_testTempDir, "RimWorld.app");
            var contentsResourcesData = Path.Combine(bundlePath, "Contents", "Resources", "Data");
            var corePath = Path.Combine(contentsResourcesData, "Core");
            var aboutDir = Path.Combine(corePath, "About");
            Directory.CreateDirectory(aboutDir);
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"),
                @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>Core</name>
    <packageId>Ludeon.RimWorld</packageId>
    <supportedVersions>
        <li>1.5</li>
    </supportedVersions>
</ModMetaData>");

            var modsPath = Path.Combine(_testTempDir, "Mods");
            var configPath = Path.Combine(_testTempDir, "Config");
            Directory.CreateDirectory(modsPath);
            Directory.CreateDirectory(configPath);

            var service = CreateService(bundlePath, modsPath, configPath);

            // Act
            await service.LoadModsAsync();

            // Assert: Core mod should be discovered from Contents/Resources/Data
            var mods = service.GetLoadedMods().ToList();
            var coreMod = mods.SingleOrDefault(m => m.PackageId == "Ludeon.RimWorld" && m.ModType == ModType.Core);
            coreMod.Should().NotBeNull();
            coreMod!.Path.Should().Be(corePath);
        }

        [Fact]
        public async Task LoadModsAsync_BundleWithContentsData_ShouldDiscoverCoreMod()
        {
            // Arrange: synthetic .app bundle with Data at Contents/Data (Waylith macOS build)
            var bundlePath = Path.Combine(_testTempDir, "RimWorld.app");
            var contentsData = Path.Combine(bundlePath, "Contents", "Data");
            var corePath = Path.Combine(contentsData, "Core");
            var aboutDir = Path.Combine(corePath, "About");
            Directory.CreateDirectory(aboutDir);
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"),
                @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>Core</name>
    <packageId>Ludeon.RimWorld</packageId>
    <supportedVersions>
        <li>1.5</li>
    </supportedVersions>
</ModMetaData>");

            var modsPath = Path.Combine(_testTempDir, "Mods");
            var configPath = Path.Combine(_testTempDir, "Config");
            Directory.CreateDirectory(modsPath);
            Directory.CreateDirectory(configPath);

            var service = CreateService(bundlePath, modsPath, configPath);

            // Act
            await service.LoadModsAsync();

            // Assert: Core mod should be discovered from Contents/Data
            var mods = service.GetLoadedMods().ToList();
            var coreMod = mods.SingleOrDefault(m => m.PackageId == "Ludeon.RimWorld" && m.ModType == ModType.Core);
            coreMod.Should().NotBeNull();
            coreMod!.Path.Should().Be(corePath);
        }

        [Fact]
        public async Task LoadModsAsync_BundleWithRootData_ShouldDiscoverCoreMod()
        {
            // Arrange: synthetic .app bundle with Data at bundle root
            var bundlePath = Path.Combine(_testTempDir, "RimWorld.app");
            var rootData = Path.Combine(bundlePath, "Data");
            var corePath = Path.Combine(rootData, "Core");
            var aboutDir = Path.Combine(corePath, "About");
            Directory.CreateDirectory(aboutDir);
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"),
                @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>Core</name>
    <packageId>Ludeon.RimWorld</packageId>
    <supportedVersions>
        <li>1.5</li>
    </supportedVersions>
</ModMetaData>");

            var modsPath = Path.Combine(_testTempDir, "Mods");
            var configPath = Path.Combine(_testTempDir, "Config");
            Directory.CreateDirectory(modsPath);
            Directory.CreateDirectory(configPath);

            var service = CreateService(bundlePath, modsPath, configPath);

            // Act
            await service.LoadModsAsync();

            // Assert: Core mod should be discovered from bundle-root Data
            var mods = service.GetLoadedMods().ToList();
            var coreMod = mods.SingleOrDefault(m => m.PackageId == "Ludeon.RimWorld" && m.ModType == ModType.Core);
            coreMod.Should().NotBeNull();
            coreMod!.Path.Should().Be(corePath);
        }

        [Fact]
        public async Task LoadModsAsync_FlatLayout_ShouldStillWork()
        {
            // Arrange: regular (non-.app) game path with Data at game root
            var gamePath = Path.Combine(_testTempDir, "RimWorld");
            var dataPath = Path.Combine(gamePath, "Data");
            var corePath = Path.Combine(dataPath, "Core");
            var aboutDir = Path.Combine(corePath, "About");
            Directory.CreateDirectory(aboutDir);
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"),
                @"<?xml version='1.0' encoding='utf-8'?>
<ModMetaData>
    <name>Core</name>
    <packageId>Ludeon.RimWorld</packageId>
    <supportedVersions>
        <li>1.5</li>
    </supportedVersions>
</ModMetaData>");

            var modsPath = Path.Combine(_testTempDir, "Mods");
            var configPath = Path.Combine(_testTempDir, "Config");
            Directory.CreateDirectory(modsPath);
            Directory.CreateDirectory(configPath);

            var service = CreateService(gamePath, modsPath, configPath);

            // Act
            await service.LoadModsAsync();

            // Assert: Core mod should be discovered from gameRoot/Data
            var mods = service.GetLoadedMods().ToList();
            var coreMod = mods.SingleOrDefault(m => m.PackageId == "Ludeon.RimWorld" && m.ModType == ModType.Core);
            coreMod.Should().NotBeNull();
            coreMod!.Path.Should().Be(corePath);
        }

    }
}


