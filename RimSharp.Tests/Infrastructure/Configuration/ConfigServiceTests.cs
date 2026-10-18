using System;
using System.IO;
using FluentAssertions;
using RimSharp.Infrastructure.Configuration;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Configuration
{
    public class ConfigServiceTests : IDisposable
    {
        private readonly string _testConfigPath;

        public ConfigServiceTests()
        {
            _testConfigPath = Path.Combine(Path.GetTempPath(), "RimSharp_ConfigTest_" + Guid.NewGuid().ToString("N") + ".cfg");
        }

        public void Dispose()
        {
            if (File.Exists(_testConfigPath))
            {
                File.Delete(_testConfigPath);
            }
        }

        [Fact]
        public void Constructor_WhenFileDoesNotExist_ShouldCreateDefaultConfig()
        {

            var service = new ConfigService(_testConfigPath);

            File.Exists(_testConfigPath).Should().BeTrue();
            service.GetConfigValue("game_folder").Should().BeEmpty();
            service.GetConfigValue("config_folder").Should().BeEmpty();
        }

        [Fact]
        public void LoadConfig_WhenFileExists_ShouldLoadValues()
        {

            File.WriteAllLines(_testConfigPath, new[] { @"game_folder=C:\Game", @"config_folder=C:\Config" });

            var service = new ConfigService(_testConfigPath);

            service.GetConfigValue("game_folder").Should().Be(@"C:\Game");
            service.GetConfigValue("config_folder").Should().Be(@"C:\Config");
        }

        [Fact]
        public void SaveConfig_ShouldPersistChanges()
        {

            var service = new ConfigService(_testConfigPath);
            service.SetConfigValue("game_folder", @"D:\RimWorld");

            service.SaveConfig();

            var newService = new ConfigService(_testConfigPath);
            newService.GetConfigValue("game_folder").Should().Be(@"D:\RimWorld");
        }

        [Fact]
        public void SetConfigValue_ShouldOnlyAllowValidKeys()
        {

            var service = new ConfigService(_testConfigPath);

            service.SetConfigValue("invalid_key", "some value");

            service.GetConfigValue("invalid_key").Should().BeEmpty();
        }

        [Fact]
        public void LoadConfig_ShouldIgnoreCommentLines()
        {

            File.WriteAllLines(_testConfigPath, new[]
            {
                "# This is a comment",
                @"game_folder=C:\Game",
                "# Another comment"
            });

            var service = new ConfigService(_testConfigPath);

            service.GetConfigValue("game_folder").Should().Be(@"C:\Game");
        }

        [Fact]
        public void LoadConfig_ShouldIgnoreBlankLines()
        {

            File.WriteAllLines(_testConfigPath, new[]
            {
                "",
                @"game_folder=D:\Game",
                "   "
            });

            var service = new ConfigService(_testConfigPath);

            service.GetConfigValue("game_folder").Should().Be(@"D:\Game");
        }

        [Fact]
        public void LoadConfig_ShouldTrimWhitespaceAroundKeyAndValue()
        {

            File.WriteAllLines(_testConfigPath, new[]
            {
                @"  game_folder  =  C:\Trimmed  "
            });

            var service = new ConfigService(_testConfigPath);

            service.GetConfigValue("game_folder").Should().Be(@"C:\Trimmed");
        }

        [Fact]
        public void SaveConfig_MultipleSaves_ShouldPersistLastValue()
        {

            var service = new ConfigService(_testConfigPath);
            service.SetConfigValue("game_folder", @"C:\First");
            service.SaveConfig();

            service.SetConfigValue("game_folder", @"C:\Second");
            service.SaveConfig();

            var loaded = new ConfigService(_testConfigPath);
            loaded.GetConfigValue("game_folder").Should().Be(@"C:\Second");
        }

        [Fact]
        public void GetConfigValue_WhenKeyNotPresent_ShouldReturnEmpty()
        {

            var service = new ConfigService(_testConfigPath);

            service.GetConfigValue("nonexistent_key").Should().BeEmpty();
        }

        [Fact]
        public void SetConfigValue_WithNull_ShouldStoreEmptyString()
        {

            var service = new ConfigService(_testConfigPath);
            service.SetConfigValue("game_folder", null!);

            service.GetConfigValue("game_folder").Should().BeEmpty();
        }

        [Fact]
        public void SetConfigValue_SteamCmdPrefix_ShouldPersistAcrossInstances()
        {

            var service = new ConfigService(_testConfigPath);
            service.SetConfigValue("steamcmd_prefix", "/opt/steamcmd-data");

            service.SaveConfig();

            var loaded = new ConfigService(_testConfigPath);
            loaded.GetConfigValue("steamcmd_prefix").Should().Be("/opt/steamcmd-data");
        }

        [Fact]
        public void SetConfigValue_LogsDirectory_ShouldPersistAcrossInstances()
        {

            var service = new ConfigService(_testConfigPath);
            service.SetConfigValue("logs_directory", "/var/log/rimsharp");

            service.SaveConfig();

            var loaded = new ConfigService(_testConfigPath);
            loaded.GetConfigValue("logs_directory").Should().Be("/var/log/rimsharp");
        }

        [Fact]
        public void LoadConfig_ShouldLoadSteamCmdPrefixAndLogsDirectory()
        {

            File.WriteAllLines(_testConfigPath, new[]
            {
                @"game_folder=C:\Game",
                @"config_folder=C:\Config",
                @"steamcmd_prefix=/opt/steamcmd-data",
                @"logs_directory=/var/log/rimsharp"
            });

            var service = new ConfigService(_testConfigPath);

            service.GetConfigValue("steamcmd_prefix").Should().Be("/opt/steamcmd-data");
            service.GetConfigValue("logs_directory").Should().Be("/var/log/rimsharp");
        }

        [Fact]
        public void SaveConfig_ShouldPersistAllWhitelistedKeysTogether()
        {

            var service = new ConfigService(_testConfigPath);
            service.SetConfigValue("game_folder", @"C:\Game");
            service.SetConfigValue("config_folder", @"C:\Config");
            service.SetConfigValue("steamcmd_prefix", "/opt/steamcmd-data");
            service.SetConfigValue("logs_directory", "/var/log/rimsharp");

            service.SaveConfig();

            var lines = File.ReadAllLines(_testConfigPath);
            lines.Should().Contain("game_folder=C:\\Game");
            lines.Should().Contain("config_folder=C:\\Config");
            lines.Should().Contain("steamcmd_prefix=/opt/steamcmd-data");
            lines.Should().Contain("logs_directory=/var/log/rimsharp");
        }
    }
}

