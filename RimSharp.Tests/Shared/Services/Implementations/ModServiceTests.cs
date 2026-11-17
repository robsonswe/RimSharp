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


