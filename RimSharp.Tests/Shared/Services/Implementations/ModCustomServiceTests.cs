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
            // Arrange
            var service = new ModCustomService(_testTempDir, _mockLogger);
            var info = new ModCustomInfo { Favorite = true, Tags = "CustomTag" };

            // Act
            await service.SaveCustomModInfoAsync("test.mod", info);

            // Assert
            File.Exists(_modsJsonPath).Should().BeTrue();
            string content = File.ReadAllText(_modsJsonPath);
            content.Should().Contain("test.mod");
            content.Should().Contain("CustomTag");
        }

        [Fact]
        public async Task GetCustomModInfo_ShouldReturnCorrectInfo()
        {
            // Arrange
            var service = new ModCustomService(_testTempDir, _mockLogger);
            var info = new ModCustomInfo { Favorite = true };
            await service.SaveCustomModInfoAsync("test.mod", info);

            // Act
            var result = service.GetCustomModInfo("TEST.MOD"); // Case insensitive

            // Assert
            result.Should().NotBeNull();
            result.Favorite.Should().BeTrue();
        }

        [Fact]
        public async Task ApplyCustomInfoToMods_ShouldUpdateModItems()
        {
            // Arrange
            var service = new ModCustomService(_testTempDir, _mockLogger);
            var info = new ModCustomInfo { Favorite = true, Tags = "NewTag", ExternalUrl = "http://example.com" };
            await service.SaveCustomModInfoAsync("test.mod", info);

            var mod = new ModItem { PackageId = "test.mod", Name = "Test Mod" };

            // Act
            service.ApplyCustomInfoToMods(new[] { mod });

            // Assert
            mod.IsFavorite.Should().BeTrue();
            mod.Tags.Should().Be("NewTag");
            mod.ExternalUrl.Should().Be("http://example.com");
        }

        [Fact]
        public async Task RemoveCustomModInfoAsync_ShouldUpdateFile()
        {
            // Arrange
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo { Favorite = true });

            // Act
            await service.RemoveCustomModInfoAsync("test.mod");

            // Assert
            var result = service.GetCustomModInfo("test.mod");
            result.Should().BeNull();
            string content = File.ReadAllText(_modsJsonPath);
            content.Should().NotContain("test.mod");
        }
    }
}
