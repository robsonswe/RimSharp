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
            _processor = new DownloadedItemProcessor(_mockLogger, _mockModService, Substitute.For<IWorkshopDescriptionService>());

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

            var item = new DownloadItem { SteamId = "123", Name = "Test Mod" };
            File.WriteAllText(Path.Combine(_sourcePath, "About.xml"), "content");

            var result = await _processor.ProcessItemAsync(item, _sourcePath, _targetPath, "_backup", CancellationToken.None);

            result.Success.Should().BeTrue();
            Directory.Exists(_targetPath).Should().BeTrue();
            File.Exists(Path.Combine(_targetPath, "About.xml")).Should().BeTrue();
            Directory.Exists(_sourcePath).Should().BeFalse();
        }

        [Fact]
        public async Task ProcessItemAsync_WhenBackupExists_ShouldCreateBackupAndDeleteOnSuccess()
        {

            var item = new DownloadItem { SteamId = "123", Name = "Test Mod" };
            File.WriteAllText(Path.Combine(_sourcePath, "new.txt"), "new");
            File.WriteAllText(Path.Combine(_targetPath, "old.txt"), "old");

            var result = await _processor.ProcessItemAsync(item, _sourcePath, _targetPath, "_backup", CancellationToken.None);

            result.Success.Should().BeTrue();
            File.Exists(Path.Combine(_targetPath, "new.txt")).Should().BeTrue();

Directory.GetDirectories(_testTempDir).Any(d => d.Contains("_backup")).Should().BeFalse();
        }

        [Fact]
        public async Task ProcessItemAsync_WhenDdsFilesExistInBackup_ShouldPreserveThem()
        {

            var item = new DownloadItem { SteamId = "123", Name = "Test Mod" };

            // Source (New version) has PNG
            File.WriteAllText(Path.Combine(_sourcePath, "texture.png"), "SAME_CONTENT");

            // Target (Old version) has PNG and its DDS
            File.WriteAllText(Path.Combine(_targetPath, "texture.png"), "SAME_CONTENT");
            File.WriteAllText(Path.Combine(_targetPath, "texture.dds"), "dds_content");

            var result = await _processor.ProcessItemAsync(item, _sourcePath, _targetPath, "_backup", CancellationToken.None);

            result.Success.Should().BeTrue();
            File.Exists(Path.Combine(_targetPath, "texture.dds")).Should().BeTrue();
        }

        [Fact]
        public async Task ProcessItemAsync_WhenExceptionDuringMove_ShouldRollback()
        {

            var item = new DownloadItem { SteamId = "123", Name = "Test Mod" };
            File.WriteAllText(Path.Combine(_sourcePath, "new.xml"), "new");
            File.WriteAllText(Path.Combine(_targetPath, "old.xml"), "old");

// but let's try something that definitely fails).

            // Let's mock _modService.CreateTimestampFilesAsync to throw.
            _mockModService.When(x => x.CreateTimestampFilesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()))
                .Do(x => throw new IOException("Simulated Failure"));

            var result = await _processor.ProcessItemAsync(item, _sourcePath, _targetPath, "_backup", CancellationToken.None);

            result.Success.Should().BeFalse();

            File.Exists(Path.Combine(_targetPath, "old.xml")).Should().BeTrue();
        }

        [Fact]
        public async Task ProcessItemAsync_CrossDeviceSource_ShouldFallBackToCopy()
        {
            // Cross-filesystem validation (Unix): /dev/shm is a tmpfs instance
            // distinct from the default temp location on most Linux systems.
            // Directory.Move across devices throws EXDEV ("Invalid cross-device
            // link") and the processor must fall back to a recursive copy.
            // Skipped when no distinct filesystem is available (e.g. Windows).
            string? crossDeviceBase = FindCrossDeviceBase();
            if (crossDeviceBase == null)
            {
                Assert.Skip("No cross-device filesystem available to exercise the copy fallback.");
            }

            var item = new DownloadItem { SteamId = "123", Name = "Test Mod" };
            string crossTestRoot = Path.Combine(crossDeviceBase!, "RimSharp_CrossDevTest_" + Guid.NewGuid().ToString("N"));
            string crossSourcePath = Path.Combine(crossTestRoot, "123");
            string crossTargetPath = Path.Combine(_testTempDir, "CrossTarget");
            try
            {
                Directory.CreateDirectory(crossSourcePath);
                File.WriteAllText(Path.Combine(crossSourcePath, "About.xml"), "content");

                var result = await _processor.ProcessItemAsync(item, crossSourcePath, crossTargetPath, "_backup", CancellationToken.None);

                result.Success.Should().BeTrue();
                Directory.Exists(crossTargetPath).Should().BeTrue();
                File.Exists(Path.Combine(crossTargetPath, "About.xml")).Should().BeTrue();
                Directory.Exists(crossSourcePath).Should().BeFalse();
                Directory.GetDirectories(_testTempDir).Any(d => d.Contains("_staging_")).Should().BeFalse();
            }
            finally
            {
                if (Directory.Exists(crossTestRoot)) Directory.Delete(crossTestRoot, true);
            }
        }

        /// <summary>
        /// Returns a base directory on a filesystem different from the default
        /// temp path, or null when none is available. Probed by attempting a
        /// real Directory.Move between the two locations.
        /// </summary>
        private static string? FindCrossDeviceBase()
        {
            string tempProbeParent = Path.Combine(Path.GetTempPath(), "RimSharp_CrossDevProbe_" + Guid.NewGuid().ToString("N"));
            foreach (string candidate in new[] { "/dev/shm" })
            {
                if (!Directory.Exists(candidate)) continue;
                string candidateProbe = Path.Combine(candidate, "RimSharp_CrossDevProbe_" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(candidateProbe);
                    Directory.CreateDirectory(tempProbeParent);
                    Directory.Move(candidateProbe, Path.Combine(tempProbeParent, "probe"));
                    // Move succeeded: same filesystem, not usable for this test.
                    return null;
                }
                catch (IOException)
                {
                    return candidate;
                }
                finally
                {
                    try { if (Directory.Exists(candidateProbe)) Directory.Delete(candidateProbe, true); } catch { }
                    try { if (Directory.Exists(tempProbeParent)) Directory.Delete(tempProbeParent, true); } catch { }
                }
            }
            return null;
        }
    }
}


