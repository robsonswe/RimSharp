using System.Runtime.InteropServices;
using FluentAssertions;
using RimSharp.Features.ModManager.ViewModels.Actions;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.ViewModels.Actions
{
    public class GameExecutableNamesTests
    {
        [Fact]
        public void GetLaunchCandidates_ForWindows_ShouldBeUnchanged()
        {
            GameExecutableNames.GetLaunchCandidates(OSPlatform.Windows)
                .Should().Equal("RimWorldWin64.exe", "RimWorld.exe");
        }

        [Fact]
        public void GetLaunchCandidates_ForLinux_ShouldReturnLinuxCandidates()
        {
            GameExecutableNames.GetLaunchCandidates(OSPlatform.Linux)
                .Should().Equal("RimWorld", "RimWorldLinux64", "RimWorldLinux", "start_RimWorld.sh");
        }

        [Fact]
        public void GetLaunchCandidates_ForMacOS_ShouldReturnMacCandidates()
        {
            GameExecutableNames.GetLaunchCandidates(OSPlatform.OSX)
                .Should().Equal("RimWorld.app");
        }

        [Fact]
        public void GetProcessNames_ForWindows_ShouldBeUnchanged()
        {
            GameExecutableNames.GetProcessNames(OSPlatform.Windows)
                .Should().Equal("RimWorldWin64", "RimWorld");
        }

        [Fact]
        public void GetProcessNames_ForLinux_ShouldAddLinuxProcessName()
        {
            GameExecutableNames.GetProcessNames(OSPlatform.Linux)
                .Should().Equal("RimWorldWin64", "RimWorld", "RimWorldLinux64", "RimWorldLinux");
        }

        [Fact]
        public void GetProcessNames_ForMacOS_ShouldReturnMacProcessNames()
        {
            GameExecutableNames.GetProcessNames(OSPlatform.OSX)
                .Should().Equal("RimWorld", "RimWorldMac");
        }
    }
}
