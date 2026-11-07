using System.IO;
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
        public async Task LoadAsync_ClassifiesLinesWithSeverities()
        {
            var logPath = Path.Combine(_root, "any.log");
            File.WriteAllText(logPath,
                "11:04:37 PM Loaded mods\nKey binding conflict: A\nUnhandled exception\n   at Verse.Awake ()\n");

            var vm = CreateViewModel();
            await vm.LoadAsync(logPath);

            vm.Lines[0].Severity.Should().Be(LogLineSeverity.Info);
            vm.Lines[0].SeverityBrushKey.Should().Be("BrushTextMuted");
            vm.Lines[1].Severity.Should().Be(LogLineSeverity.Keybind);
            vm.Lines[1].SeverityBrushKey.Should().Be("BrushTextWarning");
            vm.Lines[2].Severity.Should().Be(LogLineSeverity.Exception);
            vm.Lines[2].SeverityBrushKey.Should().Be("BrushTextError");
            // Stack-trace frames inherit exception severity.
            vm.Lines[3].Severity.Should().Be(LogLineSeverity.Exception);
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
    }
}
