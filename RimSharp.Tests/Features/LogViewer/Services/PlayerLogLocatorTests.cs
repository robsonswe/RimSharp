using System.IO;
using FluentAssertions;
using RimSharp.Features.LogViewer.Services;
using Xunit;

namespace RimSharp.Tests.Features.LogViewer.Services
{
    public class PlayerLogLocatorTests : System.IDisposable
    {
        private readonly string _root;
        private readonly string _configFolder;

        public PlayerLogLocatorTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "rimsharp-logreader-" + Path.GetRandomFileName());
            _configFolder = Path.Combine(_root, "Config");
            Directory.CreateDirectory(_configFolder);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }

        [Fact]
        public void Resolve_FindsBothLogsWhenPresent()
        {
            File.WriteAllText(Path.Combine(_root, "Player.log"), "current");
            File.WriteAllText(Path.Combine(_root, "Player-prev.log"), "previous");

            var sources = PlayerLogLocator.Resolve(_configFolder, majorGameVersion: null);

            sources.Current.Should().Be(Path.Combine(_root, "Player.log"));
            sources.Previous.Should().Be(Path.Combine(_root, "Player-prev.log"));
        }

        [Fact]
        public void Resolve_ReturnsNullForMissingPrevious()
        {
            File.WriteAllText(Path.Combine(_root, "Player.log"), "current");

            var sources = PlayerLogLocator.Resolve(_configFolder, majorGameVersion: null);

            sources.Current.Should().NotBeNull();
            sources.Previous.Should().BeNull();
        }

        [Fact]
        public void Resolve_ReturnsNullsWhenNoLogsExist()
        {
            var sources = PlayerLogLocator.Resolve(_configFolder, majorGameVersion: null);

            sources.Current.Should().BeNull();
            sources.Previous.Should().BeNull();
        }

        [Fact]
        public void Resolve_ToleratesTrailingSeparatorOnConfigFolder()
        {
            File.WriteAllText(Path.Combine(_root, "Player.log"), "current");

            var sources = PlayerLogLocator.Resolve(_configFolder + Path.DirectorySeparatorChar, majorGameVersion: null);

            sources.Current.Should().Be(Path.Combine(_root, "Player.log"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Resolve_BlankConfigFolderYieldsNulls(string? configFolder)
        {
            var sources = PlayerLogLocator.Resolve(configFolder, majorGameVersion: null);

            sources.Current.Should().BeNull();
            sources.Previous.Should().BeNull();
        }

        [Fact]
        public void Resolve_ConfigFolderAtFilesystemRootYieldsNulls()
        {
            var sources = PlayerLogLocator.Resolve(Path.GetPathRoot(_root), majorGameVersion: null);

            sources.Current.Should().BeNull();
            sources.Previous.Should().BeNull();
        }

        [Theory]
        [InlineData("1.7")]
        [InlineData("1.7.4979.109")]
        [InlineData("2.0")]
        public void Resolve_WaylithVersionsReadFromLogsSubfolder(string majorGameVersion)
        {
            var logsFolder = Path.Combine(_root, "Logs");
            Directory.CreateDirectory(logsFolder);
            File.WriteAllText(Path.Combine(logsFolder, "Player.log"), "current");
            File.WriteAllText(Path.Combine(logsFolder, "Player-prev.log"), "previous");
            // A stale root-level log must be ignored on Waylith.
            File.WriteAllText(Path.Combine(_root, "Player.log"), "stale");

            var sources = PlayerLogLocator.Resolve(_configFolder, majorGameVersion);

            sources.Current.Should().Be(Path.Combine(logsFolder, "Player.log"));
            sources.Previous.Should().Be(Path.Combine(logsFolder, "Player-prev.log"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1.6")]
        [InlineData("1.5.5071.31013")]
        [InlineData("N/A")]
        public void Resolve_PreWaylithVersionsReadFromRoot(string? majorGameVersion)
        {
            File.WriteAllText(Path.Combine(_root, "Player.log"), "current");
            var logsFolder = Path.Combine(_root, "Logs");
            Directory.CreateDirectory(logsFolder);
            File.WriteAllText(Path.Combine(logsFolder, "Player.log"), "wrong-layout");

            var sources = PlayerLogLocator.Resolve(_configFolder, majorGameVersion);

            sources.Current.Should().Be(Path.Combine(_root, "Player.log"));
        }
    }
}
