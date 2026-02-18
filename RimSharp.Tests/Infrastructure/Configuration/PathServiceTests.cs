using System;
using System.IO;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Configuration
{
    public class PathServiceTests : IDisposable
    {
        private readonly IConfigService _mockConfigService;
        private readonly PathService _pathService;
        private readonly string _testTempDir;

        public PathServiceTests()
        {
            _mockConfigService = Substitute.For<IConfigService>();
            _pathService = new PathService(_mockConfigService);
            
            // Create a unique temp directory for each test run to avoid cross-contamination
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        [Fact]
        public void GetGamePath_WhenDirectoryExists_ShouldReturnPath()
        {
            // Arrange
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            // Act
            var result = _pathService.GetGamePath();

            // Assert
            result.Should().Be(_testTempDir);
        }

        [Fact]
        public void GetGamePath_WhenDirectoryDoesNotExist_ShouldReturnEmptyString()
        {
            // Arrange
            var nonExistentPath = Path.Combine(_testTempDir, "NonExistent");
            _mockConfigService.GetConfigValue("game_folder").Returns(nonExistentPath);

            // Act
            var result = _pathService.GetGamePath();

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void GetModsPath_WhenGamePathValidAndModsFolderExists_ShouldReturnModsPath()
        {
            // Arrange
            var modsDir = Path.Combine(_testTempDir, "Mods");
            Directory.CreateDirectory(modsDir);
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            // Act
            var result = _pathService.GetModsPath();

            // Assert
            result.Should().Be(modsDir);
        }

        [Fact]
        public void GetGameVersion_WhenVersionFileExists_ShouldReturnFirstLine()
        {
            // Arrange
            var versionFile = Path.Combine(_testTempDir, "Version.txt");
            var expectedVersion = "1.5.1234 rev99";
            File.WriteAllText(versionFile, expectedVersion);
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            // Act
            var result = _pathService.GetGameVersion();

            // Assert
            result.Should().Be(expectedVersion);
        }

        [Theory]
        [InlineData("1.5.1234 rev99", "1.5")]
        [InlineData("1.4.3529", "1.4")]
        [InlineData("0.18.1722", "0.18")]
        [InlineData("1.5", "1.5")]
        [InlineData("invalid", "invalid")]
        [InlineData("N/A - File not found", "N/A - File not found")]
        public void GetMajorGameVersion_ShouldExtractCorrectMajorMinor(string fullVersion, string expectedMajor)
        {
            // Arrange
            // We need to bypass the file system for this one if we want to test the extraction logic directly.
            // But GetMajorGameVersion calls GetGameVersion() which calls GetGameVersion(_cachedGamePath).
            // So we'll mock the version file.
            var versionFile = Path.Combine(_testTempDir, "Version.txt");
            File.WriteAllText(versionFile, fullVersion);
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            // Act
            var result = _pathService.GetMajorGameVersion();

            // Assert
            result.Should().Be(expectedMajor);
        }

        [Fact]
        public void InvalidateCache_ShouldForceConfigReRead()
        {
            // Arrange
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);
            _pathService.GetGamePath(); // First call populates cache

            var newDir = Path.Combine(_testTempDir, "NewDir");
            Directory.CreateDirectory(newDir);
            _mockConfigService.GetConfigValue("game_folder").Returns(newDir);

            // Act
            _pathService.InvalidateCache();
            var result = _pathService.GetGamePath();

            // Assert
            result.Should().Be(newDir);
            _mockConfigService.Received(2).GetConfigValue("game_folder");
        }
    }
}
