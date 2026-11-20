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
    public class ModCustomServiceTests : IDisposable
    {
        private readonly ILoggerService _mockLogger;
        private readonly string _testTempDir;
        private readonly string _modsJsonPath;

        public ModCustomServiceTests()
        {
            _mockLogger = Substitute.For<ILoggerService>();
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_Custom_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);
            _modsJsonPath = Path.Combine(_testTempDir, "Data", "Custom", "Mods.json");
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        [Fact]
        public async Task SaveCustomModInfoAsync_ShouldCreateFile()
        {

            var service = new ModCustomService(_testTempDir, _mockLogger);
            var info = new ModCustomInfo { Favorite = true, Tags = "CustomTag" };

            await service.SaveCustomModInfoAsync("test.mod", info);

            File.Exists(_modsJsonPath).Should().BeTrue();
            string content = File.ReadAllText(_modsJsonPath);
            content.Should().Contain("test.mod");
            content.Should().Contain("CustomTag");
        }

        [Fact]
        public async Task GetCustomModInfo_ShouldReturnCorrectInfo()
        {

            var service = new ModCustomService(_testTempDir, _mockLogger);
            var info = new ModCustomInfo { Favorite = true };
            await service.SaveCustomModInfoAsync("test.mod", info);

            var result = service.GetCustomModInfo("TEST.MOD"); // Case insensitive

            result.Should().NotBeNull();
            result!.Favorite.Should().BeTrue();
        }

        [Fact]
        public async Task ApplyCustomInfoToMods_ShouldUpdateModItems()
        {

            var service = new ModCustomService(_testTempDir, _mockLogger);
            var info = new ModCustomInfo { Favorite = true, Tags = "NewTag", ExternalUrl = "http://example.com" };
            await service.SaveCustomModInfoAsync("test.mod", info);

            var mod = new ModItem { PackageId = "test.mod", Name = "Test Mod" };

            service.ApplyCustomInfoToMods(new[] { mod });

            mod.IsFavorite.Should().BeTrue();
            mod.Tags.Should().Be("NewTag");
            mod.ExternalUrl.Should().Be("http://example.com");
        }

        [Fact]
        public async Task ApplyCustomInfoToMods_MergesSteamTagsWithCustomTags_CaseInsensitiveAndIdempotent()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            var info = new ModCustomInfo { Tags = "translation, must have" };
            await service.SaveCustomModInfoAsync("test.mod", info);

            // Simulate the post-parse state: ModService seeded Tags from SteamTags.
            var mod = new ModItem { PackageId = "test.mod", Name = "Test Mod" };
            mod.SteamTags.Add("Translation");
            mod.Tags = string.Join(", ", mod.SteamTags); // "Translation"

            service.ApplyCustomInfoToMods(new[] { mod });
            // "translation" dedupes against the Steam "Translation" (case-insensitive);
            // the user's extra tag is appended.
            mod.Tags.Should().Be("Translation, must have");

            // Repeated application is stable — no duplication, no drift.
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.Tags.Should().Be("Translation, must have");
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.Tags.Should().Be("Translation, must have");
        }

        [Fact]
        public async Task ApplyCustomInfoToMods_UserTagsOnly_WhenNoSteamTags_Unchanged()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            var info = new ModCustomInfo { Tags = "gameplay, qol" };
            await service.SaveCustomModInfoAsync("test.mod", info);

            var mod = new ModItem { PackageId = "test.mod", Name = "Test Mod" };

            service.ApplyCustomInfoToMods(new[] { mod });

            mod.Tags.Should().Be("gameplay, qol");
        }

        [Fact]
        public async Task RemoveCustomModInfoAsync_ShouldUpdateFile()
        {

            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo { Favorite = true });

            await service.RemoveCustomModInfoAsync("test.mod");

            var result = service.GetCustomModInfo("test.mod");
            result.Should().BeNull();
            string content = File.ReadAllText(_modsJsonPath);
            content.Should().NotContain("test.mod");
        }

        [Fact]
        public async Task ApplyCustomInfoToMods_ShouldApplyLoadTop()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            var info = new ModCustomInfo { LoadTop = new LoadBottomRule { Value = true } };
            await service.SaveCustomModInfoAsync("test.mod", info);

            var mod = new ModItem { PackageId = "test.mod" };

            service.ApplyCustomInfoToMods(new[] { mod });

            mod.LoadTop.Should().BeTrue();
        }

        [Fact]
        public async Task SaveCustomModInfoAsync_ShouldRoundTripLoadTop()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            var info = new ModCustomInfo
            {
                LoadTop = new LoadBottomRule { Value = true, Comment = new List<string> { "tier 1 please" } }
            };

            await service.SaveCustomModInfoAsync("test.mod", info);

            var reloaded = service.GetCustomModInfo("test.mod");
            reloaded!.LoadTop.Should().NotBeNull();
            reloaded.LoadTop!.Value.Should().BeTrue();
            reloaded.LoadTop.Comment.Should().Contain("tier 1 please");
        }

        [Fact]
        public async Task SaveCustomModInfoAsync_WhenLoadTopFalse_ShouldCleanItUp()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                LoadTop = new LoadBottomRule { Value = false }
            });

            service.GetCustomModInfo("test.mod")!.LoadTop.Should().BeNull();
        }

        // --- Add/remove asymmetry regressions --------------------------------

        [Fact]
        public async Task ApplyCustomInfoToMods_RemovedVersion_StripsCustomVersionFromLiveMod()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                SupportedVersions = new List<string> { "1.6" }
            });

            var mod = new ModItem { PackageId = "test.mod" };
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.SupportedVersions.Should().ContainSingle(v => v.Source == VersionSource.Custom && v.Version == "1.6");

            // Save an empty record and re-apply to the SAME (folder-scan-cached) ModItem.
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.SupportedVersions.Should().NotContain(v => v.Source == VersionSource.Custom);
        }

        [Fact]
        public async Task ApplyCustomInfoToMods_RemovedTag_StripsOnlyTrackedTagFromLiveMod()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo { Tags = "qol" });

            var mod = new ModItem { PackageId = "test.mod", Tags = "base" };
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.TagList.Should().Contain("qol");

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.TagList.Should().NotContain("qol");
            mod.TagList.Should().Contain("base");
        }

        [Fact]
        public async Task ApplyCustomInfoToMods_RemovedLoadBefore_StripsTargetFromLiveMod()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                LoadBefore = new Dictionary<string, ModDependencyRule> { ["other.mod"] = new ModDependencyRule() }
            });

            var mod = new ModItem { PackageId = "test.mod" };
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.LoadBefore.Should().Contain("other.mod");

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.LoadBefore.Should().NotContain("other.mod");
        }

        [Fact]
        public async Task ApplyCustomInfoToMods_RemovedIncompatibility_StripsKeyFromLiveMod()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                IncompatibleWith = new Dictionary<string, ModIncompatibilityRule> { ["bad.mod"] = new ModIncompatibilityRule() }
            });

            var mod = new ModItem { PackageId = "test.mod" };
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.IncompatibleWith.Should().ContainKey("bad.mod");

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.IncompatibleWith.Should().NotContainKey("bad.mod");
        }

        [Fact]
        public async Task ApplyCustomInfoToMods_RemovedExternalUrl_ClearsUrlOnLiveMod()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo { ExternalUrl = "http://example.com" });

            var mod = new ModItem { PackageId = "test.mod" };
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.ExternalUrl.Should().Be("http://example.com");

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.ExternalUrl.Should().BeEmpty();
        }

        [Fact]
        public async Task ApplyCustomInfoToMods_RemovedLoadTopAndBottom_ResetsFlagsOnLiveMod()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                LoadTop = new LoadBottomRule { Value = true },
                LoadBottom = new LoadBottomRule { Value = true }
            });

            var mod = new ModItem { PackageId = "test.mod" };
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.LoadTop.Should().BeTrue();
            mod.LoadBottom.Should().BeTrue();

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.LoadTop.Should().BeFalse();
            mod.LoadBottom.Should().BeFalse();
        }

        [Fact]
        public async Task ApplyCustomInfoToMods_RepeatedApply_LeavesExactlyOneOfEachValue()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                SupportedVersions = new List<string> { "1.6" },
                Tags = "qol",
                LoadBefore = new Dictionary<string, ModDependencyRule> { ["other.mod"] = new ModDependencyRule() },
                LoadAfter = new Dictionary<string, ModDependencyRule> { ["third.mod"] = new ModDependencyRule() },
                IncompatibleWith = new Dictionary<string, ModIncompatibilityRule> { ["bad.mod"] = new ModIncompatibilityRule() },
                ExternalUrl = "http://example.com",
                LoadTop = new LoadBottomRule { Value = true }
            });

            var mod = new ModItem { PackageId = "test.mod", Tags = "base" };
            service.ApplyCustomInfoToMods(new[] { mod });
            service.ApplyCustomInfoToMods(new[] { mod });
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.SupportedVersions.Count(v => v.Source == VersionSource.Custom && v.Version == "1.6").Should().Be(1);
            mod.TagList.Count(t => string.Equals(t, "qol", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
            mod.LoadBefore.Count(t => string.Equals(t, "other.mod", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
            mod.LoadAfter.Count(t => string.Equals(t, "third.mod", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
            mod.IncompatibleWith.Keys.Count(k => string.Equals(k, "bad.mod", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
            mod.ExternalUrl.Should().Be("http://example.com");
            mod.LoadTop.Should().BeTrue();
        }

        [Fact]
        public async Task RemoveCustomModInfoAsync_ThenApply_StripsValuesFromLiveMod()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                Tags = "qol",
                ExternalUrl = "http://example.com"
            });

            var mod = new ModItem { PackageId = "test.mod", Tags = "base" };
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.TagList.Should().Contain("qol");

            await service.RemoveCustomModInfoAsync("test.mod");
            // Simulates the next load's re-apply (ModService.RemoveCustomModInfoAsync
            // -> LoadModsAsync -> ApplyCustomInfoToMods) on the cached ModItem.
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.TagList.Should().NotContain("qol");
            mod.TagList.Should().Contain("base");
            mod.ExternalUrl.Should().BeEmpty();
        }

        // --- User-concern regressions ---------------------------------------

        [Fact]
        public async Task Removal_WhenValueCameFromDiskAtStartup_StripsOnSameMod()
        {
            // Session 1: write custom info to Mods.json (simulates a previous
            // session that added the customization).
            var writer = new ModCustomService(_testTempDir, _mockLogger);
            await writer.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                SupportedVersions = new List<string> { "1.7" },
                Tags = "qol"
            });

            // Session 2: a NEW service simulates an app restart — fresh
            // _lastApplied, _isInitialized=false, reads Mods.json from disk on
            // Initialize. The value was NOT added through this instance.
            var service = new ModCustomService(_testTempDir, _mockLogger);
            var mod = new ModItem { PackageId = "test.mod" };

            // Startup apply: values come from disk.
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.SupportedVersions.Should().Contain(v => v.Source == VersionSource.Custom && v.Version == "1.7");
            mod.TagList.Should().Contain("qol");

            // User removes the customization; save empty info and re-apply on
            // the SAME (folder-scan-cached) ModItem.
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.SupportedVersions.Should().NotContain(v => v.Source == VersionSource.Custom && v.Version == "1.7");
            mod.TagList.Should().NotContain("qol");
        }

        [Fact]
        public async Task Removal_WhenCustomVersionMatchesOfficial_KeepsOfficialVersion()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            var mod = new ModItem { PackageId = "test.mod" };
            mod.SupportedVersions.Add(new VersionSupport("1.7", VersionSource.Official));

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                SupportedVersions = new List<string> { "1.7" }
            });
            service.ApplyCustomInfoToMods(new[] { mod });

            // Custom "1.7" dedupes against the Official "1.7", so nothing is
            // tracked for stripping.
            mod.SupportedVersions.Should().ContainSingle(v => v.Source == VersionSource.Official && v.Version == "1.7");

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.SupportedVersions.Should().ContainSingle(v => v.Source == VersionSource.Official && v.Version == "1.7");
        }

        [Fact]
        public async Task Removal_WhenCustomTagMatchesBaseTag_KeepsBaseTag()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            var mod = new ModItem { PackageId = "test.mod", Tags = "qol" };

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo { Tags = "qol" });
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.TagList.Should().ContainSingle(t => string.Equals(t, "qol", StringComparison.OrdinalIgnoreCase));

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.TagList.Should().ContainSingle(t => string.Equals(t, "qol", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Removal_WhenCustomLoadBeforeMatchesBase_KeepsBaseValue()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            var mod = new ModItem { PackageId = "test.mod" };
            mod.LoadBefore.Add("other.mod");
            mod.AboutLoadBefore.Add("other.mod");

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                LoadBefore = new Dictionary<string, ModDependencyRule> { ["other.mod"] = new ModDependencyRule() }
            });
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.LoadBefore.Should().Contain("other.mod");

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.LoadBefore.Should().Contain("other.mod");
            mod.AboutLoadBefore.Should().Contain("other.mod");
        }

        [Fact]
        public async Task Removal_WhenCustomIncompatibilityMatchesBase_KeepsBaseValue()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            var mod = new ModItem { PackageId = "test.mod" };
            mod.IncompatibleWith["bad.mod"] = new ModIncompatibilityRule();
            mod.AboutIncompatibleWith.Add("bad.mod");

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                IncompatibleWith = new Dictionary<string, ModIncompatibilityRule> { ["bad.mod"] = new ModIncompatibilityRule() }
            });
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.IncompatibleWith.Should().ContainKey("bad.mod");

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.IncompatibleWith.Should().ContainKey("bad.mod");
            mod.AboutIncompatibleWith.Should().Contain("bad.mod");
        }

        [Fact]
        public async Task Removal_WhenCustomLoadBottomMatchesRuleValue_RestoresPreCustomTrue()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            // A rules layer set LoadBottom before the custom layer ran.
            var mod = new ModItem { PackageId = "test.mod", LoadBottom = true };

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                LoadBottom = new LoadBottomRule { Value = true }
            });
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.LoadBottom.Should().BeTrue();

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });

            // The pre-custom snapshot (true) is restored, not blindly cleared.
            mod.LoadBottom.Should().BeTrue();
        }

        [Fact]
        public async Task Removal_WhenCustomLoadTopMatchesRuleValue_RestoresPreCustomTrue()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            var mod = new ModItem { PackageId = "test.mod", LoadTop = true };

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                LoadTop = new LoadBottomRule { Value = true }
            });
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.LoadTop.Should().BeTrue();

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.LoadTop.Should().BeTrue();
        }

        [Fact]
        public async Task Removal_DoesNotClearExternalUrl_WhenAnotherWriterChangedItAfterApply()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo { ExternalUrl = "http://custom.example" });

            var mod = new ModItem { PackageId = "test.mod" };
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.ExternalUrl.Should().Be("http://custom.example");

            // Another layer overwrites the URL after the custom layer applied.
            mod.ExternalUrl = "http://workshop.example";

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });

            mod.ExternalUrl.Should().Be("http://workshop.example");
        }

        [Fact]
        public async Task RemoveCustomModInfoAsync_StripsLiveModImmediately_WithoutFollowingApply()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                Tags = "qol",
                ExternalUrl = "http://example.com",
                SupportedVersions = new List<string> { "1.7" }
            });

            var mod = new ModItem { PackageId = "test.mod", Tags = "base" };
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.TagList.Should().Contain("qol");
            mod.ExternalUrl.Should().Be("http://example.com");
            mod.SupportedVersions.Should().Contain(v => v.Source == VersionSource.Custom && v.Version == "1.7");

            // Direct removal with NO subsequent ApplyCustomInfoToMods call —
            // the hole that direct IModCustomService callers used to hit.
            await service.RemoveCustomModInfoAsync("test.mod");

            mod.TagList.Should().NotContain("qol");
            mod.TagList.Should().Contain("base");
            mod.ExternalUrl.Should().BeEmpty();
            mod.SupportedVersions.Should().NotContain(v => v.Source == VersionSource.Custom);
        }
    }
}

