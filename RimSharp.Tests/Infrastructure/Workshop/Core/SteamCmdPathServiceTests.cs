using FluentAssertions;
using NSubstitute;
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
            // Arrange
            _configService.GetConfigValue("steamcmd_prefix").Returns("");

            // Act
            var service = new SteamCmdPathService(_configService, ExeName);

            // Assert
            service.SteamCmdPrefixPath.Should().Contain("SteamCMD_Data");
            service.SteamCmdInstallPath.Should().Contain("steamcmd");
            service.SteamCmdExePath.Should().EndWith(ExeName);
        }

        [Fact]
        public void InitializePaths_WhenConfigSet_ShouldUseConfigPath()
        {
            // Arrange
            _configService.GetConfigValue("steamcmd_prefix").Returns(@"C:\CustomPath");

            // Act
            var service = new SteamCmdPathService(_configService, ExeName);

            // Assert
            service.SteamCmdPrefixPath.Should().Be(@"C:\CustomPath");
            service.SteamCmdInstallPath.Should().Be(@"C:\CustomPath\steamcmd");
            service.SteamCmdWorkshopContentPath.Should().Contain("294100");
        }
    }
}
