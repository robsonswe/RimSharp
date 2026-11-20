using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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

        private ModDictionaryService CreateServiceWithConfiguredOrder(params string[] sourceIds)
        {
            var configService = Substitute.For<IConfigService>();
            configService.GetConfigValue(ModInfoSourceRegistry.PriorityOrderConfigKey)
                .Returns(JsonSerializer.Serialize(sourceIds));
            var registry = new ModInfoSourceRegistry(configService);
            return new ModDictionaryService(_mockPathService, _mockDataUpdateService, _mockLogger, registry);
        }

        private void WriteDbFile(string json)
        {
            File.WriteAllText(_tempDbPath, json);
        }

        private void WriteSteamDbFile(string json)
        {
            File.WriteAllText(_steamDbPath, json);
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
                ""database"": {
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
                }
            }");
            var service = CreateServiceWithRegistry();

            var entries = service.GetAllEntries();

            entries.Should().NotContainKey("database", "the 'database' wrapper must not become a bogus entry");
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
                ""database"": {
                    ""123"": { ""packageId"": ""steam.pkg"", ""name"": ""Steam Name"" }
                }
            }");
            var service = CreateServiceWithRegistry();

            var entries = service.GetAllEntries();

            entries.Should().NotContainKey("database", "the 'database' wrapper must not become a bogus entry");
            entries.Should().HaveCount(1, "first-source-wins keeps a single entry per steam id");
            var entry = entries["123"];
            entry.Name.Should().Be("Curated Name", "the curated source is iterated first and keeps priority");
            entry.PackageId.Should().Be("curated.pkg");
        }

        [Fact]
        public void GetAllEntries_SteamWorkshopDb_MapsSteamNameAndAboutName()
        {
            WriteSteamDbFile(@"{
                ""database"": {
                    ""123"": {
                        ""packageId"": ""Mod.A"",
                        ""steamName"": ""Workshop Title"",
                        ""name"": ""About Title""
                    }
                }
            }");
            var service = CreateServiceWithRegistry();

            var entry = service.GetAllEntries()["123"];

            entry.SteamName.Should().Be("Workshop Title");
            entry.AboutName.Should().Be("About Title");
            entry.Name.Should().Be("Workshop Title", "the workshop title is the preferred display name");
            entry.SourceId.Should().Be(ModInfoSourceRegistry.SteamWorkshopDbSourceId);
        }

        [Fact]
        public void GetAllEntries_Curated_MapsNameToSteamName()
        {
            WriteDbFile(@"{
                ""mods"": {
                    ""mod.a"": { ""123"": { ""name"": ""Curated Title"", ""published"": true } }
                }
            }");
            var service = CreateServiceWithRegistry();

            var entry = service.GetAllEntries()["123"];

            entry.SteamName.Should().Be("Curated Title", "curated `name` is workshop-title lineage");
            entry.AboutName.Should().BeEmpty();
            entry.Name.Should().Be("Curated Title");
            entry.SourceId.Should().Be(ModInfoSourceRegistry.CuratedSourceId);
        }

        [Fact]
        public void GetAllEntries_DefaultOrder_CuratedOwnsAndSteamDbFillsEmptyFields()
        {
            WriteDbFile(@"{
                ""mods"": {
                    ""curated.pkg"": {
                        ""123"": {
                            ""name"": ""Curated Title"",
                            ""versions"": [""1.4""],
                            ""authors"": ""Curated Author"",
                            ""published"": true
                        }
                    }
                }
            }");
            WriteSteamDbFile(@"{
                ""database"": {
                    ""123"": {
                        ""packageId"": ""steam.pkg"",
                        ""steamName"": ""Steam Title"",
                        ""name"": ""About Title"",
                        ""gameVersions"": [""1.5""],
                        ""authors"": ""Steam Author"",
                        ""unpublished"": true
                    }
                }
            }");
            var service = CreateServiceWithRegistry();

            var entry = service.GetAllEntries()["123"];

            entry.SteamName.Should().Be("Curated Title", "the first source owns SteamName");
            entry.AboutName.Should().Be("About Title", "a later source fills the empty AboutName");
            entry.PackageId.Should().Be("curated.pkg");
            entry.Authors.Should().Be("Curated Author");
            entry.Versions.Should().Equal(new[] { "1.4", "1.5" }, "missing version items are filled");
            entry.Published.Should().BeTrue("Published is first-writer-wins despite steamdb's unpublished=true");
            entry.SourceId.Should().Be(ModInfoSourceRegistry.CuratedSourceId);
        }

        [Fact]
        public void GetAllEntries_ConfiguredOrder_SteamDbOwnsAndCuratedFillsSteamName()
        {
            WriteSteamDbFile(@"{
                ""database"": {
                    ""123"": {
                        ""packageId"": ""steam.pkg"",
                        ""name"": ""About Title"",
                        ""unpublished"": true
                    }
                }
            }");
            WriteDbFile(@"{
                ""mods"": {
                    ""curated.pkg"": { ""123"": { ""name"": ""Curated Title"", ""published"": true } }
                }
            }");
            var service = CreateServiceWithConfiguredOrder(
                ModInfoSourceRegistry.SteamWorkshopDbSourceId,
                ModInfoSourceRegistry.CuratedSourceId);

            var entry = service.GetAllEntries()["123"];

            entry.SteamName.Should().Be("Curated Title", "the curated name fills steamdb's missing steamName");
            entry.AboutName.Should().Be("About Title");
            entry.PackageId.Should().Be("steam.pkg", "steamdb is first in the configured order and owns the entry");
            entry.Published.Should().BeFalse("the first writer's unpublished=true is not overridden");
            entry.SourceId.Should().Be(ModInfoSourceRegistry.SteamWorkshopDbSourceId);
        }

        [Fact]
        public void GetAllEntriesByPackageId_CaseInsensitiveReverseIndex()
        {
            WriteDbFile(@"{
                ""mods"": {
                    ""Test.Mod"": { ""123"": { ""name"": ""Test Mod"", ""published"": true } }
                }
            }");
            var service = CreateServiceWithRegistry();

            service.GetAllEntriesByPackageId("test.mod").Should().ContainSingle()
                .Which.SteamId.Should().Be("123");
            service.GetAllEntriesByPackageId("TEST.MOD").Should().ContainSingle()
                .Which.SteamId.Should().Be("123");
        }

        [Fact]
        public void GetEntryByPackageId_DefaultOrder_CuratedCandidateWins()
        {
            WriteDbFile(@"{
                ""mods"": {
                    ""shared.pkg"": {
                        ""111"": { ""name"": ""Curated"", ""versions"": [""1.5""], ""published"": true }
                    }
                }
            }");
            WriteSteamDbFile(@"{
                ""database"": {
                    ""222"": { ""packageId"": ""shared.pkg"", ""steamName"": ""Steam"", ""gameVersions"": [""1.5""] }
                }
            }");
            _mockPathService.GetMajorGameVersion().Returns("1.5");
            var service = CreateServiceWithRegistry();

            service.GetEntryByPackageId("shared.pkg")!.SteamId.Should().Be("111",
                "with equal version/published the higher-priority (curated) source wins");
        }

        [Fact]
        public void GetEntryByPackageId_ConfiguredOrder_SteamDbCandidateWins()
        {
            WriteDbFile(@"{
                ""mods"": {
                    ""shared.pkg"": {
                        ""111"": { ""name"": ""Curated"", ""versions"": [""1.5""], ""published"": true }
                    }
                }
            }");
            WriteSteamDbFile(@"{
                ""database"": {
                    ""222"": { ""packageId"": ""shared.pkg"", ""steamName"": ""Steam"", ""gameVersions"": [""1.5""] }
                }
            }");
            _mockPathService.GetMajorGameVersion().Returns("1.5");
            var service = CreateServiceWithConfiguredOrder(
                ModInfoSourceRegistry.SteamWorkshopDbSourceId,
                ModInfoSourceRegistry.CuratedSourceId);

            service.GetEntryByPackageId("shared.pkg")!.SteamId.Should().Be("222",
                "the configured order puts steamdb first, so its candidate wins the tie");
        }

        [Fact]
        public async Task GetAllEntries_ConcurrentCalls_AreSafeAndShareSingleCache()
        {
            WriteDbFile(@"{""mods"": { ""mod.a"": { ""123"": { ""name"": ""Mod A"", ""published"": true } } } }");

            const int callCount = 16;
            var tasks = Enumerable.Range(0, callCount)
                .Select(_ => Task.Run(() => _service.GetAllEntries()))
                .ToArray();

            var results = await Task.WhenAll(tasks);

            results.Should().OnlyContain(entries => ReferenceEquals(entries, results[0]),
                "all concurrent callers should observe the single cached dictionary instance");
            results[0].Should().ContainKey("123");
            results[0]["123"].Name.Should().Be("Mod A");
        }

        [Fact]
        public async Task ConcurrentInvalidateAndLookups_NeverThrow()
        {
            WriteDbFile(@"{""mods"": { ""mod.a"": { ""123"": { ""name"": ""Mod A"", ""published"": true } } } }");

            // Bounded work plus the test framework's cancellation token so the
            // test can never hang the suite.
            var token = TestContext.Current.CancellationToken;
            const int iterations = 1000;

            var invalidator = Task.Run(() =>
            {
                for (int i = 0; i < iterations && !token.IsCancellationRequested; i++)
                {
                    _service.Invalidate();
                }
            }, token);

            var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < iterations && !token.IsCancellationRequested; i++)
                {
                    _service.GetAllEntries();
                    _service.GetEntryByPackageId("mod.a");
                    _service.GetAllEntriesByPackageId("mod.a");
                    _service.GetEntryBySteamId("123");
                }
            }, token)).ToArray();

            // Task.WhenAll surfaces any exception from any worker as a test failure.
            await Task.WhenAll(readers.Append(invalidator));
        }

        [Fact]
        public void GetAllEntries_SteamWorkshopDb_IgnoresUnknownAndMalformedProperties()
        {
            WriteSteamDbFile(@"{""database"": {
                ""123"": {
                    ""unknown"": { ""nested"": true },
                    ""packageId"": ""mod.a"",
                    ""steamName"": ""Title"",
                    ""name"": ""About"",
                    ""gameVersions"": [""1.5"", 7, ""1.6""],
                    ""authors"": [""Alice"", null, ""Bob""],
                    ""unpublished"": ""yes""
                }
            }}");
            var service = CreateServiceWithRegistry();

            var entry = service.GetAllEntries()["123"];

            entry.PackageId.Should().Be("mod.a");
            entry.SteamName.Should().Be("Title");
            entry.AboutName.Should().Be("About");
            entry.Versions.Should().Equal(new[] { "1.5", "1.6" }, "non-string array items are ignored");
            entry.Authors.Should().Be("Alice, Bob", "non-string array items are ignored");
            entry.Published.Should().BeTrue("a non-boolean 'unpublished' leaves the default true");
        }
    }
}

