using System;
using System.Linq;
using FluentAssertions;
using RimSharp.Features.LogViewer.Services;
using Xunit;

namespace RimSharp.Tests.Features.LogViewer.Services
{
    /// <summary>
    /// Envelope tests run against lines copied verbatim from real shared log
    /// uploads (HugsLib-family publishers): the roster line shapes, the
    /// multi-owner patch line separator, the Harmony version map, and the
    /// two "Log file contents" header variants.
    /// </summary>
    public class HugsLogEnvelopeTests
    {
        /// <summary>
        /// A complete shared upload: upload line, roster, patch inventory with
        /// its version map, the hidden platform-info line, and a Waylith log
        /// body (which is itself a real Player.log, envelope excluded).
        /// </summary>
        public const string SampleUpload =
            "Log uploaded on Saturday, November 7, 2026, 6:33:35 PM\n" +
            "Loaded mods:\n" +
            "Harmony(brrainz.harmony)[mv:3.0.1.0]: 0Harmony(3.0.0), HarmonyMod(3.0.1)\n" +
            "Core(Ludeon.RimWorld): (no assemblies)\n" +
            "[SYR] Processor Framework(syrchalis.processor.framework): 0Harmony(av:3.0.0,fv:2.3.6), ProcessorFramework(1.0.0)\n" +
            "Vehicle Framework(SmashPhil.VehicleFramework)[v:1.7.2520 rev09040]: AnimationKit(av:1.7.0,fv:1.7.0), CoreLib(av:1.7.0,fv:1.7.0), SmashTools(av:1.7.0,fv:1.7.0), Vehicles(av:1.7.0,fv:1.7.0)\n" +
            "Adaptive Storage Framework(adaptive.storage.framework): (no assemblies)\n" +
            "\n" +
            "Active Harmony patches:\n" +
            "ActiveProcess.CalcSpeedFactor: post: MedievalOverhaul.Patches.ActiveProcess_CalcSpeedFactor.Postfix\n" +
            "AttackTargetFinder.BestAttackTarget: PRE: VEF.Weapons.VanillaExpandedFramework_AttackTargetFinder_BestAttackTarget_Patch.Prefix, CombatExtended.HarmonyCE.Harmony_AttackTargetFinder+Harmony_AttackTargetFinder_BestAttackTarget.Prefix TRANS: CombatExtended.HarmonyCE.Harmony_AttackTargetFinder+Harmony_AttackTargetFinder_BestAttackTarget.Transpiler\n" +
            "AnomalyUtility.TryDuplicatePawn: PRE: SaveOurShip2.NoDuplicatingFormgels.Prefix\n" +
            "Harmony versions present: 3.0.0.0: net.pardeike.rimworld.lib.harmony, Dubwise.PerformanceAnalyzer; 2.4.3.0: SmashPhil.VehicleFramework, OskarPotocki.VEF; 2.3.6.0: medievalOverhaul, CombatExtended.HarmonyCE\n" +
            "\n" +
            "Platform information: (hidden, use publishing options to include)\n" +
            "\n" +
            "Log file contents:\n" +
            "=== Waylith log ===\n" +
            "Started:   2026-11-07 18:31:56.481 +03:00\n" +
            "Version:   1.7.5000.1275\n" +
            "OS:        Microsoft Windows 10.0.19045 (X64)\n" +
            "2026-11-07 18:31:56.481  INFO [Verse] RimWorld 1.7.5000 rev1275\n" +
            "2026-11-07 18:32:24.216  INFO [Verse] [Visible Wealth] Loaded.\n" +
            "2026-11-07 18:33:04.062 ERROR [Verse] Root level exception in OnGUI(): System.NullReferenceException: Object reference not set to an instance of an object.\n" +
            "   at Verse.DebugToolsPawns.ActivateHediffGiver(Pawn p) in Assembly-CSharp.dll:token 0x6002d8e+0x82\n" +
            "2026-11-07 18:34:10.000  WARN [Verse] Mod Mechanitor Shared Experience dependency (brrainz.harmony) needs to have <downloadUrl> and/or <steamWorkshopUrl> specified.\n";

        private const string PlainWaylithLog =
            "=== Waylith log ===\n" +
            "Started:   2026-11-07 18:31:56.481 +03:00\n" +
            "2026-11-07 18:31:56.481  INFO [Verse] RimWorld 1.7.5000 rev1275\n";

        // ── Detection ────────────────────────────────────────────────────────

        [Fact]
        public void LooksLikeEnvelope_ReadsTheUploadLineOnly()
        {
            HugsLogEnvelopeParser.LooksLikeEnvelope(SampleUpload).Should().BeTrue();
            HugsLogEnvelopeParser.LooksLikeEnvelope(PlainWaylithLog).Should().BeFalse();
            HugsLogEnvelopeParser.LooksLikeEnvelope(null).Should().BeFalse();
            HugsLogEnvelopeParser.LooksLikeEnvelope(string.Empty).Should().BeFalse();
            HugsLogEnvelopeParser.LooksLikeEnvelope("Log uploaded ").Should().BeFalse();
        }

        [Fact]
        public void Parse_PlainLogIsNotAnExport()
        {
            HugsLogEnvelopeParser.TryParse(PlainWaylithLog).Should().BeNull();
            HugsLogEnvelopeParser.TryParse(null).Should().BeNull();
        }

        [Fact]
        public void Strip_PassesOrdinaryLogsThroughUntouched()
        {
            HugsLogEnvelopeParser.Strip(PlainWaylithLog).Should().Be(PlainWaylithLog);
            HugsLogEnvelopeParser.Strip(null).Should().Be(string.Empty);
        }

        // ── Roster ───────────────────────────────────────────────────────────

        [Fact]
        public void Parse_ReadsTheModRoster_WithEveryOptionalVersionTag()
        {
            var envelope = HugsLogEnvelopeParser.TryParse(SampleUpload)!;

            envelope.Mods.Should().HaveCount(5);

            var vehicle = envelope.Mods.Single(m => m.Name == "Vehicle Framework");
            vehicle.PackageId.Should().Be("SmashPhil.VehicleFramework");
            vehicle.ModMetaDataVersion.Should().Be("1.7.2520 rev09040");
            vehicle.OverrideVersion.Should().BeNull();
            vehicle.ManifestVersion.Should().BeNull();
            vehicle.HasNoAssemblies.Should().BeFalse();
            vehicle.Assemblies.Should().HaveCount(4);
            vehicle.Assemblies[0].Name.Should().Be("AnimationKit");
            vehicle.Assemblies[0].VersionInfo.Should().Be("av:1.7.0,fv:1.7.0");

            var harmony = envelope.Mods.Single(m => m.Name == "Harmony");
            harmony.PackageId.Should().Be("brrainz.harmony");
            harmony.ModMetaDataVersion.Should().BeNull();
            harmony.ManifestVersion.Should().Be("3.0.1.0");
            harmony.Assemblies.Should().HaveCount(2);
            harmony.Assemblies[1].Name.Should().Be("HarmonyMod");
            harmony.Assemblies[1].VersionInfo.Should().Be("3.0.1");

            var core = envelope.Mods.Single(m => m.Name == "Core");
            core.HasNoAssemblies.Should().BeTrue();
            core.Assemblies.Should().BeEmpty();

            envelope.Mods.Single(m => m.Name.Contains("Processor Framework"))
                .Assemblies.Should().HaveCount(2);
        }

        [Fact]
        public void Parse_KeepsBracketedNamesIntact()
        {
            var envelope = HugsLogEnvelopeParser.TryParse(SampleUpload)!;

            envelope.Mods.Single(m => m.Name == "[SYR] Processor Framework");
        }

        // ── Patch inventory and the Harmony version map ──────────────────────

        [Fact]
        public void Parse_ReadsThePatchInventory_SplittingOwnersAndMarkers()
        {
            var envelope = HugsLogEnvelopeParser.TryParse(SampleUpload)!;

            envelope.PatchMethods.Should().HaveCount(3);

            var bestAttack = envelope.PatchMethods.Single(m => m.Method == "AttackTargetFinder.BestAttackTarget");
            bestAttack.Prefixes.Should().HaveCount(2);
            bestAttack.Postfixes.Should().BeEmpty();
            bestAttack.Transpilers.Should().HaveCount(1);
            bestAttack.Prefixes[0].Should().Be("VEF.Weapons.VanillaExpandedFramework_AttackTargetFinder_BestAttackTarget_Patch.Prefix");
            bestAttack.Prefixes[1].Should().StartWith("CombatExtended.HarmonyCE.");

            var calcSpeed = envelope.PatchMethods.Single(m => m.Method == "ActiveProcess.CalcSpeedFactor");
            calcSpeed.Postfixes.Should().HaveCount(1);
            calcSpeed.Prefixes.Should().BeEmpty();
        }

        [Fact]
        public void Parse_ReadsTheHarmonyVersionMap_GroupedByBuild()
        {
            var envelope = HugsLogEnvelopeParser.TryParse(SampleUpload)!;

            envelope.HarmonyVersions.Should().HaveCount(3);

            var newest = envelope.HarmonyVersions.First();
            newest.Version.Should().Be("3.0.0.0");
            newest.Owners.Should().HaveCount(2);
            newest.Owners[0].Should().Be("net.pardeike.rimworld.lib.harmony");

            envelope.HarmonyVersions.Last().Version.Should().Be("2.3.6.0");
            envelope.HarmonyVersions.Last().Owners.Should().Contain("medievalOverhaul");
        }

        // ── Body extraction ──────────────────────────────────────────────────

        [Fact]
        public void Parse_ExtractsTheBodyAfterTheContentsHeader()
        {
            var envelope = HugsLogEnvelopeParser.TryParse(SampleUpload)!;

            envelope.BodyText.Should().StartWith("=== Waylith log ===\n");
            envelope.BodyText.Should().NotContain("Active Harmony patches:");
            envelope.BodyText.Should().NotContain("Log uploaded on ");
            envelope.BodyText.Should().NotContain("Loaded mods:");
            envelope.BodyText.Should().Contain("RimWorld 1.7.5000 rev1275");

            // Body line numbers restart at 1, matching the sharer's own log.
            var bodyLines = envelope.BodyText.TrimEnd('\n').Split('\n');
            bodyLines.Should().HaveCount(9);
            envelope.EnvelopeLineCount.Should().Be(SampleUpload.Split('\n').Length - bodyLines.Length - 1);

            // Strip returns exactly the body, so the display store and the
            // diagnostics head read stay consistent.
            HugsLogEnvelopeParser.Strip(SampleUpload).Should().Be(envelope.BodyText);
        }

        [Fact]
        public void Parse_ReportsTheUploadTimeOnTheUploadersWallClock()
        {
            var envelope = HugsLogEnvelopeParser.TryParse(SampleUpload)!;

            envelope.UploadedOn.Should().Be(new DateTime(2026, 11, 7, 18, 33, 35));
            envelope.UploadedOnText.Should().Be("Saturday, November 7, 2026, 6:33:35 PM");
        }

        [Fact]
        public void Parse_CapturesThePlatformInfoLine()
        {
            var envelope = HugsLogEnvelopeParser.TryParse(SampleUpload)!;

            envelope.PlatformInfo.Should().Be("(hidden, use publishing options to include)");
        }

        [Fact]
        public void Parse_ReportsADefaultBodyWithoutFlags()
        {
            var envelope = HugsLogEnvelopeParser.TryParse(SampleUpload)!;

            envelope.UploadedPreviousLaunch.Should().BeFalse();
            envelope.BodyTrimmed.Should().BeFalse();
            envelope.PathsScrubbed.Should().BeFalse();
        }

        [Fact]
        public void Parse_ToleratesCrLf()
        {
            var envelope = HugsLogEnvelopeParser.TryParse(SampleUpload.Replace("\n", "\r\n"))!;

            envelope.Mods.Should().HaveCount(5);
            envelope.HarmonyVersions.Should().HaveCount(3);
            envelope.BodyText.Should().StartWith("=== Waylith log ===\n");
        }

        // ── Publisher variants ───────────────────────────────────────────────

        [Fact]
        public void Parse_RecognizesThePreviousLaunchVariant()
        {
            var upload = SampleUpload.Replace("Log file contents:\n", "Log file contents from previous game launch:\n");

            var envelope = HugsLogEnvelopeParser.TryParse(upload)!;

            envelope.UploadedPreviousLaunch.Should().BeTrue();
            envelope.BodyText.Should().StartWith("=== Waylith log ===\n");
        }

        [Fact]
        public void Parse_FlagsATrimmedBodyAndScrubbedPaths()
        {
            var upload = SampleUpload.Replace(
                "Log file contents:\n",
                "Log file contents:\nStartup happened at [Rimworld_dir]\\RimWorldWin64_Data\n") +
                "(log trimmed to 10,000 lines. Use publishing options to upload the full log)\n";

            var envelope = HugsLogEnvelopeParser.TryParse(upload)!;

            envelope.BodyTrimmed.Should().BeTrue();
            envelope.PathsScrubbed.Should().BeTrue();
        }

        [Fact]
        public void Parse_ReadsTheIncludedPlatformInfoBlock()
        {
            var upload = SampleUpload.Replace(
                "Platform information: (hidden, use publishing options to include)",
                "Platform information: \nCPU: AMD Ryzen 9 7950X\nOS: Windows 10\nMemory: 32630 MB");

            var envelope = HugsLogEnvelopeParser.TryParse(upload)!;

            envelope.PlatformInfo.Should().Be("CPU: AMD Ryzen 9 7950X OS: Windows 10 Memory: 32630 MB");
        }

        [Fact]
        public void Parse_SurvivesAnExportWithoutThePlatformSection()
        {
            // Newer 1.7 builds drop the platform section: the log itself carries it.
            var upload = SampleUpload
                .Replace("Platform information: (hidden, use publishing options to include)\n\n", "");

            var envelope = HugsLogEnvelopeParser.TryParse(upload)!;

            envelope.PlatformInfo.Should().BeNull();
            envelope.Mods.Should().HaveCount(5);
            envelope.HarmonyVersions.Should().HaveCount(3);
            envelope.BodyText.Should().StartWith("=== Waylith log ===\n");
        }

        // ── Diagnostics keep working over the extracted body ─────────────────

        [Fact]
        public void ExtractedBody_KeepsTheDiagnosticsWorking()
        {
            var envelope = HugsLogEnvelopeParser.TryParse(SampleUpload)!;
            var bodyLines = envelope.BodyText
                .TrimEnd('\n')
                .Split('\n')
                .Select((text, i) => new LogDiagnosticLine(i + 1, text))
                .ToList();

            var issues = LogIssueDetector.Analyze(bodyLines);
            issues.Should().Contain(i => i.Title == "Null Reference Exception");
            issues.Should().Contain(i => i.Title == "Incomplete Mod Metadata");
        }
    }
}
