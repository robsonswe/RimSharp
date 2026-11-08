using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.AppLogs.Services;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.AppLogs.Services
{
    /// <summary>
    /// Locator tests run against the exact file names the app's logger
    /// writes, including module names that contain underscores.
    /// </summary>
    public class AppLogLocatorTests : System.IDisposable
    {
        private readonly string _root;

        public AppLogLocatorTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "rimsharp-applogs-locator-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }

        private static IConfigService ConfigWithLogsDirectory(string? value)
        {
            var config = Substitute.For<IConfigService>();
            config.GetConfigValue("logs_directory").Returns(value);
            return config;
        }

        [Fact]
        public void ResolveLogsDirectory_PrefersTheConfiguredDirectory()
        {
            AppLogLocator.ResolveLogsDirectory(ConfigWithLogsDirectory(@"D:\custom\logs"), @"D:\appdata")
                .Should().Be(@"D:\custom\logs");
        }

        [Fact]
        public void ResolveLogsDirectory_FallsBackToTheAppDataLogsFolder()
        {
            AppLogLocator.ResolveLogsDirectory(ConfigWithLogsDirectory(null), @"D:\appdata")
                .Should().Be(Path.Combine(@"D:\appdata", "Logs"));
        }

        [Fact]
        public void TryParseFileName_ReadsModulesWithUnderscores()
        {
            var file = AppLogLocator.TryParseFileName(
                "RimSharp_SteamCmd_Downloader_20261107.log", @"D:\logs\RimSharp_SteamCmd_Downloader_20261107.log");

            file.Should().NotBeNull();
            file!.Module.Should().Be("SteamCmd_Downloader");
            file.Date.Should().Be(new DateTime(2026, 11, 7));
        }

        [Fact]
        public void TryParseFileName_ReadsSimpleModuleNames()
        {
            var file = AppLogLocator.TryParseFileName("RimSharp_General_20261107.log", @"D:\logs\RimSharp_General_20261107.log");

            file!.Module.Should().Be("General");
            file!.Date.Should().Be(new DateTime(2026, 11, 7));
        }

        [Theory]
        [InlineData("Player.log")]
        [InlineData("RimSharp_20261107.log")]           // no module segment
        [InlineData("RimSharp_General_notadate.log")]   // trailing segment isn't a date
        [InlineData("RimSharp_General_2026117.log")]    // short date
        [InlineData("RimSharp_20261399.log")]           // impossible month
        public void TryParseFileName_RejectsEverythingElse(string fileName)
        {
            AppLogLocator.TryParseFileName(fileName, Path.Combine(_root, fileName)).Should().BeNull();
        }

        [Fact]
        public void Enumerate_GroupsTheWritersFilesAndIgnoresStrays()
        {
            File.WriteAllText(Path.Combine(_root, "RimSharp_General_20261107.log"), "today\n");
            File.WriteAllText(Path.Combine(_root, "RimSharp_SteamCmd_Downloader_20261107.log"), "today\n");
            File.WriteAllText(Path.Combine(_root, "RimSharp_SteamCmd_Downloader_20261106.log"), "yesterday\n");
            File.WriteAllText(Path.Combine(_root, "Player.log"), "not ours\n");
            File.WriteAllText(Path.Combine(_root, "RimSharp_20261107.log"), "no module\n");

            var files = AppLogLocator.Enumerate(_root);

            files.Should().HaveCount(3);
            files.Select(f => f.Module).Should().BeEquivalentTo(
                new[] { "General", "SteamCmd_Downloader", "SteamCmd_Downloader" });
            // Newest day first, so a module's newest file leads its group.
            files[0].Date.Should().Be(new DateTime(2026, 11, 7));
            files[2].Date.Should().Be(new DateTime(2026, 11, 6));
            files[0].SizeBytes.Should().BeGreaterThan(0);
        }

        [Fact]
        public void Enumerate_MissingDirectoryYieldsNothing()
        {
            AppLogLocator.Enumerate(Path.Combine(_root, "nope")).Should().BeEmpty();
            AppLogLocator.Enumerate(string.Empty).Should().BeEmpty();
        }
    }
}
