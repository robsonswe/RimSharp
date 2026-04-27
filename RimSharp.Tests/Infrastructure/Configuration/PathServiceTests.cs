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
        private readonly PathService _service;
        private readonly string _testTempDir;

        public PathServiceTests()
        {
            _mockConfigService = Substitute.For<IConfigService>();
            _service = new PathService(_mockConfigService);
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
        public void GetGamePath_WhenConfigValueIsValid_ShouldReturnPath()
        {

            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            var result = _service.GetGamePath();

            result.Should().Be(_testTempDir);
        }

        [Fact]
        public void GetGamePath_WhenConfigValueIsInvalid_ShouldReturnEmpty()
        {

            _mockConfigService.GetConfigValue("game_folder").Returns(@"C:\NonExistentPath_12345");

            var result = _service.GetGamePath();

            result.Should().BeEmpty();
        }

        [Fact]
        public void GetModsPath_WhenGamePathIsValid_ShouldReturnModsPath()
        {

            var modsPath = Path.Combine(_testTempDir, "Mods");
            Directory.CreateDirectory(modsPath);
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            var result = _service.GetModsPath();

            result.Should().Be(modsPath);
        }

        [Fact]
        public void GetGameVersion_WhenVersionFileExists_ShouldReturnVersion()
        {

            var versionFile = Path.Combine(_testTempDir, "Version.txt");
            File.WriteAllText(versionFile, "1.5.4321");
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            var result = _service.GetGameVersion();

            result.Should().Be("1.5.4321");
        }

        [Fact]
        public void GetMajorGameVersion_ShouldExtractCorrectly()
        {

            var versionFile = Path.Combine(_testTempDir, "Version.txt");
            File.WriteAllText(versionFile, "1.4.3529 rev704");
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            var result = _service.GetMajorGameVersion();

            result.Should().Be("1.4");
        }

        [Fact]
        public void RefreshPaths_ShouldInvalidateCache()
        {

            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);
            _service.GetGamePath(); // Populate cache

            var newPath = Path.Combine(_testTempDir, "NewGame");
            Directory.CreateDirectory(newPath);
            _mockConfigService.GetConfigValue("game_folder").Returns(newPath);

            _service.RefreshPaths();
            var result = _service.GetGamePath();

            result.Should().Be(newPath);
        }

        [Fact]
        public void GetModsPath_WhenGamePathDoesNotExist_ShouldReturnEmpty()
        {

            _mockConfigService.GetConfigValue("game_folder").Returns(@"C:\NonExistent_XYZ_123");

            var result = _service.GetModsPath();

            result.Should().BeEmpty();
        }

        [Fact]
        public void GetModsPath_WhenModsDirDoesNotExist_ShouldReturnEmpty()
        {

            // Game dir exists but has no Mods subdirectory
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            var result = _service.GetModsPath();

            result.Should().BeEmpty();
        }

        [Fact]
        public void GetConfigPath_WhenConfigValueIsValid_ShouldReturnPath()
        {

            var configPath = Path.Combine(_testTempDir, "Config");
            Directory.CreateDirectory(configPath);
            _mockConfigService.GetConfigValue("config_folder").Returns(configPath);

            var result = _service.GetConfigPath();

            result.Should().Be(configPath);
        }

        [Fact]
        public void GetConfigPath_WhenConfigValueDoesNotExist_ShouldReturnEmpty()
        {

            _mockConfigService.GetConfigValue("config_folder").Returns(@"C:\NonExistent_XYZ_789");

            var result = _service.GetConfigPath();

            result.Should().BeEmpty();
        }

        [Fact]
        public void GetGameVersion_WhenVersionFileDoesNotExist_ShouldReturnEmpty()
        {

            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);
            // No Version.txt created

            var result = _service.GetGameVersion();

            result.Should().BeEmpty();
        }
    }
}

