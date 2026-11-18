using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Mods.ModInfo;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;
using Xunit;

namespace RimSharp.Tests.Shared.Services.Implementations
{
    public class ModDictionaryServiceTests : IDisposable
    {
        private readonly IPathService _mockPathService;
        private readonly IDataUpdateService _mockDataUpdateService;
        private readonly ILoggerService _mockLogger;
        private readonly string _tempDbPath;
        private readonly string _steamDbPath;
        private readonly ModDictionaryService _service;

        public ModDictionaryServiceTests()
        {
            _mockPathService = Substitute.For<IPathService>();
            _mockDataUpdateService = Substitute.For<IDataUpdateService>();
            _mockLogger = Substitute.For<ILoggerService>();

            _tempDbPath = Path.Combine(Path.GetTempPath(), "RimSharpTests_db_" + Guid.NewGuid().ToString("N") + ".json");
            _mockDataUpdateService.GetExternalFilePath("ModInfo", "RimSharp", "db.json").Returns(_tempDbPath);

            _steamDbPath = Path.Combine(Path.GetTempPath(), "RimSharpTests_steamdb_" + Guid.NewGuid().ToString("N") + ".json");
            _mockDataUpdateService.GetModInfoSourceFilePath(
                ModInfoSourceRegistry.SteamWorkshopDbSourceId,
                ModInfoSourceRegistry.SteamWorkshopDbSourceUrl).Returns(_steamDbPath);

            _service = new ModDictionaryService(_mockPathService, _mockDataUpdateService, _mockLogger);
        }

        public void Dispose()
        {
            if (File.Exists(_tempDbPath))
            {
                File.Delete(_tempDbPath);
            }
            if (File.Exists(_steamDbPath))
            {
                File.Delete(_steamDbPath);
            }
        }

        private ModDictionaryService CreateServiceWithRegistry()
        {
            var configService = Substitute.For<IConfigService>();
            var registry = new ModInfoSourceRegistry(configService);
            return new ModDictionaryService(_mockPathService, _mockDataUpdateService, _mockLogger, registry);
        }

        private void WriteDbFile(string json)
        {
            File.WriteAllText(_tempDbPath, json);
        }

        [Fact]
        public void GetEntryByPackageId_WhenMultipleMatches_ShouldPrioritizeByVersion()
        {

            WriteDbFile(@"{
                ""mods"": {
                    ""test.mod"": {
                        ""111"": { ""name"": ""Mod 1.4"", ""versions"": [""1.4""], ""published"": true },
                        ""222"": { ""name"": ""Mod 1.5"", ""versions"": [""1.5""], ""published"": true }
                    }
                }
            }");
            _mockPathService.GetMajorGameVersion().Returns("1.5");

            var result = _service.GetEntryByPackageId("test.mod");

            result.Should().NotBeNull();
            result!.SteamId.Should().Be("222");
        }

        [Fact]
        public void GetEntryByPackageId_WhenMultipleMatches_ShouldPrioritizeByPublished()
        {

            WriteDbFile(@"{
                ""mods"": {
                    ""test.mod"": {
                        ""111"": { ""name"": ""Unpublished"", ""versions"": [""1.5""], ""published"": false },
                        ""222"": { ""name"": ""Published"", ""versions"": [""1.5""], ""published"": true }
                    }
                }
            }");
            _mockPathService.GetMajorGameVersion().Returns("1.5");

            var result = _service.GetEntryByPackageId("test.mod");

            result.Should().NotBeNull();
            result!.SteamId.Should().Be("222");
        }

        [Fact]
        public void GetEntryByPackageId_CaseInsensitiveLookup()
        {

            WriteDbFile(@"{
                ""mods"": {
                    ""Test.Mod"": {
                        ""123"": { ""name"": ""Test Mod"", ""versions"": [""1.5""], ""published"": true }
                    }
                }
            }");

            var result = _service.GetEntryByPackageId("test.mod");

            result.Should().NotBeNull();
            result!.PackageId.Should().Be("test.mod"); // Service normalizes to lowercase
        }

        [Fact]
        public void GetEntryBySteamId_ShouldReturnCorrectEntry()
        {

            WriteDbFile(@"{
                ""mods"": {
                    ""mod1"": { ""123"": { ""name"": ""Mod 123"", ""published"": true } },
                    ""mod2"": { ""456"": { ""name"": ""Mod 456"", ""published"": true } }
                }
            }");

            var result = _service.GetEntryBySteamId("456");

            result.Should().NotBeNull();
            result!.Name.Should().Be("Mod 456");
        }

        [Fact]
        public void GetAllEntries_SteamWorkshopDbShape_ShouldMapFields()
        {
            File.WriteAllText(_steamDbPath, @"{
                ""version"": 1790337009,
                ""123"": {
                    ""packageId"": ""Mod.A"",
                    ""name"": ""Mod A"",
                    ""authors"": [""Alice"", ""Bob""],
                    ""gameVersions"": [""1.5"", ""1.6""],
                    ""unpublished"": false
                },
                ""456"": {
                    ""packageid"": ""mod.b"",
                    ""name"": ""Mod B"",
                    ""authors"": ""Carol"",
                    ""gameVersions"": ""1.6"",
                    ""unpublished"": true
                },
                ""294100"": {
                    ""appid"": true,
                    ""packageid"": ""ludeon.rimworld"",
                    ""packageId"": ""ludeon.rimworld"",
                    ""name"": ""RimWorld""
                }
            }");
            var service = CreateServiceWithRegistry();

            var entries = service.GetAllEntries();

            entries.Should().HaveCount(3);
            var a = entries["123"];
            a.PackageId.Should().Be("mod.a");
            a.SteamId.Should().Be("123");
            a.Name.Should().Be("Mod A");
            a.Authors.Should().Be("Alice, Bob");
            a.Versions.Should().Equal("1.5", "1.6");
            a.Published.Should().BeTrue();

            var b = entries["456"];
            b.PackageId.Should().Be("mod.b");
            b.Authors.Should().Be("Carol");
            b.Versions.Should().Equal("1.6");
            b.Published.Should().BeFalse("unpublished=true maps to Published=false");

            var dlc = entries["294100"];
            dlc.PackageId.Should().Be("ludeon.rimworld", "case-insensitive packageId lookup handles DLC entries");
            dlc.Published.Should().BeTrue("unpublished absent defaults to published");
        }

        [Fact]
        public void GetAllEntries_WhenBothSourcesHaveSameSteamId_CuratedWins()
        {
            WriteDbFile(@"{
                ""mods"": {
                    ""curated.pkg"": {
                        ""123"": { ""name"": ""Curated Name"", ""published"": true }
                    }
                }
            }");
            File.WriteAllText(_steamDbPath, @"{
                ""123"": { ""packageId"": ""steam.pkg"", ""name"": ""Steam Name"" }
            }");
            var service = CreateServiceWithRegistry();

            var entries = service.GetAllEntries();

            entries.Should().HaveCount(1, "first-source-wins keeps a single entry per steam id");
            var entry = entries["123"];
            entry.Name.Should().Be("Curated Name", "the curated source is iterated first and keeps priority");
            entry.PackageId.Should().Be("curated.pkg");
        }
    }
}

