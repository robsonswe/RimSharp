using System.Runtime.InteropServices;
using FluentAssertions;
using RimSharp.Infrastructure.Workshop.Core;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Workshop.Core
{
    public class SteamCmdPlatformInfoTests
    {
        [Fact]
        public void Constructor_ShouldInitializePlatformCorrectly()
        {
            // Act
            var info = new SteamCmdPlatformInfo();

            // Assert
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                info.Platform.Should().Be(OSPlatform.Windows);
                info.SteamCmdExeName.Should().Be("steamcmd.exe");
                info.IsArchiveZip.Should().BeTrue();
                info.IsPosix.Should().BeFalse();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                info.Platform.Should().Be(OSPlatform.Linux);
                info.SteamCmdExeName.Should().Be("steamcmd.sh");
                info.IsArchiveZip.Should().BeFalse();
                info.IsPosix.Should().BeTrue();
            }
        }
    }
}
