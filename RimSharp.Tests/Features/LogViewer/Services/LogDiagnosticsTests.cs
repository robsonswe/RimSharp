using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RimSharp.Features.LogViewer.Services;
using Xunit;

namespace RimSharp.Tests.Features.LogViewer.Services
{
    /// <summary>
    /// Diagnostics tests run against line shapes copied from real Player.log
    /// files: 1.6 (Unity, no timestamps, profiler and allocator sections) and
    /// 1.7 (Waylith, timestamped and tagged messages).
    /// </summary>
    public class LogDiagnosticsTests
    {
        /// <summary>Wraps plain lines as file-numbered lines (1, 2, 3, …).</summary>
        private static List<LogDiagnosticLine> Lines(params string[] texts)
        {
            var result = new List<LogDiagnosticLine>(texts.Length);
            for (var i = 0; i < texts.Length; i++)
            {
                result.Add(new LogDiagnosticLine(i + 1, texts[i]));
            }

            return result;
        }

        private static readonly string[] WaylithStartup =
        {
            "=== Waylith log ===",
            "Started:   2026-11-02 20:57:04.800 -03:00",
            "Version:   1.7.4995.309",
            "OS:        Microsoft Windows 10.0.19045 (X64)",
            "2026-11-02 20:57:04.924  INFO [Verse] Command line arguments: -WaitForExitingPid=4296",
            "    at Brutal.Logging.LogCategory.Info(LogString`1 message, String sourceMemberName, String sourceFilePath, Int32 sourceLineNumber) [0x0]",
            "2026-11-02 20:57:04.936  INFO [Verse] RimWorld 1.7.4995 rev309",
            "    at RimWorld.VersionControl.LogVersionNumber() [0x0]",
            "2026-11-02 20:57:07.208  WARN [Verse] Mod Mechanitor Shared Experience dependency (brrainz.harmony) needs to have <downloadUrl> and/or <steamWorkshopUrl> specified.",
            "2026-11-02 20:57:10.826  INFO [Verse] Harmony Mod v3.0.1.0; Harmony library v3.0.0.0-rc.3",
            "2026-11-02 20:57:13.519  INFO [Verse] [1trickPwnyta's Biotech Patch] Loaded.",
            "2026-11-02 20:57:23.237  INFO [Verse] [Wil's Group Organization] Loaded.",
            "2026-11-02 20:58:17.192  INFO [Verse] CharacterEditor v1.7.1 initializing...",
            "    at .RimWorld.MainMenuDrawer.Init_Patch2() [0xFFFFFFFF]",
            "2026-11-02 20:58:17.276  INFO [Verse] CharacterEditor v1.7.1 ...done"
        };

        private static readonly string[] WaylithSessionStartup =
        {
            "=== Waylith log ===",
            "Started:   2026-10-31 00:34:23.100 -03:00",
            "Version:   1.7.4990.1069",
            "2026-10-31 00:34:23.728  INFO [Verse] RimWorld 1.7.4990 rev1069",
            "2026-10-31 00:34:24.227  WARN [Verse] Mod More Gravship Workbenches dependency (vanillaexpanded.gravship) needs to have <downloadUrl> and/or <steamWorkshopUrl> specified.",
            "2026-10-31 00:35:33.101  INFO [Verse] Initializing new game with mods:",
            "  - Ludeon.RimWorld",
            "  - Ludeon.RimWorld.Royalty",
            "  - Ludeon.RimWorld.Ideology",
            "  - Ludeon.RimWorld.Biotech",
            "  - Ludeon.RimWorld.Anomaly",
            "  - Ludeon.RimWorld.Odyssey",
            "    at Verse.Game.InitNewGame() [0x35]",
            "2026-10-31 00:40:26.062 ERROR [Verse] Root level exception in OnGUI(): System.NullReferenceException: Object reference not set to an instance of an object.",
            "   at Verse.DebugToolsPawns.ActivateHediffGiver(Pawn p) in Assembly-CSharp.dll:token 0x6002d8e+0x82",
            "2026-10-31 00:43:04.814 ERROR [Verse] Exception drawing Statue48831: System.NullReferenceException: Object reference not set to an instance of an object.",
            "2026-10-31 00:43:04.816 ERROR [Verse] Exception drawing Statue48831: System.NullReferenceException: Object reference not set to an instance of an object.",
            "2026-10-31 00:44:38.750  WARN [NativeAllocationRuntime] Persistent allocation 0x7F0C18334EC0 (7816 bytes, align 8) was not freed before shutdown."
        };

        private static readonly string[] UnityStartup =
        {
            "Mono path[0] = 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld/RimWorldWin64_Data/Managed'",
            "Initialize engine version: 2022.3.35f1 (011206c7a712)",
            "GfxDevice: creating device client; threaded=1; jobified=0",
            "Direct3D:",
            "    Version:  Direct3D 11.0 [level 11.1]",
            "    Renderer: NVIDIA GeForce RTX 4070 Ti (ID=0x2782)",
            "    VRAM:     11996 MB",
            "Begin MonoManager ReloadAssembly",
            "- Loaded All Assemblies, in  1.332 seconds",
            "- Finished resetting the current domain, in  0.001 seconds",
            "RimWorld 1.6.4871 rev591",
            "Loading game from file We Landed with mods:",
            "  - Ludeon.RimWorld",
            "  - Ludeon.RimWorld.Royalty",
            "  - Ludeon.RimWorld.Ideology",
            "  - Ludeon.RimWorld.Biotech",
            "  - Ludeon.RimWorld.Anomaly",
            "  - Ludeon.RimWorld.Odyssey",
            "Unloading 1 Unused Serialized files (Serialized files now loaded: 5)",
            "    [ALLOC_DEFAULT_MAIN]",
            "      Peak Allocated memory 271.0 MB",
            "    [ALLOC_DEFAULT_THREAD]",
            "      Peak Allocated memory 110.8 MB",
            "    [ALLOC_GFX_MAIN]",
            "      Peak Allocated memory 365.6 MB",
            "Could not resolve cross-reference: ThingDef named SomeMissingDef",
            "System.TypeLoadException: Could not load type 'SomeMod.Foo'",
            "Warning: Could not find Def named AnotherMissingDef"
        };

        // ── Format detection ──────────────────────────────────────────────────

        [Fact]
        public void Format_ReadsTheWaylithHeader()
        {
            LogFormatResolver.FromContent(WaylithStartup).Should().Be(LogFileFormat.Waylith);
        }

        [Fact]
        public void Format_ReadsTheWaylithMessageGrammarEvenWithoutTheHeader()
        {
            var lines = WaylithStartup.Skip(1).ToArray();
            LogFormatResolver.FromContent(lines).Should().Be(LogFileFormat.Waylith);
        }

        [Fact]
        public void Format_ReadsUnityLogs()
        {
            LogFormatResolver.FromContent(UnityStartup).Should().Be(LogFileFormat.Unity);
        }

        [Fact]
        public void Format_UnknownContentFallsBackToTheGameVersion()
        {
            LogFormatResolver.Resolve(new[] { "some unrelated line" }, "1.7").Should().Be(LogFileFormat.Waylith);
            LogFormatResolver.Resolve(new[] { "some unrelated line" }, "1.6").Should().Be(LogFileFormat.Unity);
            LogFormatResolver.Resolve(new[] { "some unrelated line" }, null).Should().Be(LogFileFormat.Unknown);
        }

        [Fact]
        public void Format_ContentWinsWhenAHandOpenedLogDisagreesWithTheInstall()
        {
            // A 1.6 log opened while 1.7 is installed must still be read as Unity.
            LogFormatResolver.Resolve(UnityStartup, "1.7").Should().Be(LogFileFormat.Unity);
        }

        [Fact]
        public void Format_TheVersionGateSplitsAt17()
        {
            LogFormatResolver.FromGameVersion("1.7").Should().Be(LogFileFormat.Waylith);
            LogFormatResolver.FromGameVersion("1.7.4990.1069").Should().Be(LogFileFormat.Waylith);
            LogFormatResolver.FromGameVersion("1.6").Should().Be(LogFileFormat.Unity);
            LogFormatResolver.FromGameVersion("1.5").Should().Be(LogFileFormat.Unity);
            LogFormatResolver.FromGameVersion("garbage").Should().Be(LogFileFormat.Unknown);
        }

        // ── Troubleshooter ────────────────────────────────────────────────────

        [Fact]
        public void Issues_FindsAndCountsWaylithSignatures()
        {
            var issues = LogIssueDetector.Analyze(Lines(WaylithSessionStartup));

            var nulls = issues.First(i => i.Title == "Null Reference Exception");
            nulls.Count.Should().Be(3);
            nulls.Severity.Should().Be(LogDiagnosticSeverity.Error);

            var metadata = issues.First(i => i.Title == "Incomplete Mod Metadata");
            metadata.Count.Should().Be(1);
            metadata.Severity.Should().Be(LogDiagnosticSeverity.Warning);
        }

        [Fact]
        public void Issues_ReportsTheFileLineNumberOfTheFirstHit()
        {
            var lines = Lines(
                "plain",
                "plain",
                "System.NullReferenceException: boom",
                "System.NullReferenceException: boom");

            var issue = LogIssueDetector.Analyze(lines).Single(i => i.Title == "Null Reference Exception");

            issue.Count.Should().Be(2);
            issue.FirstLineNumber.Should().Be(3);
        }

        [Fact]
        public void Issues_FindsTheRepeatedDrawSpamAsOneSignature()
        {
            var texts = new string[40];
            for (var i = 0; i < texts.Length; i++)
            {
                texts[i] = $"2026-10-31 00:43:04.{800 + i} ERROR [Verse] Exception drawing Statue{48831 + i}: System.NullReferenceException: boom.";
            }

            var issues = LogIssueDetector.Analyze(Lines(texts));

            var spam = issues.First(i => i.Title == "Repeated Draw Exception");
            spam.Count.Should().Be(40);
            spam.FirstLineNumber.Should().Be(1);
        }

        [Fact]
        public void Issues_FindsUnitySignatures()
        {
            var issues = LogIssueDetector.Analyze(Lines(UnityStartup));

            issues.Select(i => i.Title).Should().Contain(new[]
            {
                "Cross-reference Error",
                "Missing Def"
            });
        }

        [Fact]
        public void Issues_AreOrderedErrorsFirstThenByCount()
        {
            var issues = LogIssueDetector.Analyze(Lines(
                "Warning: something",
                "Could not find Def named A",
                "Could not find Def named B",
                "Could not find Def named C",
                "System.NullReferenceException: boom"));

            issues.First().Title.Should().Be("Null Reference Exception");
            issues.Last().Title.Should().Be("Missing Def");
            issues.Should().OnlyHaveUniqueItems(i => i.Title);
        }

        [Fact]
        public void Issues_EmptyContentProducesNoFindings()
        {
            LogIssueDetector.Analyze(new List<LogDiagnosticLine>()).Should().BeEmpty();
            LogIssueDetector.Analyze(null).Should().BeEmpty();
        }

        [Fact]
        public void Issue_ExposesLabelAndBrushKeyForTheView()
        {
            var issues = LogIssueDetector.Analyze(Lines("System.NullReferenceException: boom"));

            var issue = issues.Single();
            issue.SeverityLabel.Should().Be("Error");
            issue.SeverityBrushKey.Should().Be("BrushTextError");
        }

        [Fact]
        public void Suggestions_PointAtTheAppsOwnToolsWhereTheyExist()
        {
            var byTitle = LogDiagnosticPatterns.All.ToDictionary(p => p.Title, p => p.Suggestion);

            byTitle["Duplicate Mod"].Should().Contain("Maintenance → Duplicates");
            byTitle["Missing Def"].Should().Contain("Maintenance → Dependencies");
            byTitle["Missing Def"].Should().Contain("Sort");
            byTitle["Cross-reference Error"].Should().Contain("Maintenance → Dependencies");
            byTitle["Patch Operation Failed"].Should().Contain("Sort");
            byTitle["Missing Method Exception"].Should().Contain("Maintenance → Dependencies");
            byTitle["Type Load Exception"].Should().Contain("Maintenance → Dependencies");
            byTitle["Null Reference Exception"].Should().Contain("Maintenance → Incompatibilities");
        }

        // ── Memory (moved into the summary) ───────────────────────────────────

        [Fact]
        public void Memory_WaylithReportsGpuAndTextureHeap()
        {
            var lines = Lines(WaylithStartup);
            lines.Add(new LogDiagnosticLine(lines.Count + 1,
                "2026-11-02 20:57:04.700  INFO [GraphicsDevice] GPU [0] 'AMD Radeon R7 200 Series' (DiscreteGpu, Vulkan 1.2.170, driver AMDProprietaryKHR 'AMD proprietary driver' '22.6.1', 1024 MB device-local, uuid 00000000290000000000000000000000) - usable (score 288230376151719935)."));
            lines.Add(new LogDiagnosticLine(lines.Count + 1,
                "2026-11-02 20:57:04.696  INFO [TextureResidency] Textures are uploaded to heap index 0 (768.0 MiB). Under pressure GameTextures get moved to memory type 1 (heap 1)."));

            var memory = LogMemoryAnalyzer.Analyze(lines, LogFileFormat.Waylith);

            memory.Should().Contain(m => m.Name.Contains("AMD Radeon") && m.Megabytes == 1024);
            var textures = memory.Single(m => m.Name == "Texture residency heap");
            textures.Megabytes.Should().BeApproximately(768, 1);
            textures.DisplaySize.Should().Be("768 MB");
        }

        [Fact]
        public void Memory_UnityReportsAllocatorPeaksLargestFirst()
        {
            var memory = LogMemoryAnalyzer.Analyze(Lines(UnityStartup), LogFileFormat.Unity);

            memory.Should().HaveCount(3);
            memory.First().Name.Should().Be("Graphics memory");
            memory.First().Megabytes.Should().Be(365.6);
            memory.Should().Contain(m => m.Name == "Game memory (main)" && m.Megabytes == 271);
            memory.Should().Contain(m => m.Name == "Thread memory" && m.DisplaySize == "111 MB");
        }

        [Fact]
        public void Memory_EmptyContentReportsNothing()
        {
            LogMemoryAnalyzer.Analyze(new List<LogDiagnosticLine>(), LogFileFormat.Waylith).Should().BeEmpty();
            LogMemoryAnalyzer.Analyze(null, LogFileFormat.Unity).Should().BeEmpty();
        }

        // ── Summary ───────────────────────────────────────────────────────────

        [Fact]
        public void Summary_WaylithCountsItsOwnLevelTags()
        {
            var summary = LogSummaryAnalyzer.Analyze(Lines(WaylithSessionStartup), LogFileFormat.Waylith);

            summary.Format.Should().Be(LogFileFormat.Waylith);
            summary.GameVersion.Should().Be("1.7.4990");
            summary.GameRevision.Should().Be("1069");
            summary.ErrorCount.Should().Be(3);
            summary.WarningCount.Should().Be(2);  // one dependency-metadata warning plus the shutdown leak report
            summary.SeverityBasis.Should().Contain("ERROR / WARN tags");
            summary.ModsListed.Should().Be(6);
            summary.Issues.Should().NotBeEmpty();
            summary.TotalLines.Should().Be(WaylithSessionStartup.Length);
            summary.LastEntryTime.Should().NotBeNull();
        }

        [Fact]
        public void Summary_SessionStartComesFromTheBannerNotTheFirstMessage()
        {
            var summary = LogSummaryAnalyzer.Analyze(Lines(WaylithStartup), LogFileFormat.Waylith);

            // The banner's wall clock, with the writing machine's UTC offset ignored.
            summary.SessionStart.Should().Be(new DateTime(2026, 11, 2, 20, 57, 4, 800));
        }

        [Fact]
        public void Summary_SessionStartFallsBackToTheEarliestMessageWhenNoBanner()
        {
            // Drop the banner and the Started: header line: only messages remain.
            var summary = LogSummaryAnalyzer.Analyze(
                Lines(WaylithStartup.Skip(2).ToArray()), LogFileFormat.Waylith);

            summary.SessionStart.Should().Be(new DateTime(2026, 11, 2, 20, 57, 4, 924));
        }

        [Fact]
        public void Summary_UnityCountsThroughTheViewerClassifier()
        {
            var summary = LogSummaryAnalyzer.Analyze(Lines(UnityStartup), LogFileFormat.Unity);

            summary.ErrorCount.Should().Be(1);  // the TypeLoadException line, via the viewer's classifier
            summary.WarningCount.Should().BeGreaterOrEqualTo(1); // the missing-def line
            summary.SeverityBasis.Should().Contain("classifier");
            summary.SessionStart.Should().BeNull();             // Unity logs carry no timestamps
            summary.ModsListed.Should().Be(6);
            summary.Memory.Should().HaveCount(3);
        }

        [Fact]
        public void Summary_EmptyContentIsEmpty()
        {
            var summary = LogSummaryAnalyzer.Analyze(new List<LogDiagnosticLine>(), LogFileFormat.Waylith);

            summary.TotalLines.Should().Be(0);
            summary.Issues.Should().BeEmpty();
            summary.Memory.Should().BeEmpty();
            summary.ModsListed.Should().Be(0);
            summary.SessionStart.Should().BeNull();
        }

        // ── Shared mod-list scan ──────────────────────────────────────────────

        [Fact]
        public void ModList_StopsAtTheFirstNonListItem()
        {
            LogModListScanner.CountMods(Lines(
                "Loading game from file X with mods:",
                "  - A",
                "  - B",
                "Unrelated line")).Should().Be(2);
        }

        [Fact]
        public void ModList_MissingListIsZero()
        {
            LogModListScanner.CountMods(Lines("no mod list here")).Should().Be(0);
            LogModListScanner.CountMods(null).Should().Be(0);
        }

        // ── Head + retained-window merge (the 10 MB cap) ─────────────────────

        [Fact]
        public void Window_HeadAloneNumbersFromTheFirstLine()
        {
            var window = LogDiagnosticsWindow.Build("one\ntwo\nthree\n", 1, Array.Empty<string>());

            window.Should().HaveCount(3);
            window[0].Should().Be(new LogDiagnosticLine(1, "one"));
            window[2].Should().Be(new LogDiagnosticLine(3, "three"));
        }

        [Fact]
        public void Window_MergesHeadAndRetainedWindowWithoutDuplicatingTheOverlap()
        {
            // Store retained lines 4-5 of a 5-line log; the head covered all 5.
            var store = new[] { "four", "five" };
            var window = LogDiagnosticsWindow.Build("one\ntwo\nthree\nfour\nfive\n", 4, store);

            window.Select(l => l.LineNumber).Should().Equal(1, 2, 3, 4, 5);
            window.Should().OnlyHaveUniqueItems(l => l.LineNumber);
            window.Single(l => l.LineNumber == 4).Text.Should().Be("four");
        }

        [Fact]
        public void Window_CoversTheEvictedHeadWithTheHeadRead()
        {
            // The store evicted lines 1-3 (its cap), retaining 4-5; the head read
            // has to bring the startup back or the reports lose their data.
            var head = "banner\nversion line\nharmony line\nfour\nfive\n";
            var window = LogDiagnosticsWindow.Build(head, 4, new[] { "four", "five" });

            window.Should().HaveCount(5);
            window[0].Text.Should().Be("banner");
            window[0].LineNumber.Should().Be(1);
        }

        [Fact]
        public void Window_NumbersTheRetainedWindowFromTheStoresFirstLine()
        {
            var window = LogDiagnosticsWindow.Build(null, 101, new[] { "hundred-one", "hundred-two" });

            window.Should().Equal(
                new LogDiagnosticLine(101, "hundred-one"),
                new LogDiagnosticLine(102, "hundred-two"));
        }

        [Fact]
        public void Window_ScopeSaysSoWhenOnlyPartOfTheFileWasRead()
        {
            LogDiagnosticsWindow.DescribeScope(headCoversWholeFile: true, storeLinesEvicted: 0)
                .Should().Be("Whole log analyzed.");
            // Nothing evicted from the store means the head+store union is the
            // whole file, even when the head window itself was partial.
            LogDiagnosticsWindow.DescribeScope(headCoversWholeFile: false, storeLinesEvicted: 0)
                .Should().Be("Whole log analyzed.");
            LogDiagnosticsWindow.DescribeScope(headCoversWholeFile: false, storeLinesEvicted: 50_000)
                .Should().Contain("8 MB").And.Contain("newest lines still retained");
        }

        [Fact]
        public void Window_Regression_ReportsSurviveACappedStore()
        {
            // End-to-end guard for the bug: an 11 MB log's startup data sits in
            // the first kilobyte, which the store had already evicted.
            var head = string.Join("\n", WaylithSessionStartup) + "\n";
            var retainedTail = new[]
            {
                "2026-10-31 00:44:39.000  WARN [NativeAllocationRuntime] Persistent allocation 0x7F0C99999999 (24 bytes, align 8) was not freed before shutdown."
            };

            var window = LogDiagnosticsWindow.Build(head, 21, retainedTail);
            var summary = LogSummaryAnalyzer.Analyze(window, LogFileFormat.Waylith);

            summary.GameVersion.Should().Be("1.7.4990");
            summary.ModsListed.Should().Be(6);
            summary.SessionStart.Should().NotBeNull();

            // The tail-only occurrences still count, and the head keeps priority
            // for "first at line N" because it comes first in the file.
            var issues = LogIssueDetector.Analyze(window);
            var leaks = issues.First(i => i.Title == "Native Memory Leak Report");
            leaks.Count.Should().Be(2);
            leaks.FirstLineNumber.Should().Be(18);
            issues.First(i => i.Title == "Null Reference Exception").FirstLineNumber.Should().Be(14);
        }
    }
}
