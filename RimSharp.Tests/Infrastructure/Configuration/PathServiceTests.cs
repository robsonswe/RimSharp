using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Configuration
{
    public class PathServiceTests : IDisposable
    {
        private readonly IConfigService _mockConfigService;
        private readonly PathService _service;
        private readonly string _testTempDir;

        public PathServiceTests()
        {
            _mockConfigService = Substitute.For<IConfigService>();
            _service = new PathService(_mockConfigService);
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_" + Guid.NewGuid().ToString("N"));
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
        public void GetGamePath_WhenConfigValueIsValid_ShouldReturnPath()
        {
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            var result = _service.GetGamePath();

            result.Should().Be(_testTempDir);
        }

        [Fact]
        public void GetGamePath_WhenConfigValueIsInvalid_ShouldReturnEmpty()
        {
            _mockConfigService.GetConfigValue("game_folder").Returns(@"C:\NonExistentPath_12345");

            var result = _service.GetGamePath();

            result.Should().BeEmpty();
        }

        [Fact]
        public void GetModsPath_WhenGamePathIsValid_ShouldReturnModsPath()
        {
            var modsPath = Path.Combine(_testTempDir, "Mods");
            Directory.CreateDirectory(modsPath);
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            var result = _service.GetModsPath();

            result.Should().Be(modsPath);
        }

        [Fact]
        public void GetGameVersion_WhenVersionFileExists_ShouldReturnVersion()
        {
            var versionFile = Path.Combine(_testTempDir, "Version.txt");
            File.WriteAllText(versionFile, "1.5.4321");
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            var result = _service.GetGameVersion();

            result.Should().Be("1.5.4321");
        }

        [Fact]
        public void GetMajorGameVersion_ShouldExtractCorrectly()
        {
            var versionFile = Path.Combine(_testTempDir, "Version.txt");
            File.WriteAllText(versionFile, "1.4.3529 rev704");
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            var result = _service.GetMajorGameVersion();

            result.Should().Be("1.4");
        }

        [Fact]
        public void RefreshPaths_ShouldInvalidateCache()
        {
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);
            _service.GetGamePath(); // Populate cache

            var newPath = Path.Combine(_testTempDir, "NewGame");
            Directory.CreateDirectory(newPath);
            _mockConfigService.GetConfigValue("game_folder").Returns(newPath);

            _service.RefreshPaths();
            var result = _service.GetGamePath();

            result.Should().Be(newPath);
        }

        [Fact]
        public void GetModsPath_WhenGamePathDoesNotExist_ShouldReturnEmpty()
        {
            _mockConfigService.GetConfigValue("game_folder").Returns(@"C:\NonExistent_XYZ_123");

            var result = _service.GetModsPath();

            result.Should().BeEmpty();
        }

        [Fact]
        public void GetModsPath_WhenModsDirDoesNotExist_ShouldReturnEmpty()
        {
            // Game dir exists but has no Mods subdirectory
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            var result = _service.GetModsPath();

            result.Should().BeEmpty();
        }

        [Fact]
        public void GetConfigPath_WhenConfigValueIsValid_ShouldReturnPath()
        {
            var configPath = Path.Combine(_testTempDir, "Config");
            Directory.CreateDirectory(configPath);
            _mockConfigService.GetConfigValue("config_folder").Returns(configPath);

            var result = _service.GetConfigPath();

            result.Should().Be(configPath);
        }

        [Fact]
        public void GetConfigPath_WhenConfigValueDoesNotExist_ShouldReturnEmpty()
        {
            _mockConfigService.GetConfigValue("config_folder").Returns(@"C:\NonExistent_XYZ_789");

            var result = _service.GetConfigPath();

            result.Should().BeEmpty();
        }

        [Fact]
        public void GetGameVersion_WhenNoVersionSourceExists_ShouldReturnErrorMessage()
        {
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);
            // No Version.txt and no version-bearing game binaries created

            var result = _service.GetGameVersion();

            result.Should().Be("N/A - No version source found");
        }

        [Fact]
        public void GetGameVersion_WhenVersionTxtMissing_ButBinaryMetadataHasGameVersion_ShouldReturnFallbackVersion()
        {
            var service = new PathService(_mockConfigService, _ => "1.7.4979.109+3371aa813f09");
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);
            // No Version.txt created

            var result = service.GetGameVersion();

            result.Should().Be("1.7.4979.109");
        }

        [Fact]
        public void GetGameVersion_WhenBinaryMetadataIsEngineVersion_ShouldRejectAndReturnNA()
        {
            // Unity builds: RimWorldWin64.exe metadata holds the engine version, not the game version.
            var service = new PathService(_mockConfigService, _ => "2022.3.35f1 (011206c7a712)");
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            var result = service.GetGameVersion();

            result.Should().Be("N/A - No version source found");
        }

        [Fact]
        public void GetGameVersion_WhenVersionTxtExists_ShouldPreferVersionTxtOverBinaryMetadata()
        {
            var versionFile = Path.Combine(_testTempDir, "Version.txt");
            File.WriteAllText(versionFile, "1.5.4321");
            var service = new PathService(_mockConfigService, _ => "9.9.9");
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            var result = service.GetGameVersion();

            result.Should().Be("1.5.4321");
        }

        [Fact]
        public void GetMajorGameVersion_FromBinaryMetadataFallback_ShouldExtractMajorVersion()
        {
            var service = new PathService(_mockConfigService, _ => "1.6.9438.37837");
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            var result = service.GetMajorGameVersion();

            result.Should().Be("1.6");
        }

        [Fact]
        public void GetGameVersion_WhenBinaryMetadataIsUnparseable_ShouldReturnNA()
        {
            var service = new PathService(_mockConfigService, _ => "not-a-version");
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            var result = service.GetGameVersion();

            result.Should().Be("N/A - No version source found");
        }

        [Fact]
        public void GetGameVersion_ShouldUseRimWorldLinuxDataAssemblyCSharpCandidate()
        {
            var managedDirectory = Path.Combine(_testTempDir, "RimWorldLinux_Data", "Managed");
            Directory.CreateDirectory(managedDirectory);
            File.WriteAllBytes(Path.Combine(managedDirectory, "Assembly-CSharp.dll"), new byte[] { 0x00 });

            var probedPaths = new List<string>();
            var service = new PathService(_mockConfigService, path =>
            {
                probedPaths.Add(path);
                return path.Contains("RimWorldLinux_Data") ? "1.6.9676.18020" : null;
            });
            _mockConfigService.GetConfigValue("game_folder").Returns(_testTempDir);

            var result = service.GetGameVersion();

            result.Should().Be("1.6.9676.18020");
            probedPaths.Should().Contain(p => p.Contains(Path.Combine("RimWorldLinux_Data", "Managed", "Assembly-CSharp.dll")));
        }

        [Fact]
        public void GetGameVersion_Game16RealInstall_ShouldDetectMajorVersion()
        {
            var gameRoot = Environment.GetEnvironmentVariable("RIMSHARP_TEST_GAME_16");
            if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return;

            _mockConfigService.GetConfigValue("game_folder").Returns(gameRoot);

            var version = _service.GetGameVersion();
            var major = _service.GetMajorGameVersion();

            version.Should().StartWith("1.");
            major.Should().StartWith("1");
        }
    }

    public class PathServiceModsFolderOverrideTests : IDisposable
    {
        private readonly IConfigService _mockConfigService;
        private readonly PathService _service;
        private readonly string _testTempDir;
        private readonly ILoggerService _mockLogger;

        public PathServiceModsFolderOverrideTests()
        {
            _mockConfigService = Substitute.For<IConfigService>();
            _mockLogger = Substitute.For<ILoggerService>();
            _service = new PathService(_mockConfigService, null, _mockLogger);
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_Override_" + Guid.NewGuid().ToString("N"));
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
        public void GetModsPath_UnsetOverride_ShouldDeriveFromGameFolder()
        {
            var gameDir = Path.Combine(_testTempDir, "Game");
            var modsDir = Path.Combine(gameDir, "Mods");
            Directory.CreateDirectory(gameDir);
            Directory.CreateDirectory(modsDir);

            _mockConfigService.GetConfigValue("game_folder").Returns(gameDir);
            _mockConfigService.GetConfigValue("mods_folder").Returns(string.Empty);

            var result = _service.GetModsPath();
            result.Should().Be(modsDir);
        }

        [Fact]
        public void GetModsPath_ValidOverride_ShouldReturnUserDir()
        {
            var gameDir = Path.Combine(_testTempDir, "Game");
            var overrideDir = Path.Combine(_testTempDir, "MyMods");
            Directory.CreateDirectory(gameDir);
            Directory.CreateDirectory(overrideDir);

            _mockConfigService.GetConfigValue("game_folder").Returns(gameDir);
            _mockConfigService.GetConfigValue("mods_folder").Returns(overrideDir);

            var result = _service.GetModsPath();
            result.Should().Be(overrideDir);
        }

        [Fact]
        public void GetModsPath_BrokenOverride_ShouldWarnAndFallBack()
        {
            var gameDir = Path.Combine(_testTempDir, "Game");
            var fallbackModsDir = Path.Combine(gameDir, "Mods");
            var brokenOverrideDir = Path.Combine(_testTempDir, "DoesNotExist");

            Directory.CreateDirectory(gameDir);
            Directory.CreateDirectory(fallbackModsDir);
            // DO NOT create brokenOverrideDir - it must fail Directory.Exists to trigger the fallback!

            _mockConfigService.GetConfigValue("game_folder").Returns(gameDir);
            _mockConfigService.GetConfigValue("mods_folder").Returns(brokenOverrideDir);

            var result = _service.GetModsPath();
            
            result.Should().Be(fallbackModsDir);
            _mockLogger.Received(1).LogWarning(Arg.Is<string>(msg => msg.Contains(brokenOverrideDir)), Arg.Any<string>());
        }

        [Fact]
        public void GetModsPath_OverrideKeyedOnGamePath_ShouldInvalidateCacheWhenGamePathChanges()
        {
            var firstGameDir = Path.Combine(_testTempDir, "FirstDir");
            var firstModsDir = Path.Combine(firstGameDir, "MyMods");

            var secondGameDir = Path.Combine(_testTempDir, "SecondDir");
            var secondModsDir = Path.Combine(secondGameDir, "Mods");

            Directory.CreateDirectory(firstGameDir);
            Directory.CreateDirectory(firstModsDir);
            Directory.CreateDirectory(secondGameDir);
            Directory.CreateDirectory(secondModsDir);

            // First call -> Setup Initial Path
            _mockConfigService.GetConfigValue("game_folder").Returns(firstGameDir);
            _mockConfigService.GetConfigValue("mods_folder").Returns(firstModsDir);

            var firstResult = _service.GetModsPath();
            firstResult.Should().Be(firstModsDir);

            // Second call -> Change game path + Reset override
            _mockConfigService.GetConfigValue("game_folder").Returns(secondGameDir);
            _mockConfigService.GetConfigValue("mods_folder").Returns(string.Empty);
            
            _service.RefreshPaths(); // Necessary to clear '_isGamePathResolved' inside PathService

            var secondResult = _service.GetModsPath();
            secondResult.Should().Be(secondModsDir);
        }
    }

    public class PathServiceMacOSBundleTests : IDisposable
    {
        private readonly IConfigService _mockConfigService;
        private readonly PathService _service;
        private readonly string _testTempDir;

        public PathServiceMacOSBundleTests()
        {
            _mockConfigService = Substitute.For<IConfigService>();
            _service = new PathService(_mockConfigService);
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_MacBundle_" + Guid.NewGuid().ToString("N"));
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
        public void GetModsPathFor_BundleWithContentsMods_ShouldPickContentsMods()
        {
            // Arrange: create a synthetic .app bundle with Contents/Mods
            var bundlePath = Path.Combine(_testTempDir, "RimWorld.app");
            var contentsMods = Path.Combine(bundlePath, "Contents", "Mods");
            var rootMods = Path.Combine(bundlePath, "Mods");
            Directory.CreateDirectory(contentsMods);
            Directory.CreateDirectory(rootMods);

            // Act
            var result = _service.GetModsPathFor(bundlePath);

            // Assert: should prefer Contents/Mods
            result.Should().Be(contentsMods);
        }

        [Fact]
        public void GetModsPathFor_BundleWithOnlyRootMods_ShouldPickRootMods()
        {
            // Arrange: create a synthetic .app bundle with only bundle-root Mods
            var bundlePath = Path.Combine(_testTempDir, "RimWorld.app");
            var rootMods = Path.Combine(bundlePath, "Mods");
            Directory.CreateDirectory(rootMods);
            // Contents/Mods does NOT exist

            // Act
            var result = _service.GetModsPathFor(bundlePath);

            // Assert: should fall back to bundle-root Mods
            result.Should().Be(rootMods);
        }

        [Fact]
        public void GetModsPathFor_BundleWithNeitherMods_ShouldReturnRootMods()
        {
            // Arrange: create a synthetic .app bundle with neither Contents/Mods nor root Mods
            var bundlePath = Path.Combine(_testTempDir, "RimWorld.app");
            Directory.CreateDirectory(bundlePath);
            // Neither Contents/Mods nor root Mods exists

            // Act
            var result = _service.GetModsPathFor(bundlePath);

            // Assert: deterministic fallback is bundle-root Mods (not Contents/Mods)
            var expected = Path.Combine(bundlePath, "Mods");
            result.Should().Be(expected);
        }

        [Fact]
        public void GetModsPathFor_NonBundlePath_ShouldReturnLegacyCombine()
        {
            // Arrange: regular (non-.app) game path
            var gamePath = Path.Combine(_testTempDir, "RimWorld");
            Directory.CreateDirectory(gamePath);

            // Act
            var result = _service.GetModsPathFor(gamePath);

            // Assert: byte-identical to Path.Combine(gamePath, "Mods")
            result.Should().Be(Path.Combine(gamePath, "Mods"));
        }

        [Fact]
        public void GetModsPathFor_EmptyPath_ShouldReturnEmpty()
        {
            // Act
            var result = _service.GetModsPathFor(string.Empty);

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void GetGameDataPathFor_BundleWithContentsData_ShouldPickContentsData()
        {
            // Arrange: create a synthetic .app bundle with Contents/Data
            var bundlePath = Path.Combine(_testTempDir, "RimWorld.app");
            var contentsData = Path.Combine(bundlePath, "Contents", "Data");
            var contentsResourcesData = Path.Combine(bundlePath, "Contents", "Resources", "Data");
            var rootData = Path.Combine(bundlePath, "Data");
            Directory.CreateDirectory(contentsData);
            Directory.CreateDirectory(contentsResourcesData);
            Directory.CreateDirectory(rootData);

            // Act
            var result = _service.GetGameDataPathFor(bundlePath);

            // Assert: should prefer Contents/Data (first in priority order)
            result.Should().Be(contentsData);
        }

        [Fact]
        public void GetGameDataPathFor_BundleWithContentsResourcesData_ShouldPickContentsResourcesData()
        {
            // Arrange: create a synthetic .app bundle with Contents/Resources/Data (but no Contents/Data)
            var bundlePath = Path.Combine(_testTempDir, "RimWorld.app");
            var contentsResourcesData = Path.Combine(bundlePath, "Contents", "Resources", "Data");
            var rootData = Path.Combine(bundlePath, "Data");
            Directory.CreateDirectory(contentsResourcesData);
            Directory.CreateDirectory(rootData);
            // Contents/Data does NOT exist

            // Act
            var result = _service.GetGameDataPathFor(bundlePath);

            // Assert: should pick Contents/Resources/Data (second in priority order)
            result.Should().Be(contentsResourcesData);
        }

        [Fact]
        public void GetGameDataPathFor_BundleWithOnlyRootData_ShouldPickRootData()
        {
            // Arrange: create a synthetic .app bundle with only bundle-root Data
            var bundlePath = Path.Combine(_testTempDir, "RimWorld.app");
            var rootData = Path.Combine(bundlePath, "Data");
            Directory.CreateDirectory(rootData);
            // Neither Contents/Data nor Contents/Resources/Data exists

            // Act
            var result = _service.GetGameDataPathFor(bundlePath);

            // Assert: should pick bundle-root Data (third in priority order)
            result.Should().Be(rootData);
        }

        [Fact]
        public void GetGameDataPathFor_BundleWithNoData_ShouldReturnLegacyFallback()
        {
            // Arrange: create a synthetic .app bundle with no Data folders at all
            var bundlePath = Path.Combine(_testTempDir, "RimWorld.app");
            Directory.CreateDirectory(bundlePath);

            // Act
            var result = _service.GetGameDataPathFor(bundlePath);

            // Assert: should return legacy non-checking fallback <game>/Data
            var expected = Path.Combine(bundlePath, "Data");
            result.Should().Be(expected);
        }

        [Fact]
        public void GetGameDataPathFor_NonBundlePath_ShouldReturnLegacyCombine()
        {
            // Arrange: regular (non-.app) game path
            var gamePath = Path.Combine(_testTempDir, "RimWorld");
            Directory.CreateDirectory(gamePath);

            // Act
            var result = _service.GetGameDataPathFor(gamePath);

            // Assert: byte-identical to Path.Combine(gamePath, "Data")
            result.Should().Be(Path.Combine(gamePath, "Data"));
        }

        [Fact]
        public void GetGameDataPathFor_EmptyPath_ShouldReturnEmpty()
        {
            // Act
            var result = _service.GetGameDataPathFor(string.Empty);

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void GetModsPathFor_CaseInsensitiveAppExtension_ShouldWork()
        {
            // Arrange: .APP, .App, .aPp should all be treated as bundles
            var bundlePathUpper = Path.Combine(_testTempDir, "RimWorld.APP");
            var bundlePathMixed = Path.Combine(_testTempDir, "RimWorld.App");
            var contentsModsUpper = Path.Combine(bundlePathUpper, "Contents", "Mods");
            var contentsModsMixed = Path.Combine(bundlePathMixed, "Contents", "Mods");
            Directory.CreateDirectory(contentsModsUpper);
            Directory.CreateDirectory(contentsModsMixed);

            // Act
            var resultUpper = _service.GetModsPathFor(bundlePathUpper);
            var resultMixed = _service.GetModsPathFor(bundlePathMixed);

            // Assert
            resultUpper.Should().Be(contentsModsUpper);
            resultMixed.Should().Be(contentsModsMixed);
        }

        [Fact]
        public void GetModsPathFor_DirectoryExistsProbe_UsesTestSeam()
        {
            // Arrange: create a bundle but use a custom probe that always returns false
            var bundlePath = Path.Combine(_testTempDir, "RimWorld.app");
            var contentsMods = Path.Combine(bundlePath, "Contents", "Mods");
            Directory.CreateDirectory(contentsMods);

            // Create service with probe that always returns false (simulating missing dirs)
            var serviceWithFalseProbe = new PathService(_mockConfigService, null, null, _ => false);

            // Act
            var result = serviceWithFalseProbe.GetModsPathFor(bundlePath);

            // Assert: probe returns false for Contents/Mods, so falls back to root Mods
            var expected = Path.Combine(bundlePath, "Mods");
            result.Should().Be(expected);
        }
    }
}