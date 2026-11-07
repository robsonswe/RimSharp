using System;
using FluentAssertions;
using RimSharp.Core.Services;
using Xunit;

namespace RimSharp.Tests.Core.Services
{
    public class SteamProtocolLauncherTests
    {
        [Fact]
        public void SelectLinuxTerminal_WhenNoneAvailable_ShouldReturnNull()
        {
            SteamProtocolLauncher.SelectLinuxTerminal(_ => false).Should().BeNull();
        }

        [Fact]
        public void SelectLinuxTerminal_ShouldPreferFirstCandidateInOrder()
        {
            // Only the later candidates exist -> the first AVAILABLE one wins, in order.
            SteamProtocolLauncher.SelectLinuxTerminal(cmd => cmd is "konsole" or "xterm")
                .Should().Be("konsole");
        }

        [Fact]
        public void SelectLinuxTerminal_WhenGnomeTerminalAvailable_ShouldWin()
        {
            SteamProtocolLauncher.SelectLinuxTerminal(cmd => cmd is "gnome-terminal" or "xterm")
                .Should().Be("gnome-terminal");
        }

        [Fact]
        public void SelectLinuxTerminal_WhenOnlyXtermFamilyAvailable_ShouldReturnIt()
        {
            SteamProtocolLauncher.SelectLinuxTerminal(cmd => cmd == "xfce4-terminal")
                .Should().Be("xfce4-terminal");
        }

        [Fact]
        public void TryOpen_WhenUrlNullOrWhitespace_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() => SteamProtocolLauncher.TryOpen(" "));
        }
    }
}
