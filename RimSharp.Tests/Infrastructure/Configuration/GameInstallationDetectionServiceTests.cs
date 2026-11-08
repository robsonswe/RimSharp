using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using RimSharp.Infrastructure.Configuration;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Configuration
{
    /// <summary>
    /// Structural validation of manual Steam claims (SteamClaimValidationResult):
    /// layout under &lt;library&gt;\steamapps\common\RimWorld, Steam
    /// manifest present, and the workshop ACF file validating.
    /// </summary>
    public class GameInstallationDetectionServiceTests : IDisposable
    {
        private readonly GameInstallationDetectionService _service;
        private readonly string _tempRoot;

        private readonly string _validGameDir;          // steamapps\common\RimWorld (full valid layout)
        private readonly string _noContentWorkshopDir;  // valid layout WITHOUT content\294100 on disk
        private readonly string _noManifestRoot;        // steamapps lacking appmanifest_294100.acf
        private readonly string _noWorkshopAcfRoot;     // steamapps lacking workshop acf
        private readonly string _nonSteamDir;           // RimWorld folder not under steamapps\common

        private readonly string _renamedGameDir;        // renamed install folder (manifest-installdir match)
        private readonly string _renamedWorkshopContent;

        public GameInstallationDetectionServiceTests()
        {
            _service = new GameInstallationDetectionService();
            _tempRoot = Path.Combine(Path.GetTempPath(), "RimSharpTests_" + Guid.NewGuid().ToString("N"));

            // Fully valid Steam layout (Unity-era: RimWorldWin64.exe + installdir "RimWorld")
            _validGameDir = Path.Combine(_tempRoot, "Lib1", "steamapps", "common", "RimWorld");
            MarkAsRimWorldInstallation(_validGameDir);
            WriteSteamManifest("Lib1", installdir: "RimWorld");
            WriteWorkshopAcf("Lib1");

            var workshopContent = Path.Combine(_tempRoot, "Lib1", "steamapps", "workshop", "content", "294100");
            Directory.CreateDirectory(workshopContent);

            // Valid except no workshop content folder at all (no mods downloaded yet)
            _noContentWorkshopDir = Path.Combine(_tempRoot, "Lib2", "steamapps", "common", "RimWorld");
            MarkAsRimWorldInstallation(_noContentWorkshopDir);
            WriteSteamManifest("Lib2", installdir: "RimWorld");
            WriteWorkshopAcf("Lib2");

            // Steam layout missing the RimWorld app manifest (DRM-free copied layout)
            _noManifestRoot = Path.Combine(_tempRoot, "Lib3", "steamapps", "common", "RimWorld");
            MarkAsRimWorldInstallation(_noManifestRoot);
            WriteWorkshopAcf("Lib3");

            // Steam layout missing the workshop ACF file
            _noWorkshopAcfRoot = Path.Combine(_tempRoot, "Lib4", "steamapps", "common", "RimWorld");
            MarkAsRimWorldInstallation(_noWorkshopAcfRoot);
            WriteSteamManifest("Lib4", installdir: "RimWorld");

            // Looks like RimWorld but is not under steamapps\common (DRM-free install)
            _nonSteamDir = Path.Combine(_tempRoot, "GOG Games", "RimWorld");
            MarkAsRimWorldInstallation(_nonSteamDir);

            // Future-proofing library: renamed Steam install folder + the Waylith-era
            // executable name ("RimWorld.exe", not RimWorldWin64.exe)
            _renamedGameDir = Path.Combine(_tempRoot, "Lib6", "steamapps", "common", "WaylithBuild");
            MarkAsRimWorldInstallation(_renamedGameDir, "RimWorld.exe");
            WriteSteamManifest("Lib6", installdir: "WaylithBuild");
            WriteWorkshopAcf("Lib6");
            var renamedContentDir = Path.Combine(_tempRoot, "Lib6", "steamapps", "workshop", "content", "294100");
            Directory.CreateDirectory(renamedContentDir);
            _renamedWorkshopContent = renamedContentDir;
        }

        /// <summary>
        /// IsRimWorldInstallation requires an executable plus a parsable Version.txt
        /// (or Data\Core\About\About.xml) — this creates the minimal passing layout.
        /// </summary>
        private static void MarkAsRimWorldInstallation(string gameDir, string exeName = "RimWorldWin64.exe")
        {
            Directory.CreateDirectory(gameDir);
            File.WriteAllText(Path.Combine(gameDir, exeName), "");
            File.WriteAllText(Path.Combine(gameDir, "Version.txt"), "1.6.4518 rev1234");
        }

        /// <summary>
        /// Writes a realistic appmanifest_294100.acf (plain key-value text shaped like
        /// Steam's: one "installdir" value per line) into &lt;lib&gt;\steamapps.
        /// </summary>
        private void WriteSteamManifest(string libName, string installdir)
        {
            var steamappsDir = Path.Combine(_tempRoot, libName, "steamapps");
            Directory.CreateDirectory(steamappsDir);
            File.WriteAllText(
                Path.Combine(steamappsDir, "appmanifest_294100.acf"),
                "\"AppState\"\n{\n\t\"appid\"\t\t\"294100\"\n\t\"name\"\t\t\"RimWorld\"\n\t\"installdir\"\t\t\"" + installdir + "\"\n}\n");
        }

        /// <summary>Writes a &lt;lib&gt;\steamapps\workshop\appworkshop_294100.acf marker file (creates parents).</summary>
        private void WriteWorkshopAcf(string libName)
        {
            var workshopDir = Path.Combine(_tempRoot, libName, "steamapps", "workshop");
            Directory.CreateDirectory(workshopDir);
            File.WriteAllText(Path.Combine(workshopDir, "appworkshop_294100.acf"), "\"appworkshop_294100\"{}");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        [Fact]
        public void ValidateSteamClaim_WhenFullValidLayout_ShouldReturnValidWithWorkshopFolder()
        {
            var result = _service.ValidateSteamClaim(_validGameDir);

            result.IsValid.Should().BeTrue();
            result.FailureReason.Should().BeEmpty();
            result.WorkshopFolder.Should().Be(Path.Combine(_tempRoot, "Lib1", "steamapps", "workshop", "content", "294100"));
        }

        [Fact]
        public void ValidateSteamClaim_WhenWorkshopContentDirMissing_ShouldFail()
        {
            // Valid layout but zero workshop mods downloaded: the content folder does not
            // exist, so there is nothing to validate against GameInstance.ValidateAndFix
            // semantics (which require an existing workshop folder). The claim is refused;
            // the user must download at least one Workshop mod once.
            var result = _service.ValidateSteamClaim(_noContentWorkshopDir);

            result.IsValid.Should().BeFalse();
            result.FailureReason.Should().Contain("appworkshop_294100.acf");
        }

        [Fact]
        public void ValidateSteamClaim_WhenAppManifestMissing_ShouldFailWithManifestReason()
        {
            var result = _service.ValidateSteamClaim(_noManifestRoot);

            result.IsValid.Should().BeFalse();
            result.FailureReason.Should().Contain("appmanifest_294100.acf");
        }

        [Fact]
        public void ValidateSteamClaim_WhenWorkshopAcfMissing_ShouldFailWithWorkshopReason()
        {
            var result = _service.ValidateSteamClaim(_noWorkshopAcfRoot);

            result.IsValid.Should().BeFalse();
            result.FailureReason.Should().Contain("appworkshop_294100.acf");
        }

        [Fact]
        public void ValidateSteamClaim_WhenNotUnderSteamappsCommon_ShouldFail()
        {
            var result = _service.ValidateSteamClaim(_nonSteamDir);

            result.IsValid.Should().BeFalse();
            result.FailureReason.Should().Contain("steamapps");
        }

        [Fact]
        public void ValidateSteamClaim_WhenUnderSteamappsButWrongCommonChild_ShouldFail()
        {
            // RimWorld folder nested under a differently-shaped library: the steamapps
            // ancestor exists but common\RimWorld does not match the claimed path
            var wrongLayout = Path.Combine(_tempRoot, "Lib5", "steamapps", "common", "OtherGame", "RimWorld");
            Directory.CreateDirectory(wrongLayout);
            File.WriteAllText(Path.Combine(wrongLayout, "Version.txt"), "1.6.4518 rev1234");
            File.WriteAllText(Path.Combine(_tempRoot, "Lib5", "steamapps", "appmanifest_294100.acf"), "\"294100\"{}");

            var result = _service.ValidateSteamClaim(wrongLayout);

            result.IsValid.Should().BeFalse();
        }

        [Fact]
        public void ValidateSteamClaim_WhenNotARimWorldInstallation_ShouldFail()
        {
            var emptyDir = Path.Combine(_tempRoot, "Empty", "SomeGame");
            Directory.CreateDirectory(emptyDir);

            var result = _service.ValidateSteamClaim(emptyDir);

            result.IsValid.Should().BeFalse();
            result.FailureReason.Should().Contain("does not look like a RimWorld installation");
        }

        [Fact]
        public void ValidateSteamClaim_WhenPathDoesNotExist_ShouldFail()
        {
            var result = _service.ValidateSteamClaim(Path.Combine(_tempRoot, "No", "Such", "Dir", "RimWorld"));
            result.IsValid.Should().BeFalse();
        }

        [Fact]
        public void ValidateSteamClaim_WhenNull_ShouldFail()
        {
            var result = _service.ValidateSteamClaim(null);
            result.IsValid.Should().BeFalse();
        }

        [Fact]
        public void ValidateSteamClaim_WhenEmpty_ShouldFail()
        {
            var result = _service.ValidateSteamClaim(string.Empty);
            result.IsValid.Should().BeFalse();
        }

        [Fact]
        public void ValidateSteamClaim_WhenInstallFolderRenamed_ShouldSucceed()
        {
            // Future-proofing regression: the Steam-managed install folder may not stay
            // "RimWorld" (RimWorld 1.7/Waylith already reshaped the install). The manifest's
            // own "installdir" record, not the literal folder name, proves the claim.
            var result = _service.ValidateSteamClaim(_renamedGameDir);

            result.IsValid.Should().BeTrue();
            result.WorkshopFolder.Should().Be(_renamedWorkshopContent);
        }

        [Fact]
        public void ValidateSteamClaim_WhenInstalldirMismatch_ShouldFail()
        {
            // Manifest says the game lives in "RimWorld"; the claimed folder says otherwise
            var gameDir = Path.Combine(_tempRoot, "Lib7", "steamapps", "common", "SomeOtherFolder");
            MarkAsRimWorldInstallation(gameDir);
            WriteSteamManifest("Lib7", installdir: "RimWorld");

            var result = _service.ValidateSteamClaim(gameDir);

            result.IsValid.Should().BeFalse();
            result.FailureReason.Should().Contain("installdir");
        }

        [Fact]
        public void TryFindRimWorldViaManifest_WhenInstalldirRecorded_ShouldReturnGameFolder()
        {
            var steamappsDir = Path.Combine(_tempRoot, "Lib1", "steamapps");

            var result = _service.TryFindRimWorldViaManifest(steamappsDir);

            result.Should().NotBeNull();
            result.Should().Be(_validGameDir);
        }

        [Fact]
        public void TryFindRimWorldViaManifest_WhenRenamedInstalldir_ShouldStillFindGame()
        {
            // The detection fallback: even if the install folder were ever renamed away
            // from "RimWorld", the manifest record points at it
            var result = _service.TryFindRimWorldViaManifest(Path.Combine(_tempRoot, "Lib6", "steamapps"));

            result.Should().Be(_renamedGameDir);
        }

        [Fact]
        public void TryFindRimWorldViaManifest_WhenGameFolderIsNotRimWorld_ShouldReturnNull()
        {
            // Manifest points somewhere that holds no RimWorld markers
            var emptyGameDir = Path.Combine(_tempRoot, "Lib8", "steamapps", "common", "NotRimWorld");
            Directory.CreateDirectory(emptyGameDir);
            WriteSteamManifest("Lib8", installdir: "NotRimWorld");

            var result = _service.TryFindRimWorldViaManifest(Path.Combine(_tempRoot, "Lib8", "steamapps"));

            result.Should().BeNull();
        }

        [Fact]
        public void TryFindRimWorldViaManifest_WhenNoManifest_ShouldReturnNull()
        {
            var result = _service.TryFindRimWorldViaManifest(Path.Combine(_tempRoot, "Lib3", "steamapps"));
            result.Should().BeNull();
        }

        [Fact]
        public void TryFindRimWorldViaManifest_WhenNull_ShouldReturnNull()
        {
            _service.TryFindRimWorldViaManifest(null).Should().BeNull();
        }
    }

    /// <summary>
    /// Tests for the scored config folder selection algorithm.
    /// Uses a testable subclass to inject custom candidate enumeration via temp directories.
    /// </summary>
    public class ConfigFolderScoringTests : IDisposable
    {
        private readonly string _tempRoot;
        private readonly TestableDetectionService _service;

        public ConfigFolderScoringTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "RimSharpConfigTests_" + Guid.NewGuid().ToString("N"));
            _service = new TestableDetectionService(_tempRoot);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        /// <summary>
        /// Testable subclass that allows injecting custom config folder candidates.
        /// </summary>
        private class TestableDetectionService : GameInstallationDetectionService
        {
            private readonly string _testRoot;
            private readonly List<string> _injectedCandidates = new();

            public TestableDetectionService(string testRoot)
            {
                _testRoot = testRoot;
            }

            public void AddCandidate(string configPath)
            {
                _injectedCandidates.Add(configPath);
            }

            // Override to return injected candidates instead of real filesystem enumeration
            protected override IEnumerable<string> EnumerateConfigCandidates(string? steamRoot)
            {
                return _injectedCandidates.Where(c => Directory.Exists(c));
            }
        }

        private static void CreateConfigFolder(string path, bool withModsConfig = true, DateTime? modsConfigTime = null, int saveCount = 0, string userFolderName = "user")
        {
            Directory.CreateDirectory(path);
            if (withModsConfig)
            {
                var modsConfigPath = Path.Combine(path, "ModsConfig.xml");
                File.WriteAllText(modsConfigPath, "<ModsConfig />");
                if (modsConfigTime.HasValue)
                {
                    File.SetLastWriteTimeUtc(modsConfigPath, modsConfigTime.Value);
                }
            }

            if (saveCount > 0)
            {
                var savesPath = Path.Combine(path, "Saves");
                Directory.CreateDirectory(savesPath);
                for (int i = 0; i < saveCount; i++)
                {
                    File.WriteAllText(Path.Combine(savesPath, $"save{i}.rws"), "save data");
                }
            }
        }

        [Fact]
        public void ScoreCandidates_TwoCandidatesWithModsConfig_NewerMtimeWins()
        {
            // Arrange
            var now = DateTime.UtcNow;
            var candidate1 = Path.Combine(_tempRoot, "Config1");
            var candidate2 = Path.Combine(_tempRoot, "Config2");

            // Candidate 1: older ModsConfig.xml
            CreateConfigFolder(candidate1, withModsConfig: true, modsConfigTime: now.AddDays(-5));
            // Candidate 2: newer ModsConfig.xml
            CreateConfigFolder(candidate2, withModsConfig: true, modsConfigTime: now.AddHours(-2));

            _service.AddCandidate(candidate1);
            _service.AddCandidate(candidate2);

            // Act
            var result = _service.ResolveBestConfigFolder(null);

            // Assert
            result.Should().Be(candidate2); // Newer mtime should win
        }

        [Fact]
        public void ScoreCandidates_CandidateWithModsConfigBeatsEmptyCandidate()
        {
            // Arrange
            var candidate1 = Path.Combine(_tempRoot, "WithModsConfig");
            var candidate2 = Path.Combine(_tempRoot, "EmptyConfig");

            // Candidate 1: has ModsConfig.xml (old)
            CreateConfigFolder(candidate1, withModsConfig: true, modsConfigTime: DateTime.UtcNow.AddDays(-30));
            // Candidate 2: empty directory, no ModsConfig.xml
            CreateConfigFolder(candidate2, withModsConfig: false);

            _service.AddCandidate(candidate1);
            _service.AddCandidate(candidate2);

            // Act
            var result = _service.ResolveBestConfigFolder(null);

            // Assert
            result.Should().Be(candidate1); // +100 for ModsConfig.xml should beat 0
        }

        [Fact]
        public void ScoreCandidates_SavesBonusBeatsEqualRecencyNoSaves()
        {
            // Arrange
            var now = DateTime.UtcNow;
            var candidate1 = Path.Combine(_tempRoot, "WithSaves");
            var candidate2 = Path.Combine(_tempRoot, "NoSaves");

            // Both have ModsConfig.xml with same recency
            CreateConfigFolder(candidate1, withModsConfig: true, modsConfigTime: now.AddHours(-2), saveCount: 5);
            CreateConfigFolder(candidate2, withModsConfig: true, modsConfigTime: now.AddHours(-2), saveCount: 0);

            _service.AddCandidate(candidate1);
            _service.AddCandidate(candidate2);

            // Act
            var result = _service.ResolveBestConfigFolder(null);

            // Assert
            result.Should().Be(candidate1); // Saves bonus (+20+5) should win
        }

        [Fact]
        public void ScoreCandidates_SteamUserFolderBonus()
        {
            // Arrange
            var now = DateTime.UtcNow;
            // Create paths that simulate the steamuser folder structure
            // .../users/steamuser/AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Config
            var steamUserConfig = Path.Combine(_tempRoot, "users", "steamuser", "AppData", "LocalLow", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config");
            var regularUserConfig = Path.Combine(_tempRoot, "users", "regular", "AppData", "LocalLow", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config");

            CreateConfigFolder(steamUserConfig, withModsConfig: true, modsConfigTime: now.AddHours(-2));
            CreateConfigFolder(regularUserConfig, withModsConfig: true, modsConfigTime: now.AddHours(-2));

            _service.AddCandidate(steamUserConfig);
            _service.AddCandidate(regularUserConfig);

            // Act
            var result = _service.ResolveBestConfigFolder(null);

            // Assert
            result.Should().Be(steamUserConfig); // steamuser bonus (+5) should win
        }

        [Fact]
        public void ScoreCandidates_ScoreZeroFallbackToLegacyDefault()
        {
            // Arrange - no candidates with ModsConfig.xml or saves
            var candidate1 = Path.Combine(_tempRoot, "Empty1");
            var candidate2 = Path.Combine(_tempRoot, "Empty2");

            CreateConfigFolder(candidate1, withModsConfig: false);
            CreateConfigFolder(candidate2, withModsConfig: false);

            _service.AddCandidate(candidate1);
            _service.AddCandidate(candidate2);

            // Act
            var result = _service.ResolveBestConfigFolder(null);

            // Assert - should fall back to legacy default (ResolveConfigFolderForSteamRoot)
            var legacyDefault = GameInstallationDetectionService.ResolveConfigFolderForSteamRoot(null);
            result.Should().Be(legacyDefault);
        }

        [Fact]
        public void ScoreCandidates_ProtonEnumerationAllAppIds_RecentWinsRegardlessOfAppId()
        {
            // Arrange - simulate Proton compatdata with two appids
            var now = DateTime.UtcNow;
            var steamRoot = Path.Combine(_tempRoot, "SteamRoot");
            
            // AppId 123456 with newer ModsConfig.xml
            var appId123456Config = Path.Combine(steamRoot, "steamapps", "compatdata", "123456", "pfx", "drive_c", "users", "steamuser", "AppData", "LocalLow", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config");
            // AppId 294100 with older ModsConfig.xml
            var appId294100Config = Path.Combine(steamRoot, "steamapps", "compatdata", "294100", "pfx", "drive_c", "users", "steamuser", "AppData", "LocalLow", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config");

            CreateConfigFolder(appId123456Config, withModsConfig: true, modsConfigTime: now.AddHours(-1));
            CreateConfigFolder(appId294100Config, withModsConfig: true, modsConfigTime: now.AddDays(-2));

            // Use a testable service that overrides enumeration to only return our test candidates
            var testService = new TestableDetectionService(_tempRoot);
            testService.AddCandidate(appId123456Config);
            testService.AddCandidate(appId294100Config);
            
            // Act
            var result = testService.ResolveBestConfigFolder(steamRoot);

            // Assert - the more recent one (appId 123456) should win, not the 294100 one
            result.Should().Be(appId123456Config);
        }
    }

    /// <summary>
    /// Filtered drive-scan fallback (last-resort installation discovery) and the
    /// steam-copy layout probe. Driven through the internal testable seams so no
    /// real drives are touched.
    /// </summary>
    public class DetectionFallbackTechniqueTests : IDisposable
    {
        private readonly string _tempRoot;
        private readonly GameInstallationDetectionService _service;

        public DetectionFallbackTechniqueTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "RimSharpDetectionTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            _service = new GameInstallationDetectionService();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        /// <summary>
        /// Creates a minimal structure IsRimWorldInstallation() accepts:
        /// a RimWorld executable plus a parsable Version.txt.
        /// </summary>
        private static string CreateValidInstall(string dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "RimWorldWin64.exe"), "exe");
            File.WriteAllText(Path.Combine(dir, "Version.txt"), "1.6.4054 rev421");
            return dir;
        }

        [Fact]
        public void DriveScan_FindsInstallInsideGameLikelyDirectoryTree()
        {
            // Arrange: root/Games/RimWorld-style tree holding a valid install.
            var installDir = CreateValidInstall(Path.Combine(_tempRoot, "Games", "RimWorldPort"));

            // Act
            var found = _service.ScanDrivesForRimWorldGameFolderInternal(new[] { _tempRoot });

            // Assert
            found.Should().Be(installDir);
        }

        [Fact]
        public void DriveScan_DoesNotDescendOutsideGameLikelyDirectoryNames()
        {
            // Arrange: install exists but sits outside any descend-list directory.
            CreateValidInstall(Path.Combine(_tempRoot, "Custom", "RimWorldPort"));

            // Act
            var found = _service.ScanDrivesForRimWorldGameFolderInternal(new[] { _tempRoot });

            // Assert
            found.Should().BeNull();
        }

        [Fact]
        public void DriveScan_SkipsListedSystemDirectories()
        {
            // Arrange: a naive detector walking system dirs would find this;
            // the skip-list must refuse it.
            CreateValidInstall(Path.Combine(_tempRoot, "windows", "RimWorldPort"));

            // Act
            var found = _service.ScanDrivesForRimWorldGameFolderInternal(new[] { _tempRoot });

            // Assert
            found.Should().BeNull();
        }

        [Fact]
        public void DriveScan_RespectsDepthCap()
        {
            // Arrange: three nested game-library levels still reach the install...
            var withinCapRoot = Path.Combine(_tempRoot, "within");
            Directory.CreateDirectory(withinCapRoot);
            var shallow = CreateValidInstall(Path.Combine(
                withinCapRoot, "Games", "Games", "Games", "RimWorldPort"));

            // ...but four do not (max depth 3).
            var overCapRoot = Path.Combine(_tempRoot, "over");
            Directory.CreateDirectory(overCapRoot);
            var tooDeep = Path.Combine(
                overCapRoot, "Games", "Games", "Games", "Games", "RimWorldPort");
            CreateValidInstall(tooDeep);

            // Act
            var foundInScope = _service.ScanDrivesForRimWorldGameFolderInternal(new[] { withinCapRoot });
            var foundOutOfScope = _service.ScanDrivesForRimWorldGameFolderInternal(new[] { overCapRoot });

            // Assert
            foundInScope.Should().Be(shallow);
            foundOutOfScope.Should().BeNull();
        }

        [Fact]
        public void DriveScan_IgnoresRimWorldNamedDirWithoutInstallMarkers()
        {
            // Arrange: name matches but the probe must reject empty directories.
            Directory.CreateDirectory(Path.Combine(_tempRoot, "Games", "RimWorldEmpty"));

            // Act
            var found = _service.ScanDrivesForRimWorldGameFolderInternal(new[] { _tempRoot });

            // Assert
            found.Should().BeNull();
        }

        [Fact]
        public void AppearsToBeSteamCopy_DetectsSteamApiDll()
        {
            // Arrange
            var dir = Path.Combine(_tempRoot, "SomeGameCopy");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "steam_api64.dll"), "dll");

            // Act
            var result = GameInstallationDetectionService.AppearsToBeSteamCopy(dir);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void AppearsToBeSteamCopy_DetectsSteamappsAncestor()
        {
            // Arrange
            var dir = Path.Combine(_tempRoot, "steamapps", "common", "RimWorld");
            Directory.CreateDirectory(dir);

            // Act
            var result = GameInstallationDetectionService.AppearsToBeSteamCopy(dir);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void AppearsToBeSteamCopy_RejectsPlainFolderAndNull()
        {
            // Arrange
            var dir = Path.Combine(_tempRoot, "PlainFolder");
            Directory.CreateDirectory(dir);

            // Act
            var plainResult = GameInstallationDetectionService.AppearsToBeSteamCopy(dir);
            var nullResult = GameInstallationDetectionService.AppearsToBeSteamCopy(null);
            var missingResult = GameInstallationDetectionService.AppearsToBeSteamCopy(
                Path.Combine(_tempRoot, "does", "not", "exist"));

            // Assert
            plainResult.Should().BeFalse();
            nullResult.Should().BeFalse();
            missingResult.Should().BeFalse();
        }
    }
}
