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
    public class ModDictionaryServiceTests : IDisposable
    {
        private readonly IPathService _mockPathService;
        private readonly IDataUpdateService _mockDataUpdateService;
        private readonly ILoggerService _mockLogger;
        private readonly string _tempDbPath;
        private readonly ModDictionaryService _service;

        public ModDictionaryServiceTests()
        {
            _mockPathService = Substitute.For<IPathService>();
            _mockDataUpdateService = Substitute.For<IDataUpdateService>();
            _mockLogger = Substitute.For<ILoggerService>();
            
            _tempDbPath = Path.Combine(Path.GetTempPath(), "RimSharpTests_db_" + Guid.NewGuid().ToString("N") + ".json");
            _mockDataUpdateService.GetDataFilePath("db.json").Returns(_tempDbPath);

            _service = new ModDictionaryService(_mockPathService, _mockDataUpdateService, _mockLogger);
        }

        public void Dispose()
        {
            if (File.Exists(_tempDbPath))
            {
                File.Delete(_tempDbPath);
            }
        }

        private void WriteDbFile(string json)
        {
            File.WriteAllText(_tempDbPath, json);
        }

        [Fact]
        public void GetEntryByPackageId_WhenMultipleMatches_ShouldPrioritizeByVersion()
        {
            // Arrange
            WriteDbFile(@"{
                ""mods"": {
                    ""test.mod"": {
                        ""111"": { ""name"": ""Mod 1.4"", ""versions"": [""1.4""], ""published"": true },
                        ""222"": { ""name"": ""Mod 1.5"", ""versions"": [""1.5""], ""published"": true }
                    }
                }
            }");
            _mockPathService.GetMajorGameVersion().Returns("1.5");

            // Act
            var result = _service.GetEntryByPackageId("test.mod");

            // Assert
            result.Should().NotBeNull();
            result!.SteamId.Should().Be("222");
        }

        [Fact]
        public void GetEntryByPackageId_WhenMultipleMatches_ShouldPrioritizeByPublished()
        {
            // Arrange
            WriteDbFile(@"{
                ""mods"": {
                    ""test.mod"": {
                        ""111"": { ""name"": ""Unpublished"", ""versions"": [""1.5""], ""published"": false },
                        ""222"": { ""name"": ""Published"", ""versions"": [""1.5""], ""published"": true }
                    }
                }
            }");
            _mockPathService.GetMajorGameVersion().Returns("1.5");

            // Act
            var result = _service.GetEntryByPackageId("test.mod");

            // Assert
            result.Should().NotBeNull();
            result!.SteamId.Should().Be("222");
        }

        [Fact]
        public void GetEntryByPackageId_CaseInsensitiveLookup()
        {
            // Arrange
            WriteDbFile(@"{
                ""mods"": {
                    ""Test.Mod"": {
                        ""123"": { ""name"": ""Test Mod"", ""versions"": [""1.5""], ""published"": true }
                    }
                }
            }");

            // Act
            var result = _service.GetEntryByPackageId("test.mod");

            // Assert
            result.Should().NotBeNull();
            result!.PackageId.Should().Be("test.mod"); // Service normalizes to lowercase
        }

        [Fact]
        public void GetEntryBySteamId_ShouldReturnCorrectEntry()
        {
            // Arrange
            WriteDbFile(@"{
                ""mods"": {
                    ""mod1"": { ""123"": { ""name"": ""Mod 123"", ""published"": true } },
                    ""mod2"": { ""456"": { ""name"": ""Mod 456"", ""published"": true } }
                }
            }");

            // Act
            var result = _service.GetEntryBySteamId("456");

            // Assert
            result.Should().NotBeNull();
            result!.Name.Should().Be("Mod 456");
        }
    }
}
