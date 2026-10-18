using System;
using System.IO;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Workshop.Core;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Workshop.Core
{
    public class SteamCmdPathServiceTests
    {
        private readonly IConfigService _configService = Substitute.For<IConfigService>();
        private const string ExeName = "steamcmd.exe";

        [Fact]
        public void InitializePaths_WhenConfigEmpty_ShouldUseDefault()
        {

            _configService.GetConfigValue("steamcmd_prefix").Returns("");

            var service = new SteamCmdPathService(_configService, ExeName);

            service.SteamCmdPrefixPath.Should().Contain("SteamCMD_Data");
            service.SteamCmdInstallPath.Should().Contain("steamcmd");
            service.SteamCmdExePath.Should().EndWith(ExeName);
        }

        [Fact]
        public void InitializePaths_WhenConfigEmpty_ShouldDefaultToAppDataPathsDataDirectory()
        {

            _configService.GetConfigValue("steamcmd_prefix").Returns("");

            var service = new SteamCmdPathService(_configService, ExeName);

            service.SteamCmdPrefixPath.Should().Be(Path.Combine(AppDataPaths.DataDirectory, "SteamCMD_Data"));
        }

        [Fact]
        public void InitializePaths_WhenConfigSet_ShouldUseConfigPath()
        {

            // Build the configured prefix with Path.Combine so the expected
            // values match the platform directory separator.
            var customPrefix = Path.Combine(Path.GetTempPath(), "CustomPath");
            _configService.GetConfigValue("steamcmd_prefix").Returns(customPrefix);

            var service = new SteamCmdPathService(_configService, ExeName);

            service.SteamCmdPrefixPath.Should().Be(customPrefix);
            service.SteamCmdInstallPath.Should().Be(Path.Combine(customPrefix, "steamcmd"));
            service.SteamCmdWorkshopContentPath.Should().Contain("294100");
        }
    }
}

