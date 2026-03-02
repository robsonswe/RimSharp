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

            _configService.GetConfigValue("steamcmd_prefix").Returns("");

            var service = new SteamCmdPathService(_configService, ExeName);

            service.SteamCmdPrefixPath.Should().Contain("SteamCMD_Data");
            service.SteamCmdInstallPath.Should().Contain("steamcmd");
            service.SteamCmdExePath.Should().EndWith(ExeName);
        }

        [Fact]
        public void InitializePaths_WhenConfigSet_ShouldUseConfigPath()
        {

            _configService.GetConfigValue("steamcmd_prefix").Returns(@"C:\CustomPath");

            var service = new SteamCmdPathService(_configService, ExeName);

            service.SteamCmdPrefixPath.Should().Be(@"C:\CustomPath");
            service.SteamCmdInstallPath.Should().Be(@"C:\CustomPath\steamcmd");
            service.SteamCmdWorkshopContentPath.Should().Contain("294100");
        }
    }
}

