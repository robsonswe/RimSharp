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

        [Fact]
        public void BuildProtonConfigPath_ShouldUseCompatdataPrefixLayout()
        {
            var steamRoot = "FakeSteamRoot";

            var expected = Path.Combine(
                steamRoot, "steamapps", "compatdata", "294100",
                "pfx", "drive_c", "users", "steamuser",
                "AppData", "LocalLow", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config");

            RimWorldDefaultPaths.BuildProtonConfigPath(steamRoot).Should().Be(expected);
        }

        [Fact]
        public void GetProtonConfigFolder_WhenPrefixExists_ShouldReturnProtonPath()
        {
            var expected = RimWorldDefaultPaths.BuildProtonConfigPath("FakeSteamRoot");

            RimWorldDefaultPaths.GetProtonConfigFolder("FakeSteamRoot", path => path == expected)
                .Should().Be(expected);
        }

        [Fact]
        public void GetProtonConfigFolder_WhenPrefixMissing_ShouldReturnNull()
        {
            RimWorldDefaultPaths.GetProtonConfigFolder("FakeSteamRoot", _ => false)
                .Should().BeNull();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void GetProtonConfigFolder_WhenSteamRootNotUsable_ShouldReturnNull(string? steamRoot)
        {
            RimWorldDefaultPaths.GetProtonConfigFolder(steamRoot, _ => true)
                .Should().BeNull();
        }

        [Fact]
        public void GetLinuxSteamRootCandidates_ShouldListNativeThenSandboxedInstallRoots()
        {
            // Path.Combine on both sides: pins the exact set/order without separators
            // depending on the host OS running the suite.
            var userProfile = "TesterHome";

            RimWorldDefaultPaths.GetLinuxSteamRootCandidates(userProfile)
                .Should().Equal(
                    Path.Combine(userProfile, ".steam", "debian-installation"),
                    Path.Combine(userProfile, ".steam", "steam"),
                    Path.Combine(userProfile, ".local", "share", "Steam"),
                    Path.Combine(userProfile, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
                    Path.Combine(userProfile, "snap", "steam", "common", ".local", "share", "Steam"));
        }

        [Fact]
        [Trait("OS", "linux")]
        public void GetProtonConfigFolder_OnLinux_WithRealFilesystem_ShouldDetectCreatedPrefix()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;

            var testRoot = Path.Combine(Path.GetTempPath(), "RimSharpTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                // Real Linux filesystem is case-sensitive: the prefix must be created
                // with the exact casing the helper builds, and no other candidate may
                // exist — this pins the full path, not just a sub-string.
                var protonConfig = RimWorldDefaultPaths.BuildProtonConfigPath(testRoot);
                Directory.CreateDirectory(protonConfig);

                RimWorldDefaultPaths.GetProtonConfigFolder(testRoot).Should().Be(protonConfig);

                // A steam root without the compat prefix must fall back to null so the
                // caller keeps using the native Unity config path.
                var otherRoot = testRoot + "_other";
                Directory.CreateDirectory(Path.Combine(otherRoot, "steamapps"));
                RimWorldDefaultPaths.GetProtonConfigFolder(otherRoot).Should().BeNull();
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
                if (Directory.Exists(testRoot + "_other")) Directory.Delete(testRoot + "_other", true);
            }
        }
    }
}
