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
            _modsJsonPath = Path.Combine(_testTempDir, "Rules", "Custom", "Mods.json");
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
    }
}

