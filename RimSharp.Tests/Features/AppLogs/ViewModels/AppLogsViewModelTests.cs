using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.AppLogs.Services;
using RimSharp.Features.AppLogs.ViewModels;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Features.AppLogs.ViewModels
{
    /// <summary>
    /// VM tests write log files in the exact shape the app's logger writes
    /// and read them back through the window's own path.
    /// </summary>
    public class AppLogsViewModelTests : System.IDisposable
    {
        private readonly string _root;
        private readonly IConfigService _configService = Substitute.For<IConfigService>();

        public AppLogsViewModelTests()
        {
            TestApp.InitializeTestApp();
            _root = Path.Combine(Path.GetTempPath(), "rimsharp-applogs-vm-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_root);
            _configService.GetConfigValue("logs_directory").Returns(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }

        private void WriteLog(string module, string date, params string[] lines) =>
            File.WriteAllLines(Path.Combine(_root, $"RimSharp_{module}_{date}.log"), lines);

        private AppLogsViewModel CreateViewModel(IFileDialogService? files = null) =>
            new(_configService, fileDialogService: files);

        [AvaloniaFact]
        public async Task Refresh_GroupsFilesByModule_NewestDayFirst()
        {
            WriteLog("SteamCmd_Downloader", "20261107", "2026-11-07 18:31:56.481 [INFO] [SteamCmd_Downloader] today");
            WriteLog("SteamCmd_Downloader", "20261106", "2026-11-06 18:31:56.481 [INFO] [SteamCmd_Downloader] yesterday");
            WriteLog("General", "20261107", "2026-11-07 18:31:56.481 [INFO] [General] hello");

            var vm = CreateViewModel();
            await vm.RefreshAsync();

            vm.Modules.Should().HaveCount(2);
            var downloader = vm.Modules.Single(m => m.Module == "SteamCmd_Downloader");
            downloader.FileCount.Should().Be(2);
            downloader.Newest.Date.Should().Be(new System.DateTime(2026, 11, 7));
            downloader.SubHeader.Should().Contain("2026-11-07").And.Contain("1 older day");
            // Same-day modules order alphabetically — the list is stable.
            vm.Modules[0].Module.Should().Be("General");

            // The first module is preselected and loaded.
            vm.SelectedModule.Should().Be(vm.Modules[0]);
            await (vm.LoadTask ?? Task.CompletedTask);
            vm.Messages.Should().ContainSingle(m => m.Entry.Text == "hello");
            vm.FilePathText.Should().Contain("RimSharp_General_20261107.log");

            // Selecting a group loads that service's newest day.
            vm.SelectedModule = downloader;
            await (vm.LoadTask ?? Task.CompletedTask);
            vm.Messages.Should().ContainSingle(m => m.Entry.Text == "today");
            vm.FilePathText.Should().Contain("RimSharp_SteamCmd_Downloader_20261107.log");
        }

        [AvaloniaFact]
        public async Task SelectingAModule_LoadsItsNewestDay()
        {
            WriteLog("DownloadedItemProcessor", "20261107", "2026-11-07 18:31:56.481 [ERROR] [DownloadedItemProcessor] fresh failure");
            WriteLog("DownloadedItemProcessor", "20261106", "2026-11-06 18:31:56.481 [INFO] [DownloadedItemProcessor] old note");

            var vm = CreateViewModel();
            await vm.RefreshAsync();

            vm.Messages.Should().ContainSingle(m => m.Entry.Text == "fresh failure");
            vm.Stats.Error.Should().Be(1);
        }

        [AvaloniaFact]
        public async Task Stats_CountMessagesPerSeverity()
        {
            WriteLog("General", "20261107",
                "2026-11-07 18:31:56.481 [INFO] [General] a",
                "2026-11-07 18:31:57.481 [WARNING] [General] b",
                "2026-11-07 18:31:58.481 [ERROR] [General] c",
                "2026-11-07 18:31:59.481 [CRITICAL] [General] d");

            var vm = CreateViewModel();
            await vm.RefreshAsync();

            vm.Stats.Info.Should().Be(1);
            vm.Stats.Warning.Should().Be(1);
            vm.Stats.Error.Should().Be(1);
            vm.Stats.Critical.Should().Be(1);
            vm.Stats.Total.Should().Be(4);
            vm.LineCountText.Should().Be("4 messages");
        }

        [AvaloniaFact]
        public async Task LevelFilter_ShowsOnlyTheChosenSeverity()
        {
            WriteLog("General", "20261107",
                "2026-11-07 18:31:56.481 [INFO] [General] routine",
                "2026-11-07 18:31:57.481 [WARNING] [General] heads up",
                "2026-11-07 18:31:58.481 [ERROR] [General] broke");

            var vm = CreateViewModel();
            await vm.RefreshAsync();

            vm.SelectedLevelFilter = vm.LevelFilters.Single(f => f.Level == AppLogLevel.Warning);

            vm.Displayed.Should().ContainSingle();
            vm.Displayed[0].Entry.Text.Should().Be("heads up");
            vm.Displayed[0].SeverityBrushKey.Should().Be("BrushTextWarning");
            vm.LineCountText.Should().Be("1 of 3 messages");
        }

        [AvaloniaFact]
        public async Task Search_FiltersMessagesByText()
        {
            WriteLog("General", "20261107",
                "2026-11-07 18:31:56.481 [INFO] [General] installed mods: 61",
                "2026-11-07 18:31:57.481 [INFO] [General] checked for updates");

            var vm = CreateViewModel();
            await vm.RefreshAsync();

            vm.ApplySearch("mods");

            vm.Displayed.Should().ContainSingle();
            vm.Displayed[0].Entry.Text.Should().Contain("installed mods");
            vm.StatusMessage.Should().Be("Loaded.");
        }

        [AvaloniaFact]
        public async Task CollapseRepeats_MergesIdenticalConsecutiveMessages()
        {
            WriteLog("SteamCmd_Downloader", "20261107",
                "2026-11-07 18:31:56.481 [INFO] [SteamCmd_Downloader] waiting",
                "2026-11-07 18:31:57.481 [INFO] [SteamCmd_Downloader] waiting",
                "2026-11-07 18:31:58.481 [INFO] [SteamCmd_Downloader] waiting");

            var vm = CreateViewModel();
            await vm.RefreshAsync();

            vm.Messages.Should().HaveCount(1);
            vm.Messages[0].RepeatCount.Should().Be(3);
            vm.Messages[0].DisplayText.Should().Contain("Repeated 3 times");
            vm.Stats.Total.Should().Be(3); // stats count every written message

            vm.CollapseRepeats = false;
            vm.Messages.Should().HaveCount(3);
        }

        [AvaloniaFact]
        public async Task Export_WritesTheDisplayedMessages()
        {
            WriteLog("General", "20261107", "2026-11-07 18:31:56.481 [ERROR] [General] failure one");
            var exportPath = Path.Combine(_root, "out.txt");
            var files = new FakeFileDialogService { SaveResult = (true, exportPath) };

            var vm = CreateViewModel(files);
            await vm.RefreshAsync();

            await vm.ExportAsync();

            File.Exists(exportPath).Should().BeTrue();
            File.ReadAllText(exportPath).Should().Contain("failure one");
            vm.StatusMessage.Should().Contain("Exported 1 messages");
        }

        [AvaloniaFact]
        public async Task Refresh_NoLogFiles_ShowsTheEmptyState()
        {
            var vm = CreateViewModel();
            await vm.RefreshAsync();

            vm.Modules.Should().BeEmpty();
            vm.Messages.Should().BeEmpty();
            vm.ShowPlaceholder.Should().BeTrue();
            vm.StatusMessage.Should().Contain("No app log files");
        }

        [AvaloniaFact]
        public async Task Refresh_EmptyLogFile_LoadsNothingWithoutFailing()
        {
            WriteLog("General", "20261107");

            var vm = CreateViewModel();
            await vm.RefreshAsync();

            vm.Modules.Should().HaveCount(1);
            vm.Messages.Should().BeEmpty();
            vm.StatusMessage.Should().Contain("empty");
        }

        private sealed class FakeFileDialogService : IFileDialogService
        {
            public (bool Success, string? FilePath) SaveResult { get; set; } = (true, null);

            public string? LastSaveFileName { get; private set; }

            public Task<(bool Success, string? FilePath)> ShowOpenFileDialogAsync(string title, string filter, string? initialDirectory = null) =>
                Task.FromResult((true, (string?)null));

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
