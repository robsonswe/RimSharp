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
    }
}

