using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Infrastructure.Workshop.Download.Processing;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Workshop.Download.Processing
{
    public class DownloadedItemProcessorTests : IDisposable
    {
        private readonly ILoggerService _mockLogger;
        private readonly IModService _mockModService;
        private readonly DownloadedItemProcessor _processor;
        private readonly string _testTempDir;
        private readonly string _sourcePath;
        private readonly string _targetPath;

        public DownloadedItemProcessorTests()
        {
            _mockLogger = Substitute.For<ILoggerService>();
            _mockModService = Substitute.For<IModService>();
            _processor = new DownloadedItemProcessor(_mockLogger, _mockModService);

            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharp_ProcessorTest_" + Guid.NewGuid().ToString("N"));
            _sourcePath = Path.Combine(_testTempDir, "Source");
            _targetPath = Path.Combine(_testTempDir, "Target");

            Directory.CreateDirectory(_sourcePath);
            Directory.CreateDirectory(_targetPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        [Fact]
        public async Task ProcessItemAsync_SameVolume_ShouldMoveSuccessfully()
        {
            // Arrange
            var item = new DownloadItem { SteamId = "123", Name = "Test Mod" };
            File.WriteAllText(Path.Combine(_sourcePath, "About.xml"), "content");

            // Act
            var result = await _processor.ProcessItemAsync(item, _sourcePath, _targetPath, "_backup", CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            Directory.Exists(_targetPath).Should().BeTrue();
            File.Exists(Path.Combine(_targetPath, "About.xml")).Should().BeTrue();
            Directory.Exists(_sourcePath).Should().BeFalse();
        }

        [Fact]
        public async Task ProcessItemAsync_WhenBackupExists_ShouldCreateBackupAndDeleteOnSuccess()
        {
            // Arrange
            var item = new DownloadItem { SteamId = "123", Name = "Test Mod" };
            File.WriteAllText(Path.Combine(_sourcePath, "new.txt"), "new");
            File.WriteAllText(Path.Combine(_targetPath, "old.txt"), "old");

            // Act
            var result = await _processor.ProcessItemAsync(item, _sourcePath, _targetPath, "_backup", CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            File.Exists(Path.Combine(_targetPath, "new.txt")).Should().BeTrue();
            
            // Backup should be deleted on success
            Directory.GetDirectories(_testTempDir).Any(d => d.Contains("_backup")).Should().BeFalse();
        }

        [Fact]
        public async Task ProcessItemAsync_WhenDdsFilesExistInBackup_ShouldPreserveThem()
        {
            // Arrange
            var item = new DownloadItem { SteamId = "123", Name = "Test Mod" };
            
            // Source (New version) has PNG
            File.WriteAllText(Path.Combine(_sourcePath, "texture.png"), "SAME_CONTENT");
            
            // Target (Old version) has PNG and its DDS
            File.WriteAllText(Path.Combine(_targetPath, "texture.png"), "SAME_CONTENT");
            File.WriteAllText(Path.Combine(_targetPath, "texture.dds"), "dds_content");

            // Act
            var result = await _processor.ProcessItemAsync(item, _sourcePath, _targetPath, "_backup", CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            File.Exists(Path.Combine(_targetPath, "texture.dds")).Should().BeTrue();
        }

        [Fact]
        public async Task ProcessItemAsync_WhenExceptionDuringMove_ShouldRollback()
        {
            // Arrange
            var item = new DownloadItem { SteamId = "123", Name = "Test Mod" };
            File.WriteAllText(Path.Combine(_sourcePath, "new.xml"), "new");
            File.WriteAllText(Path.Combine(_targetPath, "old.xml"), "old");

            // We'll simulate a failure by making the source a directory that can't be moved 
            // because the target is now a directory with files (actually move handles that, 
            // but let's try something that definitely fails).
            // A reliable way: make a file read-only or similar might not work across all systems.
            // Let's mock _modService.CreateTimestampFilesAsync to throw.
            _mockModService.When(x => x.CreateTimestampFilesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()))
                .Do(x => throw new IOException("Simulated Failure"));

            // Act
            var result = await _processor.ProcessItemAsync(item, _sourcePath, _targetPath, "_backup", CancellationToken.None);

            // Assert
            result.Success.Should().BeFalse();
            // Original target should still be there because it failed before move
            File.Exists(Path.Combine(_targetPath, "old.xml")).Should().BeTrue();
        }
    }
}
