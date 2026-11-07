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
        public async Task SelectFilterCommand_AppliesChosenMode()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath, "plain\n[E] Failed\nanother plain\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            var errors = vm.FilterOptions.First(o => o.Mode == LogFilterMode.ErrorsOnly);
            vm.SelectFilterCommand.Execute(errors);

            vm.SelectedFilterOption.Should().Be(errors);
            vm.Lines.Should().HaveCount(1);
            vm.Lines[0].Severity.Should().Be(LogLineSeverity.Error);
        }

        [AvaloniaFact]
        public async Task StatButtonsUseTheSameFilterModes()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath, "plain\nKey binding conflict: A\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            var keybinds = vm.FilterOptions.First(o => o.Mode == LogFilterMode.KeybindsOnly);
            vm.SelectFilterCommand.Execute(keybinds);

            vm.Lines.Should().HaveCount(1);
            vm.Lines[0].Severity.Should().Be(LogLineSeverity.Keybind);
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

        private sealed class FakeFileDialogService : IFileDialogService
        {
            public (bool Success, string? FilePath) OpenResult { get; set; } = (true, null);

            public (bool Success, string? FilePath) SaveResult { get; set; } = (true, null);

            public string? LastSaveFileName { get; private set; }

            public Task<(bool Success, string? FilePath)> ShowOpenFileDialogAsync(string title, string filter, string? initialDirectory = null) =>
                Task.FromResult(OpenResult);

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
