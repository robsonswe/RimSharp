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

            var info = new SteamCmdPlatformInfo();

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

        [Fact]
        public void GetRuntimePrerequisiteProblem_OnWindows_ShouldReturnNull()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

            var info = new SteamCmdPlatformInfo();

            info.GetRuntimePrerequisiteProblem(_ => false).Should().BeNull();
        }

        [Fact]
        public void GetRuntimePrerequisiteProblem_OnLinux_WhenLoaderMissing_ShouldReturnMessage()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;

            var info = new SteamCmdPlatformInfo();

            var problem = info.GetRuntimePrerequisiteProblem(_ => false);

            problem.Should().NotBeNull();
            problem.Should().Contain("32-bit");
        }

        [Fact]
        public void GetRuntimePrerequisiteProblem_OnLinux_WhenLoaderPresent_ShouldReturnNull()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;

            var info = new SteamCmdPlatformInfo();

            info.GetRuntimePrerequisiteProblem(_ => true).Should().BeNull();
        }
    }
}

