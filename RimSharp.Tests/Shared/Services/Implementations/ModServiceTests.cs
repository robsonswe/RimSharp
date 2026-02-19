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
            // Arrange
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

            // Act
            await service.LoadModsAsync();
            var mods = service.GetLoadedMods().ToList();

            // Assert
            mods.Should().HaveCount(1);
            mods[0].Name.Should().Be("Test Mod Name");
            mods[0].PackageId.Should().Be("test.mod.id");
            mods[0].SupportedVersionStrings.Should().Contain("1.5");
        }

        [Fact]
        public async Task LoadModsAsync_WhenNameMissingInXml_ShouldUseFolderName()
        {
            // Arrange
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

            // Act
            await service.LoadModsAsync();
            var mods = service.GetLoadedMods().ToList();

            // Assert
            mods.Should().HaveCount(1);
            // It should fall back to the folder name because of the ParseAboutXml logic
            mods[0].Name.Should().Be("MyFolderName");
        }

        [Fact]
        public async Task LoadModsAsync_WhenXmlIsMalformed_ShouldSkipMod()
        {
            // Arrange
            var modDir = Path.Combine(_modsPath, "BadMod");
            var aboutDir = Path.Combine(modDir, "About");
            Directory.CreateDirectory(aboutDir);
            
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"), "NOT XML");

            var service = CreateService();

            // Act
            await service.LoadModsAsync();
            var mods = service.GetLoadedMods().ToList();

            // Assert
            mods.Should().BeEmpty();
        }
    }
}
