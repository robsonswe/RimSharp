using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NSubstitute;
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
        private readonly ModReplacementService _service;

        public ModReplacementServiceTests()
        {
            _mockPathService = Substitute.For<IPathService>();
            _mockDataUpdateService = Substitute.For<IDataUpdateService>();
            _mockLogger = Substitute.For<ILoggerService>();

            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_Replace_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);

            _tempDbPath = Path.Combine(_testTempDir, "replacements.json");
            _mockDataUpdateService.GetDataFilePath("replacements.json").Returns(_tempDbPath);

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

        [Fact]
        public void GetAllReplacements_ShouldLoadFromJson()
        {
            // Arrange
            WriteJsonDb(@"{
                ""mods"": {
                    ""rule1"": { ""steamId"": ""123"", ""modId"": ""old.mod"", ""replacementModId"": ""new.mod"" }
                }
            }");

            // Act
            var result = _service.GetAllReplacements();

            // Assert
            result.Should().ContainKey("123");
            result["123"].ModId.Should().Be("old.mod");
        }

        [Fact]
        public void GetReplacementByPackageId_ShouldOnlyMatchNonSteamRules()
        {
            // Arrange
            WriteJsonDb(@"{
                ""mods"": {
                    ""steamRule"": { ""steamId"": ""123"", ""modId"": ""steam.mod"", ""replacementModId"": ""rep1"" },
                    ""nonSteamRule"": { ""steamId"": null, ""modId"": ""nonsteam.mod"", ""replacementModId"": ""rep2"" }
                }
            }");

            // Act
            var resultSteam = _service.GetReplacementByPackageId("steam.mod");
            var resultNonSteam = _service.GetReplacementByPackageId("nonsteam.mod");

            // Assert
            resultSteam.Should().BeNull();
            resultNonSteam.Should().NotBeNull();
            resultNonSteam!.ReplacementModId.Should().Be("rep2");
        }

        [Fact]
        public void LoadFromUseThisInsteadXml_ShouldLoadValidXmls()
        {
            // Arrange
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

            // Act
            var result = _service.GetAllReplacements();

            // Assert
            result.Should().ContainKey("999");
            result["999"].ModId.Should().Be("old.xml.mod");
            result["999"].Source.Should().Be(ReplacementSource.UseThisInstead);
        }

        [Fact]
        public void JsonDb_ShouldHavePriorityOverXml()
        {
             // Arrange
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

            // Act
            var result = _service.GetAllReplacements();

            // Assert
            result.Should().ContainKey("123");
            result["123"].ReplacementModId.Should().Be("json-replacement");
            result["123"].Source.Should().Be(ReplacementSource.Database);
        }
    }
}
