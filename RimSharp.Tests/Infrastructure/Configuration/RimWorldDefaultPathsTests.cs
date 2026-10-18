using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FluentAssertions;
using RimSharp.Infrastructure.Configuration;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Configuration
{
    public class RimWorldDefaultPathsTests
    {
        [Fact]
        public void GetConfigFolder_WhenNoCandidateExists_ShouldReturnNull()
        {
            RimWorldDefaultPaths.GetConfigFolder(_ => false).Should().BeNull();
        }

        [Fact]
        public void GetConfigFolder_WhenCandidateExists_ShouldReturnFirstExisting()
        {
            var candidates = RimWorldDefaultPaths.GetConfigFolderCandidates();
            var expected = candidates.First();

            RimWorldDefaultPaths.GetConfigFolder(_ => true).Should().Be(expected);
        }

        [Fact]
        public void GetConfigFolderCandidates_OnLinux_ShouldContainUnity3DPath()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;

            var candidates = RimWorldDefaultPaths.GetConfigFolderCandidates();

            candidates.First().Should().Be(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "unity3d", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config"));
        }

        [Fact]
        public void GetConfigFolderCandidates_OnWindows_ShouldContainLocalLowPath()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

            var candidates = RimWorldDefaultPaths.GetConfigFolderCandidates();

            candidates.First().Should().Be(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config"));
        }

        [Fact]
        public void GetGameFolderHint_WhenNoCandidateExists_ShouldReturnNull()
        {
            RimWorldDefaultPaths.GetGameFolderHint(_ => false).Should().BeNull();
        }

        [Fact]
        public void GetGameFolderHint_WhenCandidateExists_ShouldReturnFirstExisting()
        {
            var candidates = RimWorldDefaultPaths.GetGameFolderHintCandidates();
            var expected = candidates.First();

            RimWorldDefaultPaths.GetGameFolderHint(_ => true).Should().Be(expected);
        }

        [Fact]
        public void GetGameFolderHintCandidates_ShouldPointToSteamLibraries()
        {
            var candidates = RimWorldDefaultPaths.GetGameFolderHintCandidates();

            candidates.Should().OnlyContain(path => path.EndsWith(
                Path.Combine("steamapps", "common", "RimWorld"), StringComparison.Ordinal));
        }
    }
}
