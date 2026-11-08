using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.LogViewer.Services;
using RimSharp.Features.LogViewer.ViewModels;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Tests.Base;
using RimSharp.Tests.Features.LogViewer.Services;
using Xunit;

namespace RimSharp.Tests.Features.LogViewer.ViewModels
{
    public class LogViewerViewModelTests : System.IDisposable
    {
        private readonly string _root;
        private readonly string _configFolder;
        private readonly IPathService _pathService = Substitute.For<IPathService>();

        public LogViewerViewModelTests()
        {
            TestApp.InitializeTestApp();
            _root = Path.Combine(Path.GetTempPath(), "rimsharp-logviewer-vm-" + Path.GetRandomFileName());
            _configFolder = Path.Combine(_root, "Config");
            Directory.CreateDirectory(_configFolder);
            _pathService.GetConfigPath().Returns(_configFolder);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }

        private void WriteCurrentLog(string content) =>
            File.WriteAllText(Path.Combine(_root, "Player.log"), content);

        private LogViewerViewModel CreateViewModel() => new(_pathService);

        private static void WriteWaylithLog(string path) =>
            File.WriteAllLines(path, new[]
            {
                "=== Waylith log ===",
                "Started:   2026-11-02 20:57:04.800 -03:00",
                "Version:   1.7.4995.309",
                "2026-11-02 20:57:04.936  INFO [Verse] RimWorld 1.7.4995 rev309",
                "2026-11-02 20:57:10.826  INFO [Verse] Harmony Mod v3.0.1.0; Harmony library v3.0.0.0-rc.3",
                "2026-11-02 20:57:13.519  INFO [Verse] [Bed Rest For Food Poisoning] Loaded.",
                "2026-11-02 20:57:23.237  INFO [Verse] [Wil's Group Organization] Loaded.",
                "2026-11-02 20:57:07.208  WARN [Verse] Mod Age vanity dependency (brrainz.harmony) needs to have <downloadUrl> and/or <steamWorkshopUrl> specified.",
                "2026-11-02 20:58:17.192  INFO [Verse] CharacterEditor v1.7.1 initializing...",
                "    at .RimWorld.MainMenuDrawer.Init_Patch2() [0xFFFFFFFF]",
                "2026-11-02 20:59:01.001 ERROR [Verse] Root level exception in OnGUI(): System.NullReferenceException: Object reference not set to an instance of an object.",
                "2026-11-02 20:59:01.002 ERROR [Verse] Root level exception in OnGUI(): System.NullReferenceException: Object reference not set to an instance of an object.",
                "2026-11-02 20:59:01.003  WARN [NativeAllocationRuntime] Persistent allocation 0x7F0C18334EC0 (7816 bytes, align 8) was not freed before shutdown."
            });

        [AvaloniaFact]
        public async Task LoadDefaultAsync_OnWaylithVersionReadsLogsSubfolder()
        {
            _pathService.GetMajorGameVersion().Returns("1.7");
            var logsFolder = Path.Combine(_root, "Logs");
            Directory.CreateDirectory(logsFolder);
            File.WriteAllText(Path.Combine(logsFolder, "Player.log"), "waylith line one\nwaylith line two\n");
            // Root-level log must be ignored on 1.7+.
            File.WriteAllText(Path.Combine(_root, "Player.log"), "stale root log\n");

            var vm = CreateViewModel();
            await vm.LoadDefaultAsync();

            vm.Lines.Should().HaveCount(2);
            vm.PathText.Should().Be(Path.Combine(logsFolder, "Player.log"));
        }

        [AvaloniaFact]
        public async Task LoadDefaultAsync_ReadsPlayerLogIntoLines()
        {
            WriteCurrentLog("first line\nWarning: something deprecated\n[E] Failed to load\nplain line\n");

            var vm = CreateViewModel();
            await vm.LoadDefaultAsync();

            vm.Lines.Should().HaveCount(4);
            vm.LineCountText.Should().Be("4 lines");
            vm.PathText.Should().Be(Path.Combine(_root, "Player.log"));
            vm.StatusMessage.Should().Contain("Loaded");
        }

        [AvaloniaFact]
        public async Task LoadDefaultAsync_WhenLogMissing_ReportsNotFound()
        {
            var vm = CreateViewModel();
            await vm.LoadDefaultAsync();

            vm.Lines.Should().BeEmpty();
            vm.PathText.Should().Be("Player.log not found");
            vm.StatusMessage.Should().Contain("not found");
        }

        [AvaloniaFact]
        public async Task LoadDefaultAsync_WhenPathServiceMissing_DoesNotThrow()
        {
            var vm = new LogViewerViewModel(pathService: null);

            await vm.LoadDefaultAsync();

            vm.Lines.Should().BeEmpty();
            vm.StatusMessage.Should().Contain("not found");
        }

        [AvaloniaFact]
        public async Task LoadAsync_ClassifiesMessagesWithSeverities()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath,
                "11:04:37 PM Loaded mods\nKey binding conflict: A\nUnhandled exception\n   at Verse.Awake ()\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            // The stack frame joins the exception message; three messages total.
            vm.Lines.Should().HaveCount(3);
            vm.Lines[0].Severity.Should().Be(LogLineSeverity.Info);
            vm.Lines[0].SeverityBrushKey.Should().Be("BrushTextMuted");
            vm.Lines[1].Severity.Should().Be(LogLineSeverity.Keybind);
            vm.Lines[1].SeverityBrushKey.Should().Be("BrushTextWarning");
            vm.Lines[2].Severity.Should().Be(LogLineSeverity.Exception);
            vm.Lines[2].SeverityBrushKey.Should().Be("BrushTextError");
            vm.Lines[2].Text.Should().Be("Unhandled exception\n   at Verse.Awake ()");
        }

        [AvaloniaFact]
        public async Task LoadAsync_ExposesHumanReadableSize()
        {
            WriteCurrentLog(new string('x', 2048));

            var vm = CreateViewModel();
            await vm.LoadDefaultAsync();

            vm.SizeText.Should().Be("2.0 KB");
        }

        [AvaloniaFact]
        public async Task LoadAsync_MissingFile_ReportsErrorWithoutLines()
        {
            var vm = CreateViewModel();

            await vm.LoadAsync(Path.Combine(_root, "nope.log"));

            vm.Lines.Should().BeEmpty();
            vm.StatusMessage.Should().Contain("not found");
        }

        [AvaloniaFact]
        public async Task RefreshAsync_RereadsCurrentLogFromTop()
        {
            WriteCurrentLog("one\n");
            var vm = CreateViewModel();
            await vm.LoadDefaultAsync();

            File.AppendAllText(Path.Combine(_root, "Player.log"), "two\n");
            await vm.RefreshAsync();

            vm.Lines.Should().HaveCount(2);
            vm.Lines[1].Text.Should().Be("two");
        }

        [AvaloniaFact]
        public void Commands_AreDisabledWhileLoading()
        {
            var vm = CreateViewModel();

            vm.LoadDefaultCommand.CanExecute(null).Should().BeTrue();
        }

        [AvaloniaFact]
        public async Task LoadAsync_AccumulatesStatisticsOverAllLines()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath,
                "Loaded mods\nWarning: one\nWarning: two\n[E] Failed\n[MyMod] Error while resolving\nplain\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            vm.Stats.TotalLines.Should().Be(6);
            vm.Stats.Infos.Should().Be(1);
            vm.Stats.Warnings.Should().Be(2);
            vm.Stats.Errors.Should().Be(2);
            vm.Stats.ModIssues.Should().Be(1);
            vm.Stats.AllIssues.Should().Be(4); // distinct issue lines, not bucket sum
        }

        [AvaloniaFact]
        public async Task SelectingWarningsFilter_ShowsOnlyWarnings()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath, "Loaded mods\nWarning: one\nplain\nWarning: two\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            var warnings = vm.FilterOptions.First(o => o.Mode == LogFilterMode.WarningsOnly);
            vm.SelectedFilterOption = warnings;

            vm.Lines.Should().HaveCount(2);
            vm.Lines.Should().OnlyContain(l => l.Severity == LogLineSeverity.Warning);
            vm.LineCountText.Should().Be("2 of 4 lines");
        }

        [AvaloniaFact]
        public async Task LineFilter_ComposesWithModeFilter()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath,
                "[Alpha] Error in alpha\n[Beta] Error in beta\n[Alpha] plain info line\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            var errors = vm.FilterOptions.First(o => o.Mode == LogFilterMode.ErrorsOnly);
            vm.SelectedFilterOption = errors;

            vm.ApplyLineFilter("alpha");

            vm.Lines.Should().HaveCount(1);
            vm.Lines[0].Text.Should().Be("[Alpha] Error in alpha");
            vm.LineCountText.Should().Be("1 of 3 lines");
        }

        [AvaloniaFact]
        public async Task LineFilter_IsCaseInsensitive()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath, "[AlphaMod] Warning\n[betamod] Warning\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            vm.ApplyLineFilter("ALPHAMOD");

            vm.Lines.Should().HaveCount(1);
            vm.Lines[0].Text.Should().Be("[AlphaMod] Warning");
        }

        [AvaloniaFact]
        public async Task Filters_WithNoMatches_ShowEmptyAndStatus()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath, "plain one\nplain two\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            var exceptions = vm.FilterOptions.First(o => o.Mode == LogFilterMode.ExceptionsOnly);
            vm.SelectedFilterOption = exceptions;

            vm.Lines.Should().BeEmpty();
            vm.StatusMessage.Should().Contain("No lines match");
            vm.LineCountText.Should().Be("0 of 2 lines");
        }

        [AvaloniaFact]
        public void EmptyLogArea_ShowsPlaceholder_UntilContentLoads()
        {
            var vm = CreateViewModel();

            vm.ShowLogPlaceholder.Should().BeTrue();
        }

        [AvaloniaFact]
        public async Task LoadedLogArea_HidesPlaceholder()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath, "plain\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            vm.ShowLogPlaceholder.Should().BeFalse();
        }

        [AvaloniaFact]
        public async Task MultilineMessage_CountsAndDisplaysAsOneEntry()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath,
                "Error in MyMod: resolving XML\n  at Verse.Foo ()\n  at RimSharp.Bar ()\nplain info\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            // Two messages, not four lines.
            vm.Lines.Should().HaveCount(2);
            vm.Stats.TotalLines.Should().Be(2);
            vm.Lines[0].Text.Should().Be("Error in MyMod: resolving XML\n  at Verse.Foo ()\n  at RimSharp.Bar ()");
            vm.LineCountText.Should().Be("2 lines");
        }

        [AvaloniaFact]
        public async Task TextFilter_MatchesContentOnContinuationLines()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath, "Wrapped output\n  mentioned Harmony here\nplain info\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            vm.ApplyLineFilter("harmony");

            vm.Lines.Should().HaveCount(1);
            vm.Lines[0].Text.Should().Contain("mentioned Harmony here");
        }

        [AvaloniaFact]
        public async Task ModeFilter_SeesTheWholeMessage()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath, "Loading scene\n  UnityException: boom\nplain info\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            var exceptions = vm.FilterOptions.First(o => o.Mode == LogFilterMode.ExceptionsOnly);
            vm.SelectedFilterOption = exceptions;

            vm.Lines.Should().HaveCount(1);
            vm.Lines[0].Severity.Should().Be(LogLineSeverity.Exception);
        }

        [AvaloniaFact]
        public async Task Refresh_PreservesActiveFilter()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath, "Warning: old\nplain\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);
            vm.SelectedFilterOption = vm.FilterOptions.First(o => o.Mode == LogFilterMode.WarningsOnly);

            File.AppendAllText(logPath, "Warning: new\nplain\n");
            await vm.RefreshAsync();

            // Filter stays applied across the refresh and picks up the new line.
            vm.SelectedFilterOption.Mode.Should().Be(LogFilterMode.WarningsOnly);
            vm.Lines.Should().HaveCount(2);
            vm.Lines[1].Text.Should().Be("Warning: new");
        }

        [AvaloniaFact]
        public async Task LoadAsync_ShowsLastModifiedTimestamp()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath, "plain\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            vm.ModifiedText.Should().Be("Last modified " + File.GetLastWriteTime(logPath).ToString("G"));
        }

        [AvaloniaFact]
        public async Task LoadFromFileAsync_CancelledDialog_LoadsNothing()
        {
            var files = new FakeFileDialogService { OpenResult = (false, null) };
            var vm = new LogViewerViewModel(_pathService, fileDialogService: files);

            await vm.LoadFromFileAsync();

            vm.Lines.Should().BeEmpty();
            vm.StatusMessage.Should().Contain("cancelled");
        }

        [AvaloniaFact]
        public async Task LoadFromFileAsync_OpensTheChosenFileAndTracksItAsSource()
        {
            var logPath = Path.Combine(_root, "shared.log");
            File.WriteAllText(logPath, "shared line one\nWarning: shared two\n");
            var files = new FakeFileDialogService { OpenResult = (true, logPath) };
            var vm = new LogViewerViewModel(_pathService, fileDialogService: files);

            vm.LoadFileCommand.CanExecute(null).Should().BeTrue();
            await vm.LoadFromFileAsync();

            vm.Lines.Should().HaveCount(2);
            vm.PathText.Should().Be(logPath);
            vm.SourceOptions.Should().Contain(o => o.Path == logPath);
            vm.SelectedSourceOption!.Path.Should().Be(logPath);
        }

        [AvaloniaFact]
        public async Task LoadFromFileAsync_OffersTxtSoSharedExportsOpenByDefault()
        {
            // Shared log uploads arrive as .txt (paste.gg and "Save as" both
            // default to it), so the picker's default filter must show them.
            var files = new FakeFileDialogService { OpenResult = (false, null) };
            var vm = new LogViewerViewModel(_pathService, fileDialogService: files);

            await vm.LoadFromFileAsync();

            files.LastOpenTitle.Should().Contain("shared export");
            files.LastOpenFilter.Should().Be("Log files and shared exports|*.log;*.txt|All files|*.*");
        }

        [AvaloniaFact]
        public async Task SourceSwitcher_ListsPreviousSessionWhenPresent()
        {
            File.WriteAllText(Path.Combine(_root, "Player.log"), "current line\n");
            File.WriteAllText(Path.Combine(_root, "Player-prev.log"), "previous line\n");

            var vm = CreateViewModel();
            await vm.LoadDefaultAsync();

            vm.SourceOptions.Should().HaveCount(2);
            vm.HasMultipleSources.Should().BeTrue();
            vm.SelectedSourceOption!.Path.Should().Be(Path.Combine(_root, "Player.log"));
        }

        [AvaloniaFact]
        public async Task SourceSwitcher_OnlyCurrentSessionWhenPrevMissing()
        {
            File.WriteAllText(Path.Combine(_root, "Player.log"), "current line\n");

            var vm = CreateViewModel();
            await vm.LoadDefaultAsync();

            vm.SourceOptions.Should().HaveCount(1);
            vm.HasMultipleSources.Should().BeFalse();
        }

        [AvaloniaFact]
        public async Task SwitchingSource_ReloadsAndKeepsFilterState()
        {
            var current = Path.Combine(_root, "Player.log");
            var previous = Path.Combine(_root, "Player-prev.log");
            File.WriteAllText(current, "Warning: current\n");
            File.WriteAllText(previous, "Warning: previous\nplain in prev\n");

            var vm = CreateViewModel();
            await vm.LoadDefaultAsync();
            vm.SelectedFilterOption = vm.FilterOptions.First(o => o.Mode == LogFilterMode.WarningsOnly);
            vm.ApplyLineFilter("prev");

            vm.SelectedSourceOption = vm.SourceOptions.First(o => o.Path == previous);
            await vm.SwitchTask!;

            vm.PathText.Should().Be(previous);
            vm.SelectedSourceOption!.Path.Should().Be(previous);
            vm.SelectedFilterOption.Mode.Should().Be(LogFilterMode.WarningsOnly);
            vm.LineFilter.Should().Be("prev");
            vm.Lines.Should().HaveCount(1);
            vm.Lines[0].Text.Should().Be("Warning: previous");
        }

        [AvaloniaFact]
        public async Task SwitchingBackToCurrentSession_WorksAfterLoadingPrev()
        {
            var current = Path.Combine(_root, "Player.log");
            var previous = Path.Combine(_root, "Player-prev.log");
            File.WriteAllText(current, "Warning: current\n");
            File.WriteAllText(previous, "previous line\n");

            var vm = CreateViewModel();
            await vm.LoadDefaultAsync();

            vm.SelectedSourceOption = vm.SourceOptions.First(o => o.Path == previous);
            await vm.SwitchTask!;
            vm.PathText.Should().Be(previous);

            await vm.LoadDefaultAsync();

            vm.PathText.Should().Be(current);
            vm.SelectedSourceOption!.Path.Should().Be(current);
        }

        [AvaloniaFact]
        public async Task CollapseRepeats_MergesConsecutiveIdenticalMessages()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath,
                "Warning: same dropped stack\nWarning: same dropped stack\nWarning: same dropped stack\nother\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            vm.Lines.Should().HaveCount(2);
            vm.Lines[0].RepeatCount.Should().Be(3);
            vm.Lines[0].Text.Should().Be("Warning: same dropped stack");
            vm.Lines[0].DisplayText.Should().Be("Warning: same dropped stack\n(Repeated 3 times)");
            // The collapsed run counts once, so totals reflect what is displayed.
            vm.Stats.TotalLines.Should().Be(2);
            vm.Stats.Warnings.Should().Be(1);
            vm.LineCountText.Should().Be("2 lines");
        }

        [AvaloniaFact]
        public async Task CollapseRepeats_DoesNotMergeNonConsecutiveMessages()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath, "Warning: a\nWarning: b\nWarning: a\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            vm.Lines.Should().HaveCount(3);
            vm.Lines.Should().OnlyContain(l => l.RepeatCount == 1);
        }

        [AvaloniaFact]
        public async Task CollapseRepeats_TogglingOffRestoresEveryMessage()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath, "Warning: same\nWarning: same\nother\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);
            vm.Lines.Should().HaveCount(2);

            vm.CollapseRepeats = false;

            vm.Lines.Should().HaveCount(3);
            vm.Stats.TotalLines.Should().Be(3);
            vm.Lines.Should().OnlyContain(l => l.RepeatCount == 1);
        }

        [AvaloniaFact]
        public async Task TextFilter_MatchesTheMessageNotTheAnnotation()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath, "Warning: same\nWarning: same\nother\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);
            vm.ApplyLineFilter("same");

            vm.Lines.Should().HaveCount(1);
            vm.Lines[0].DisplayText.Should().Contain("(Repeated 2 times)");
        }

        [AvaloniaFact]
        public async Task Export_WritesTheFilteredViewAndOffersADefaultName()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath, "[Alpha] Error in alpha\n[Beta] Error in beta\nplain\n");
            var files = new FakeFileDialogService();
            var vm = new LogViewerViewModel(_pathService, fileDialogService: files);
            await vm.LoadAsync(logPath);

            vm.ExportCommand.CanExecute(null).Should().BeTrue();
            var target = Path.Combine(_root, "export.txt");
            files.SaveResult = (true, target);

            await vm.ExportAsync();

            files.LastSaveFileName.Should().Be("Player-log-export.txt");
            File.ReadAllText(target).Trim().Replace("\r\n", "\n")
                .Should().Be("[Alpha] Error in alpha\n[Beta] Error in beta\nplain");
            vm.StatusMessage.Should().Contain("Exported 3 lines");
        }

        [AvaloniaFact]
        public async Task Export_ExportsOnlyWhatTheFilterShows()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath, "Warning: keep\nplain\n");
            var files = new FakeFileDialogService();
            var vm = new LogViewerViewModel(_pathService, fileDialogService: files);
            await vm.LoadAsync(logPath);
            vm.SelectedFilterOption = vm.FilterOptions.First(o => o.Mode == LogFilterMode.WarningsOnly);

            var target = Path.Combine(_root, "export.txt");
            files.SaveResult = (true, target);
            await vm.ExportAsync();

            File.ReadAllText(target).Trim().Replace("\r\n", "\n").Should().Be("Warning: keep");
        }

        [AvaloniaFact]
        public async Task Export_CancelledDialog_WritesNothing()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath, "plain\n");
            var files = new FakeFileDialogService { SaveResult = (false, null) };
            var vm = new LogViewerViewModel(_pathService, fileDialogService: files);
            await vm.LoadAsync(logPath);

            await vm.ExportAsync();

            File.Exists(Path.Combine(_root, "export.txt")).Should().BeFalse();
            vm.StatusMessage.Should().Contain("cancelled");
        }

        [AvaloniaFact]
        public async Task Export_WithNothingLoaded_StaysDisabledAndExplains()
        {
            var files = new FakeFileDialogService();
            var vm = new LogViewerViewModel(_pathService, fileDialogService: files);

            vm.ExportCommand.CanExecute(null).Should().BeFalse();

            await vm.ExportAsync();

            vm.StatusMessage.Should().Contain("Nothing to export");
        }

        [AvaloniaFact]
        public async Task Diagnostics_RebuildAfterLoadingAWaylithLog()
        {
            var logPath = Path.Combine(_root, "Player.log");
            WriteWaylithLog(logPath);
            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            vm.Diagnostics.HasContent.Should().BeTrue();
            vm.Diagnostics.SummaryFormat.Should().Be("Waylith (1.7+)");
            vm.Diagnostics.SummaryVersion.Should().Be("1.7.4995 rev 309");

            vm.Diagnostics.Issues.Should().Contain(i => i.Title == "UI Loop Exception" && i.Count == 2);
            vm.Diagnostics.Issues.Should().Contain(i => i.Title == "Incomplete Mod Metadata");
            vm.Diagnostics.Issues.Should().Contain(i => i.Title == "Native Memory Leak Report");
            vm.Diagnostics.IssueSummaryText.Should().Contain("4 distinct issue signatures");

            // The OnGUI lines raise both the UI-loop and the null-reference signature.
            vm.Diagnostics.Issues.Should().Contain(i => i.Title == "Null Reference Exception" && i.Count == 2);

            vm.Diagnostics.SummaryRows.Should().Contain("Log format: Waylith (1.7+)");
            vm.Diagnostics.SummaryRows.Should().Contain("Game version: 1.7.4995 rev 309");
            vm.Diagnostics.SummaryRows.Should().Contain("Mods listed: 0");

            // The viewer's own message counters now live in the summary (the
            // numbers the stat buttons used to show).
            vm.Diagnostics.SummaryRows.Should().Contain(r => r.StartsWith("Messages:"));
            vm.Diagnostics.SummaryRows.Should().Contain(r => r.StartsWith("Error lines:"));

            // The first issue is preselected so the detail pane is never empty.
            vm.Diagnostics.SelectedIssue.Should().NotBeNull();
            vm.Diagnostics.IssueDetailTitle.Should().Be(vm.Diagnostics.SelectedIssue!.Title);
            vm.Diagnostics.IssueDetail.Should().Contain("What to try");
        }

        [AvaloniaFact]
        public async Task Diagnostics_ContentWinsOverTheInstalledVersion()
        {
            // The install runs 1.7 (Waylith) but the opened log is a 1.6 (Unity) log.
            _pathService.GetMajorGameVersion().Returns("1.7");
            var logPath = Path.Combine(_root, "old.log");
            File.WriteAllLines(logPath, new[]
            {
                "Mono path[0] = 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld/RimWorldWin64_Data/Managed'",
                "Begin MonoManager ReloadAssembly",
                "- Loaded All Assemblies, in  1.332 seconds",
                "    [ALLOC_GFX_MAIN]",
                "      Peak Allocated memory 365.6 MB",
                "RimWorld 1.6.4871 rev591"
            });

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            vm.Diagnostics.SummaryFormat.Should().Be("Unity (pre-1.7)");
            // Memory figures moved into the summary rows.
            vm.Diagnostics.SummaryRows.Should().Contain(r => r.StartsWith("Graphics memory: 366 MB"));
            vm.Diagnostics.SummaryRows.Should().Contain("Severity by: the viewer's line classifier (this log has no level tags)");
        }

        [AvaloniaFact]
        public async Task Diagnostics_VersionSettlesAnUnrecognizedLog()
        {
            _pathService.GetMajorGameVersion().Returns("1.7");
            var logPath = Path.Combine(_root, "mystery.log");
            File.WriteAllText(logPath, "nothing recognizable here\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            vm.Diagnostics.SummaryFormat.Should().Be("Waylith (1.7+)");
            vm.Diagnostics.HasContent.Should().BeTrue();
            vm.Diagnostics.Issues.Should().BeEmpty();
            vm.Diagnostics.IssueSummaryText.Should().Be("No known issues detected in this log.");
        }

        [AvaloniaFact]
        public void Diagnostics_FreshViewModelHasNoReports()
        {
            var vm = CreateViewModel();

            vm.Diagnostics.HasContent.Should().BeFalse();
            vm.Diagnostics.Issues.Should().BeEmpty();
            vm.Diagnostics.SummaryRows.Should().BeEmpty();
            vm.Diagnostics.IssueDetail.Should().Contain("Select an issue");
        }

        [AvaloniaFact]
        public async Task Diagnostics_SelectingAnIssueShowsItsDetail()
        {
            var logPath = Path.Combine(_root, "Player.log");
            WriteWaylithLog(logPath);
            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            var leak = vm.Diagnostics.Issues.First(i => i.Title == "Native Memory Leak Report");
            vm.Diagnostics.SelectedIssue = leak;

            vm.Diagnostics.IssueDetailTitle.Should().Be("Native Memory Leak Report");
            vm.Diagnostics.IssueDetail.Should().Contain("Severity: Warning");
            vm.Diagnostics.IssueDetail.Should().Contain("Occurrences: 1");
            vm.Diagnostics.IssueDetail.Should().Contain("First at line");
            vm.Diagnostics.IssueDetail.Should().Contain("What it means");
        }

        // ── Shared log exports (HugsLib-family publishers) ──────────────────

        [AvaloniaFact]
        public async Task LoadAsync_SharedExport_LoadsOnlyTheLogBody()
        {
            var exportPath = Path.Combine(_root, "hugslib-share.txt");
            File.WriteAllText(exportPath, HugsLogEnvelopeTests.SampleUpload);

            var vm = CreateViewModel();
            await vm.LoadAsync(exportPath);

            // The envelope is stripped from the content: no publisher lines
            // reach the display, filters or reports.
            vm.Lines.Should().NotBeEmpty();
            vm.Lines.Should().NotContain(l => l.Text.Contains("Log uploaded on"));
            vm.Lines.Should().NotContain(l =>
                l.Text.Contains("Loaded mods:") ||
                l.Text.Contains("Active Harmony patches:") ||
                l.Text.Contains("Harmony versions present:"));
            vm.Lines.Should().Contain(l => l.Text.Contains("Root level exception"));
            vm.Stats.Errors.Should().Be(1);

            // The body's own line numbers survive, so "first at line N" in the
            // troubleshooter points into the sharer's log, not the shared file.
            vm.PathText.Should().Be(exportPath);
            vm.StatusMessage.Should().Contain("shared log export");
            vm.SourceOptions[0].Label.Should().Contain("shared export");
        }

        [AvaloniaFact]
        public async Task LoadAsync_SharedExport_SummaryCarriesThePublisherMetadata()
        {
            var exportPath = Path.Combine(_root, "hugslib-share.txt");
            File.WriteAllText(exportPath, HugsLogEnvelopeTests.SampleUpload);

            var vm = CreateViewModel();
            await vm.LoadAsync(exportPath);
            var rows = vm.Diagnostics.SummaryRows;

            // The roster wins over the body scan: the log itself lists no mods.
            rows.Should().Contain("Mods listed: 5");
            rows.Should().Contain(r => r.StartsWith("Shared export: uploaded"));
            rows.Should().Contain(r => r.StartsWith("Mod list source:"));
            rows.Should().Contain("Harmony builds: 3.0.0.0 (2 mods), 2.4.3.0 (2 mods), 2.3.6.0 (2 mods)");
            rows.Should().Contain("Patched methods: 3 (from the sharer's patch inventory)");
            rows.Should().Contain(r => r.Contains("different Harmony builds are loaded at once"));
            rows.Should().Contain("Publisher platform info: (hidden, use publishing options to include)");

            // Reports still analyze the extracted body.
            vm.Diagnostics.HasContent.Should().BeTrue();
            vm.Diagnostics.Issues.Should().Contain(i => i.Title == "Incomplete Mod Metadata");
        }

        [AvaloniaFact]
        public async Task LoadAsync_PlainLog_GetsNoExportRows()
        {
            var logPath = Path.Combine(_root, "Player.log");
            File.WriteAllText(logPath,
                "=== Waylith log ===\n" +
                "Started:   2026-11-07 18:31:56.481 +03:00\n" +
                "2026-11-07 18:31:56.481  INFO [Verse] RimWorld 1.7.5000 rev1275\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            vm.Diagnostics.SummaryRows.Should().NotContain(r => r.StartsWith("Shared export:"));
            vm.Diagnostics.SummaryRows.Should().NotContain(r => r.StartsWith("Harmony builds:"));
            vm.StatusMessage.Should().Be("Loaded.");
            vm.SourceOptions[0].Label.Should().Contain("Player.log");
        }

        private sealed class FakeFileDialogService : IFileDialogService        {
            public (bool Success, string? FilePath) OpenResult { get; set; } = (true, null);

            public (bool Success, string? FilePath) SaveResult { get; set; } = (true, null);

            public string? LastSaveFileName { get; private set; }

            public string? LastOpenFilter { get; private set; }

            public string? LastOpenTitle { get; private set; }

            public Task<(bool Success, string? FilePath)> ShowOpenFileDialogAsync(string title, string filter, string? initialDirectory = null)
            {
                LastOpenFilter = filter;
                LastOpenTitle = title;
                return Task.FromResult(OpenResult);
            }

            public Task<(bool Success, string? FilePath)> ShowSaveFileDialogAsync(
                string title, string filter, string? initialDirectory = null, string? defaultExtension = null, string? defaultFileName = null)
            {
                LastSaveFileName = defaultFileName;
                return Task.FromResult(SaveResult);
            }

            public Task<(bool Success, string? Path)> ShowOpenFolderDialogAsync(string title, string? initialDirectory = null) =>
                Task.FromResult<(bool Success, string? Path)>((true, null));
        }
    }
}
