using System;
using System.IO;
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
}
