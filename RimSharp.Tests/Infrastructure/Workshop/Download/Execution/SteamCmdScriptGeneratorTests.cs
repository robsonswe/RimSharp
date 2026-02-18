using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Infrastructure.Workshop.Download.Execution;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Workshop.Download.Execution
{
    public class SteamCmdScriptGeneratorTests : IDisposable
    {
        private readonly SteamCmdScriptGenerator _generator;
        private readonly string _testTempDir;

        public SteamCmdScriptGeneratorTests()
        {
            _generator = new SteamCmdScriptGenerator();
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_Script_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        [Fact]
        public async Task GenerateScriptAsync_ShouldCreateCorrectScriptFile()
        {
            // Arrange
            var items = new List<DownloadItem>
            {
                new DownloadItem { SteamId = "123" },
                new DownloadItem { SteamId = "456" }
            };
            var installDir = @"C:\SteamCMD\RimWorld";
            var uniqueId = "test";

            // Act
            var path = await _generator.GenerateScriptAsync(_testTempDir, uniqueId, installDir, items, true, CancellationToken.None);

            // Assert
            File.Exists(path).Should().BeTrue();
            var content = await File.ReadAllTextAsync(path);
            content.Should().Contain($@"force_install_dir ""{installDir}""");
            content.Should().Contain("login anonymous");
            content.Should().Contain("workshop_download_item 294100 123 validate");
            content.Should().Contain("workshop_download_item 294100 456 validate");
            content.Should().Contain("quit");
        }

        [Fact]
        public async Task GenerateScriptAsync_WithoutValidate_ShouldNotIncludeValidateInCommands()
        {
            // Arrange
            var items = new List<DownloadItem> { new DownloadItem { SteamId = "789" } };
            var installDir = @"C:\SteamCMD\RimWorld";
            var uniqueId = "test_no_validate";

            // Act
            var path = await _generator.GenerateScriptAsync(_testTempDir, uniqueId, installDir, items, false, CancellationToken.None);

            // Assert
            var content = await File.ReadAllTextAsync(path);
            content.Should().Contain("workshop_download_item 294100 789");
            content.Should().NotContain("validate");
        }

        [Fact]
        public async Task GenerateScriptAsync_WithEmptyItems_ShouldStillCreateValidBaseScript()
        {
            // Act
            var path = await _generator.GenerateScriptAsync(_testTempDir, "empty", "dir", new List<DownloadItem>(), false, CancellationToken.None);

            // Assert
            var content = await File.ReadAllTextAsync(path);
            content.Should().Contain("login anonymous");
            content.Should().Contain("quit");
            content.Should().NotContain("workshop_download_item");
        }
    }
}
