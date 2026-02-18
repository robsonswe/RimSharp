using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;
using Xunit;

namespace RimSharp.Tests.Shared.Services.Implementations
{
    public class MlieVersionServiceTests : IDisposable
    {
        private readonly IPathService _mockPathService;
        private readonly ILoggerService _mockLogger;
        private readonly string _testTempDir;
        private readonly string _mlieModPath;

        public MlieVersionServiceTests()
        {
            _mockPathService = Substitute.For<IPathService>();
            _mockLogger = Substitute.For<ILoggerService>();

            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_Mlie_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);

            // Path expected by MlieVersionService
            _mlieModPath = Path.Combine(_testTempDir, "2599504692");
            Directory.CreateDirectory(_mlieModPath);

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
            var versionDir = Path.Combine(_mlieModPath, version);
            Directory.CreateDirectory(versionDir);
            File.WriteAllText(Path.Combine(versionDir, "ModIdsToFix.xml"), xmlContent);
        }

        [Fact]
        public void GetMlieVersions_ShouldLoadVersionsFromXml()
        {
            // Arrange
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

            var service = new MlieVersionService(_mockPathService, _mockLogger);

            // Act
            var result = service.GetMlieVersions();

            // Assert
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
            // Arrange
            var xml = @"<ModIds><li>Test.Mod.One</li></ModIds>";
            CreateMlieVersionFile("1.5", xml);

            var service = new MlieVersionService(_mockPathService, _mockLogger);

            // Act
            var result = service.GetMlieVersions();

            // Assert
            result.Should().ContainKey("test.mod.one");
        }

        [Fact]
        public void GetMlieVersions_WhenModFolderMissing_ShouldReturnEmpty()
        {
            // Arrange
            Directory.Delete(_mlieModPath);
            var service = new MlieVersionService(_mockPathService, _mockLogger);

            // Act
            var result = service.GetMlieVersions();

            // Assert
            result.Should().BeEmpty();
        }
    }
}
