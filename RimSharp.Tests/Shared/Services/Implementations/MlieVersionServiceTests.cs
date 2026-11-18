using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Mods.Versions;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;
using Xunit;

namespace RimSharp.Tests.Shared.Services.Implementations
{
    public class MlieVersionServiceTests : IDisposable
    {
        private readonly IPathService _mockPathService;
        private readonly IDataUpdateService _mockDataUpdateService;
        private readonly ILoggerService _mockLogger;
        private readonly string _testTempDir;
        private readonly string _mlieModPath;
        private readonly string _snapshotRoot;

        public MlieVersionServiceTests()
        {
            _mockPathService = Substitute.For<IPathService>();
            _mockDataUpdateService = Substitute.For<IDataUpdateService>();
            _mockLogger = Substitute.For<ILoggerService>();

            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_Mlie_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);

            // Path expected by MlieVersionService
            _mlieModPath = Path.Combine(_testTempDir, "2599504692");
            Directory.CreateDirectory(_mlieModPath);

            // Snapshot root expected by MlieVersionService:
            // GetExternalFilePath("Versions", "NoVersionWarning", "ModIdsToFix.xml")
            _snapshotRoot = Path.Combine(_testTempDir, "snapshot");
            _mockDataUpdateService
                .GetExternalFilePath("Versions", "NoVersionWarning", "ModIdsToFix.xml")
                .Returns(Path.Combine(_snapshotRoot, "ModIdsToFix.xml"));

            _mockPathService.GetModsPath().Returns(_testTempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        private void CreateMlieVersionFile(string version, string xmlContent)
        {
            CreateVersionFile(_mlieModPath, version, xmlContent);
        }

        private static void CreateVersionFile(string root, string version, string xmlContent)
        {
            var versionDir = Path.Combine(root, version);
            Directory.CreateDirectory(versionDir);
            File.WriteAllText(Path.Combine(versionDir, "ModIdsToFix.xml"), xmlContent);
        }

        private static NoVersionWarningSourceRegistry CreateRegistry(params ModRuleSourceRecord[] sources)
        {
            var configService = Substitute.For<IConfigService>();
            configService.GetConfigValue(Arg.Any<string>()).Returns((string?)null);
            var registry = new NoVersionWarningSourceRegistry(configService);
            registry.ReplaceAll(sources);
            return registry;
        }

        private static ModRuleSourceRecord LocalSource(string id, string name, string localPath) => new()
        {
            Id = id,
            Name = name,
            Type = ModRuleSourceType.LocalFile,
            LocalPath = localPath,
            Enabled = true
        };

        [Fact]
        public void GetMlieVersions_ShouldLoadVersionsFromXml()
        {

            var xml14 = @"
<ModIds>
    <li>test.mod.one</li>
    <li>test.mod.two</li>
</ModIds>";
            var xml15 = @"
<ModIds>
    <li>test.mod.one</li>
    <li>test.mod.three</li>
</ModIds>";

            CreateMlieVersionFile("1.4", xml14);
            CreateMlieVersionFile("1.5", xml15);

            var service = new MlieVersionService(_mockPathService, _mockDataUpdateService, _mockLogger);

            var result = service.GetMlieVersions();

            result.Should().ContainKey("test.mod.one");
            result["test.mod.one"].Should().HaveCount(2).And.Contain(new[] { "1.4", "1.5" });
            
            result.Should().ContainKey("test.mod.two");
            result["test.mod.two"].Should().ContainSingle().Which.Should().Be("1.4");

            result.Should().ContainKey("test.mod.three");
            result["test.mod.three"].Should().ContainSingle().Which.Should().Be("1.5");
        }

        [Fact]
        public void GetMlieVersions_CaseInsensitivePackageId()
        {

            var xml = @"<ModIds><li>Test.Mod.One</li></ModIds>";
            CreateMlieVersionFile("1.5", xml);

            var service = new MlieVersionService(_mockPathService, _mockDataUpdateService, _mockLogger);

            var result = service.GetMlieVersions();

            result.Should().ContainKey("test.mod.one");
        }

        [Fact]
        public void GetMlieVersions_WhenBothSourcesMissing_ShouldReturnEmpty()
        {

            Directory.Delete(_mlieModPath);
            var service = new MlieVersionService(_mockPathService, _mockDataUpdateService, _mockLogger);

            var result = service.GetMlieVersions();

            result.Should().BeEmpty();
        }
        private void CreateSnapshotVersionFile(string version, string xmlContent)
        {
            CreateVersionFile(_snapshotRoot, version, xmlContent);
        }

        [Fact]
        public void GetMlieVersions_ShouldUnionModFolderAndRepositorySnapshot()
        {
            CreateMlieVersionFile("1.4", @"<ModIds><li>test.mod.one</li></ModIds>");
            CreateSnapshotVersionFile("1.5", @"<ModIds><li>test.mod.one</li><li>test.mod.repo</li></ModIds>");

            var service = new MlieVersionService(_mockPathService, _mockDataUpdateService, _mockLogger);

            var result = service.GetMlieVersions();

            result.Should().ContainKey("test.mod.one");
            result["test.mod.one"].Should().HaveCount(2).And.Contain(new[] { "1.4", "1.5" });
            result.Should().ContainKey("test.mod.repo");
            result["test.mod.repo"].Should().ContainSingle().Which.Should().Be("1.5");
        }

        [Fact]
        public void GetMlieVersions_WhenModFolderMissing_ShouldFallBackToRepositorySnapshot()
        {
            Directory.Delete(_mlieModPath);
            CreateSnapshotVersionFile("1.6", @"<ModIds><li>test.mod.snapshot</li></ModIds>");

            var service = new MlieVersionService(_mockPathService, _mockDataUpdateService, _mockLogger);

            var result = service.GetMlieVersions();

            result.Should().ContainKey("test.mod.snapshot");
            result["test.mod.snapshot"].Should().ContainSingle().Which.Should().Be("1.6");
        }

        [Fact]
        public void GetMlieVersions_MalformedSnapshotXml_ShouldBeIgnored()
        {
            CreateSnapshotVersionFile("1.5", @"<ModIds><li>test.mod.broken");
            CreateSnapshotVersionFile("1.6", @"<ModIds><li>test.mod.good</li></ModIds>");

            var service = new MlieVersionService(_mockPathService, _mockDataUpdateService, _mockLogger);

            var result = service.GetMlieVersions();

            result.Should().ContainKey("test.mod.good");
            result.Should().NotContainKey("test.mod.broken");
        }

        [Fact]
        public void GetExternalVersions_WhenSeveralSourcesDeclareSameVersion_FirstSourceWinsAttribution()
        {
            var dirA = Path.Combine(_testTempDir, "sourceA");
            var dirB = Path.Combine(_testTempDir, "sourceB");
            Directory.CreateDirectory(dirA);
            Directory.CreateDirectory(dirB);
            CreateVersionFile(dirA, "1.5", @"<ModIds><li>test.mod.one</li></ModIds>");
            CreateVersionFile(dirB, "1.5", @"<ModIds><li>test.mod.one</li></ModIds>");

            var registry = CreateRegistry(
                LocalSource("first", "First Source", dirA),
                LocalSource("second", "Second Source", dirB));
            var service = new MlieVersionService(_mockPathService, _mockDataUpdateService, _mockLogger, registry);

            var result = service.GetExternalVersions();

            result["test.mod.one"].Should().ContainSingle()
                .Which.Should().Be(new ExternalVersionInfo("1.5", "First Source"));
        }

        [Fact]
        public void GetExternalVersions_WithSingleSource_ShouldUseItsName()
        {
            var dir = Path.Combine(_testTempDir, "solo");
            Directory.CreateDirectory(dir);
            CreateVersionFile(dir, "1.6", @"<ModIds><li>test.mod.solo</li></ModIds>");

            var registry = CreateRegistry(LocalSource("solo-id", "Solo Source", dir));
            var service = new MlieVersionService(_mockPathService, _mockDataUpdateService, _mockLogger, registry);

            var result = service.GetExternalVersions();

            result["test.mod.solo"].Should().ContainSingle()
                .Which.Should().Be(new ExternalVersionInfo("1.6", "Solo Source"));
        }

        [Fact]
        public void GetExternalVersions_WhenSourceNameMissing_ShouldFallBackToId()
        {
            var dir = Path.Combine(_testTempDir, "nameless");
            Directory.CreateDirectory(dir);
            CreateVersionFile(dir, "1.5", @"<ModIds><li>test.mod.nameless</li></ModIds>");

            var registry = CreateRegistry(LocalSource("fallback-id", string.Empty, dir));
            var service = new MlieVersionService(_mockPathService, _mockDataUpdateService, _mockLogger, registry);

            var result = service.GetExternalVersions();

            result["test.mod.nameless"].Should().ContainSingle()
                .Which.Should().Be(new ExternalVersionInfo("1.5", "fallback-id"));
        }

        [Fact]
        public void GetExternalVersions_WithoutRegistry_ShouldUseFallbackNames()
        {
            CreateMlieVersionFile("1.4", @"<ModIds><li>test.mod.one</li></ModIds>");

            var service = new MlieVersionService(_mockPathService, _mockDataUpdateService, _mockLogger);

            var result = service.GetExternalVersions();

            result["test.mod.one"].Should().ContainSingle()
                .Which.Should().Be(new ExternalVersionInfo("1.4", NoVersionWarningSourceRegistry.ModSourceName));
        }
    }
}
