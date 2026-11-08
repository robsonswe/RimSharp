using System;
using System.IO;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Configuration
{
    /// <summary>
    /// Tests for the steam_workshop_folder override semantics applied through
    /// SteamDetectionService (the single writer of GameInstance.WorkshopFolder):
    /// empty = keep detected; valid = use; invalid = warn + fall back, never clear.
    /// </summary>
    public class SteamDetectionServiceTests : IDisposable
    {
        private readonly GameInstance _gameInstance;
        private readonly SteamDetectionService _service;

        private readonly string _testTempDir;
        private readonly string _detectedWorkshopDir;   // simulated detection result
        private readonly string _validOverrideDir;      // real dir with appworkshop acf
        private readonly string _invalidOverrideDir;    // real dir WITHOUT acf

        public SteamDetectionServiceTests()
        {
            var mockPathService = Substitute.For<IPathService>();
            var mockLogger = Substitute.For<ILoggerService>();
            _service = new SteamDetectionService(mockPathService, mockLogger);
            _gameInstance = new GameInstance();

            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);

            _detectedWorkshopDir = Path.Combine(_testTempDir, "steamapps", "workshop", "content", "294100");
            Directory.CreateDirectory(_detectedWorkshopDir);
            // The detected folder is fully valid (the normal detection outcome)
            File.WriteAllText(
                Path.Combine(_detectedWorkshopDir, "appworkshop_294100.acf"),
                "\"appworkshop_294100\" { }");

            _validOverrideDir = Path.Combine(_testTempDir, "Override_Workshop_Valid");
            Directory.CreateDirectory(_validOverrideDir);
            File.WriteAllText(
                Path.Combine(_validOverrideDir, "appworkshop_294100.acf"),
                "\"appworkshop_294100\" { }");

            _invalidOverrideDir = Path.Combine(_testTempDir, "Override_Workshop_Invalid");
            Directory.CreateDirectory(_invalidOverrideDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        private void SeedDetectedWorkshopFolder()
        {
            _gameInstance.WorkshopFolder = _detectedWorkshopDir;
        }

        [Fact]
        public void ApplyWorkshopFolderOverride_WhenEmpty_ShouldKeepDetectedFolder()
        {
            // Auto mode: empty config = detection re-runs every launch; detected value stands
            SeedDetectedWorkshopFolder();

            _service.ApplyWorkshopFolderOverride(_gameInstance, string.Empty);

            _gameInstance.WorkshopFolder.Should().Be(_detectedWorkshopDir);
        }

        [Fact]
        public void ApplyWorkshopFolderOverride_WhenNull_ShouldKeepDetectedFolder()
        {
            SeedDetectedWorkshopFolder();

            _service.ApplyWorkshopFolderOverride(_gameInstance, null);

            _gameInstance.WorkshopFolder.Should().Be(_detectedWorkshopDir);
        }

        [Fact]
        public void ApplyWorkshopFolderOverride_WhenValid_ShouldUseOverride()
        {
            SeedDetectedWorkshopFolder();

            _service.ApplyWorkshopFolderOverride(_gameInstance, _validOverrideDir);

            _gameInstance.WorkshopFolder.Should().Be(_validOverrideDir);
        }

        [Fact]
        public void ApplyWorkshopFolderOverride_WhenValidWithoutAcfDetectedFolder_ShouldStillUseOverride()
        {
            // Override wins even when the detected folder exists but has no ACF file
            var detectedWithoutAcf = Path.Combine(_testTempDir, "steamapps", "workshop", "content_other");
            Directory.CreateDirectory(detectedWithoutAcf);
            _gameInstance.WorkshopFolder = detectedWithoutAcf;

            _service.ApplyWorkshopFolderOverride(_gameInstance, _validOverrideDir);

            _gameInstance.WorkshopFolder.Should().Be(_validOverrideDir);
        }

        [Fact]
        public void ApplyWorkshopFolderOverride_WhenFolderExistsButAcfMissing_ShouldFallBackToDetected()
        {
            SeedDetectedWorkshopFolder();

            _service.ApplyWorkshopFolderOverride(_gameInstance, _invalidOverrideDir);

            // Fail-open: bypassed at runtime, detection result retained
            _gameInstance.WorkshopFolder.Should().Be(_detectedWorkshopDir);
        }

        [Fact]
        public void ApplyWorkshopFolderOverride_WhenFolderDoesNotExist_ShouldFallBackToDetected()
        {
            SeedDetectedWorkshopFolder();
            var missing = Path.Combine(_testTempDir, "No_Such_Dir_XYZ");

            _service.ApplyWorkshopFolderOverride(_gameInstance, missing);

            _gameInstance.WorkshopFolder.Should().Be(_detectedWorkshopDir);
        }

        [Fact]
        public void ApplyWorkshopFolderOverride_WhenInvalid_ShouldWarn()
        {
            var mockLogger = Substitute.For<ILoggerService>();
            var service = new SteamDetectionService(Substitute.For<IPathService>(), mockLogger);
            SeedDetectedWorkshopFolder();

            service.ApplyWorkshopFolderOverride(_gameInstance, _invalidOverrideDir);

            mockLogger.Received(1).LogWarning(
                Arg.Is<string>(s => s.Contains("invalid") && s.Contains(_invalidOverrideDir)),
                Arg.Any<string>());
        }

        [Fact]
        public void ApplyWorkshopFolderOverride_WhenValid_ShouldLogInfo()
        {
            var mockLogger = Substitute.For<ILoggerService>();
            var service = new SteamDetectionService(Substitute.For<IPathService>(), mockLogger);
            SeedDetectedWorkshopFolder();

            service.ApplyWorkshopFolderOverride(_gameInstance, _validOverrideDir);

            mockLogger.Received(1).LogInfo(
                Arg.Is<string>(s => s.Contains("valid") && s.Contains(_validOverrideDir)),
                Arg.Any<string>());
        }

        [Fact]
        public void ApplyWorkshopFolderOverride_WhenNullInstance_ShouldThrow()
        {
            Action act = () => _service.ApplyWorkshopFolderOverride(null!, _validOverrideDir);

            act.Should().Throw<ArgumentNullException>();
        }
    }

    public class SteamDetectionServiceMacOSBundleTests : IDisposable
    {
        private readonly string _testTempDir;

        public SteamDetectionServiceMacOSBundleTests()
        {
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_SteamBundle_" + Guid.NewGuid().ToString("N"));
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
        public void GetModsPathForFallback_BundleWithContentsMods_ShouldUseContentsMods()
        {
            // Arrange: synthetic .app bundle with Contents/Mods
            var bundlePath = Path.Combine(_testTempDir, "RimWorld.app");
            var contentsMods = Path.Combine(bundlePath, "Contents", "Mods");
            var rootMods = Path.Combine(bundlePath, "Mods");
            Directory.CreateDirectory(contentsMods);
            Directory.CreateDirectory(rootMods);

            // Act
            var result = SteamDetectionService.GetModsPathForFallback(bundlePath);

            // Assert: should prefer Contents/Mods
            result.Should().Be(contentsMods);
        }

        [Fact]
        public void GetModsPathForFallback_BundleWithOnlyRootMods_ShouldUseRootMods()
        {
            // Arrange: synthetic .app bundle with only bundle-root Mods
            var bundlePath = Path.Combine(_testTempDir, "RimWorld.app");
            var rootMods = Path.Combine(bundlePath, "Mods");
            Directory.CreateDirectory(rootMods);
            // Contents/Mods does NOT exist

            // Act
            var result = SteamDetectionService.GetModsPathForFallback(bundlePath);

            // Assert: should fall back to bundle-root Mods
            result.Should().Be(rootMods);
        }

        [Fact]
        public void GetModsPathForFallback_NonBundlePath_ShouldUseLegacyCombine()
        {
            // Arrange: regular (non-.app) game path
            var gamePath = Path.Combine(_testTempDir, "RimWorld");
            Directory.CreateDirectory(gamePath);

            // Act
            var result = SteamDetectionService.GetModsPathForFallback(gamePath);

            // Assert: should be Path.Combine(gamePath, "Mods") (legacy)
            result.Should().Be(Path.Combine(gamePath, "Mods"));
        }

        [Fact]
        public void GetModsPathForFallback_BundleCaseInsensitive_ShouldWork()
        {
            // Arrange: .APP extension should be treated as bundle
            var bundlePath = Path.Combine(_testTempDir, "RimWorld.APP");
            var contentsMods = Path.Combine(bundlePath, "Contents", "Mods");
            Directory.CreateDirectory(contentsMods);

            // Act
            var result = SteamDetectionService.GetModsPathForFallback(bundlePath);

            // Assert
            result.Should().Be(contentsMods);
        }

        [Fact]
        public void GetModsPathForFallback_EmptyPath_ShouldReturnEmpty()
        {
            // Act
            var result = SteamDetectionService.GetModsPathForFallback(string.Empty);

            // Assert
            result.Should().BeEmpty();
        }
    }
}
