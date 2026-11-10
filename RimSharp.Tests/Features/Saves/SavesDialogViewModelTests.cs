using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.Saves.ViewModels;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Services.ModManagement;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Features.Saves
{
    /// <summary>
    /// VM tests of the saves dialog. The header service is substituted - the
    /// comparison and the diff run through the real service, so the case
    /// handling of the mod ids is the one the app uses.
    /// </summary>
    public class SavesDialogViewModelTests : IDisposable
    {
        private readonly ISaveHeaderService _saveHeaderService = Substitute.For<ISaveHeaderService>();
        private readonly IModListManager _modListManager = Substitute.For<IModListManager>();
        private readonly ILoggerService _logger = Substitute.For<ILoggerService>();
        private readonly IPathService _pathService = Substitute.For<IPathService>();
        private readonly SaveHeaderService _realService;
        private readonly Dictionary<string, SaveHeader?> _headers = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _root;

        public SavesDialogViewModelTests()
        {
            TestApp.InitializeTestApp();

            _root = Path.Combine(Path.GetTempPath(), "rimsharp-saves-vm-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_root);

            _realService = new SaveHeaderService(_pathService, _logger);

            // The comparison and the diff are the real ones - only the file
            // access is substituted.
            _saveHeaderService.Compare(
                    Arg.Any<SaveHeader>(),
                    Arg.Any<IReadOnlyCollection<string>>(),
                    Arg.Any<IReadOnlyCollection<string>>())
                .Returns(call => _realService.Compare(
                    call.Arg<SaveHeader>(),
                    call.ArgAt<IReadOnlyCollection<string>>(1),
                    call.ArgAt<IReadOnlyCollection<string>>(2)));

            _saveHeaderService.Diff(
                    Arg.Any<SaveHeader>(),
                    Arg.Any<IReadOnlyCollection<string>>())
                .Returns(call => _realService.Diff(
                    call.Arg<SaveHeader>(),
                    call.ArgAt<IReadOnlyCollection<string>>(1)));

            _saveHeaderService.ReadHeaderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => _headers.TryGetValue(call.Arg<string>(), out var header) ? header : null);

            _modListManager.GetAllMods().Returns(Array.Empty<ModItem>());
            _modListManager.VirtualActiveMods.Returns(new List<(ModItem Mod, int LoadOrder)>());

            // Every test starts without a remembered selection.
            SavesDialogViewModel.LastSelectedPath = null;
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }

        #region Helpers

        private SavesDialogViewModel CreateViewModel()
            => new(_saveHeaderService, _modListManager);

        /// <summary>Sets the installed and the active mods the saves are compared against.</summary>
        private void SetModContext(string[] installed, string[] active)
        {
            _modListManager.GetAllMods()
                .Returns(installed.Select(id => new ModItem { PackageId = id }).ToList());

            var activeMods = new List<(ModItem Mod, int LoadOrder)>();
            for (int i = 0; i < active.Length; i++)
            {
                activeMods.Add((new ModItem { PackageId = active[i] }, i));
            }

            _modListManager.VirtualActiveMods.Returns(activeMods);
        }

        /// <summary>A save file in the test folder.</summary>
        private SaveFileEntry Entry(string name, DateTime modifiedUtc, long sizeBytes = 1024)
            => new(name, Path.Combine(_root, name + ".rws"), sizeBytes, modifiedUtc);

        /// <summary>Registers the header of a save file.</summary>
        private SaveHeader SetHeader(SaveFileEntry entry, params string[] modIds)
        {
            var header = new SaveHeader(
                entry.Name,
                entry.Path,
                "1.7.4990 rev1065",
                modIds,
                Array.Empty<string>(),
                Array.Empty<string>());

            _headers[entry.Path] = header;
            return header;
        }

        private static string FormatDate(DateTime utc)
            => utc.ToLocalTime().ToString("MMM d, yyyy, h:mm tt", CultureInfo.CurrentCulture);

        /// <summary>Counts the header reads the dialog did.</summary>
        private int HeaderReads()
            => _saveHeaderService.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ISaveHeaderService.ReadHeaderAsync));

        /// <summary>Counts the detail reads the dialog did.</summary>
        private int DetailReads()
            => _saveHeaderService.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ISaveHeaderService.ReadDetailsAsync));

        #endregion

        [AvaloniaFact]
        public async Task Refresh_LoadsTheSavesNewestFirst_WithSizeAndDate()
        {
            var older = Entry("Older", new DateTime(2026, 11, 8, 15, 14, 0, DateTimeKind.Utc), 12L * 1024 * 1024);
            var newer = Entry("Newer", new DateTime(2026, 11, 9, 15, 14, 0, DateTimeKind.Utc), 20L * 1024 * 1024);
            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { newer, older });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);

            vm.Saves.Should().HaveCount(2);
            vm.Saves[0].Name.Should().Be("Newer");
            vm.Saves[1].Name.Should().Be("Older");

            vm.Saves[0].SizeText.Should().Be("20 MB");
            vm.Saves[0].ModifiedText.Should().Be(FormatDate(newer.LastModifiedUtc));
            vm.Saves[0].SecondaryText.Should().Be($"20 MB · modified {FormatDate(newer.LastModifiedUtc)}");

            vm.CountText.Should().Be("2 saves");
            vm.ShowPlaceholder.Should().BeFalse();
        }

        [AvaloniaFact]
        public async Task TheListIsBuiltFromTheFileMetadataAlone()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var entries = Enumerable.Range(1, 5)
                .Select(i => Entry($"Save {i}", DateTime.UtcNow.AddMinutes(-i)))
                .ToList();

            foreach (var entry in entries)
            {
                SetHeader(entry, "mod.alpha");
            }

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>()).Returns(entries);

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);

            // Five saves are listed - but only the selected one is ever read.
            vm.Saves.Should().HaveCount(5);
            HeaderReads().Should().Be(1);
            DetailReads().Should().Be(1);

            // The newest save is selected on its own - that is the one read.
            vm.SelectedSave.Should().NotBeNull();
            vm.SelectedSave!.Name.Should().Be("Save 1");
            await (vm.LoadTask ?? Task.CompletedTask);
            HeaderReads().Should().Be(1);
            DetailReads().Should().Be(1);

            // A second selection reads exactly that save - nothing else.
            vm.SelectedSave = vm.Saves[1];
            await (vm.LoadTask ?? Task.CompletedTask);
            HeaderReads().Should().Be(2);
            DetailReads().Should().Be(2);

            // Revealing the rest of the list reads nothing at all.
            vm.IsExpanded = true;
            vm.VisibleSaves.Should().HaveCount(5);
            HeaderReads().Should().Be(2);
            DetailReads().Should().Be(2);

            // Every further selection adds exactly one read.
            vm.SelectedSave = vm.Saves[2];
            await (vm.LoadTask ?? Task.CompletedTask);
            HeaderReads().Should().Be(3);
            DetailReads().Should().Be(3);
        }

        [AvaloniaFact]
        public async Task ASelectionWithoutMods_ShowsTheUnknownVerdict()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("Nomods", DateTime.UtcNow);
            SetHeader(save);

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            vm.Details.HasSelection.Should().BeTrue();
            vm.Details.HasError.Should().BeFalse();
            vm.Details.CompatText.Should().Be("Unknown");
            vm.Details.CompatNote.Should().Be("No mods recorded in this save — compatibility can't be judged.");
            vm.Details.ModsInSaveText.Should().Be("Mods in save: 0");
            vm.Details.HasDiff.Should().BeFalse();
        }

        [AvaloniaFact]
        public async Task ASelectionWithAMissingMod_ShowsTheMissingVerdictAndTheMissingIds()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("Missing", DateTime.UtcNow);
            SetHeader(save, "mod.alpha", "mod.gone");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            vm.Details.CompatText.Should().Be("Missing");
            vm.Details.ModsInSaveText.Should().Be("Mods in save: 2");
            vm.Details.HasMissingMods.Should().BeTrue();
            vm.Details.MissingModsText.Should().Be("Missing: mod.gone");
            vm.Details.DiffSummaryText.Should().Be("0 added, 1 removed");
        }

        [AvaloniaFact]
        public async Task ASelectionWithAnUnreadableHeader_ShowsTheUnreadableState()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("Broken", DateTime.UtcNow);
            File.WriteAllText(save.Path, "this is not a save");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            vm.Details.HasError.Should().BeTrue();
            vm.Details.ErrorMessage.Should().Be("Could not read this save.");
            vm.Details.ErrorReason.Should().Be("The save is corrupt or in a format this reader can't parse.");
            vm.Details.CompatText.Should().Be("Unreadable");
            vm.Details.ShowSections.Should().BeFalse();
        }

        [AvaloniaFact]
        public async Task Diff_ListsTheAddedAndTheRemovedModsSorted()
        {
            SetModContext(
                installed: new[] { "mod.alpha", "mod.beta", "mod.gamma", "mod.delta" },
                active: new[] { "mod.delta", "mod.beta" });

            var save = Entry("Diffed", DateTime.UtcNow);
            SetHeader(save, "mod.gamma", "mod.alpha");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            vm.Details.CompatText.Should().Be("Changed");
            vm.Details.ModsInSaveText.Should().Be("Mods in save: 2");
            vm.Details.DiffSummaryText.Should().Be("2 added, 2 removed");
            vm.Details.AddedModsText.Should().Be("Added: mod.beta, mod.delta");
            vm.Details.RemovedModsText.Should().Be("Removed: mod.alpha, mod.gamma");
        }

        [AvaloniaFact]
        public async Task TheComparisonIgnoresTheCaseOfTheModIds()
        {
            SetModContext(
                installed: new[] { "mod.alpha", "mod.beta" },
                active: new[] { "mod.alpha", "mod.beta" });

            var save = Entry("Cased", DateTime.UtcNow);
            SetHeader(save, "MOD.ALPHA", "Mod.Beta");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            vm.Details.CompatText.Should().Be("Compatible");
        }

        [AvaloniaFact]
        public async Task Details_ShowAnEmDashForEveryFieldTheSaveDoesNotCarry()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("Sparse", DateTime.UtcNow);
            SetHeader(save, "mod.alpha");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            _saveHeaderService.ReadDetailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new SaveDetails(
                    null, null, null, null, null, null, null,
                    null, null, null, null, null, null, Array.Empty<string>()));

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            vm.Details.GameVersionText.Should().Be("1.7.4990 rev1065");
            vm.Details.ScenarioText.Should().Be("—");
            vm.Details.StorytellerText.Should().Be("—");
            vm.Details.DifficultyText.Should().Be("—");
            vm.Details.InGameDateText.Should().Be("—");
            vm.Details.StartingTileText.Should().Be("—");
            vm.Details.WealthTotalText.Should().Be("—");
            vm.Details.WealthItemsText.Should().Be("—");
            vm.Details.WealthBuildingsText.Should().Be("—");
            vm.Details.FreeColonistsText.Should().Be("—");
            vm.Details.PrisonersText.Should().Be("—");
            vm.Details.CompletedResearchText.Should().Be("—");
            vm.Details.ActiveDlcText.Should().Be("—");
        }

        [AvaloniaFact]
        public async Task Details_ShowTheFieldsOfTheSelectedSave()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("Colony", DateTime.UtcNow);
            SetHeader(save, "mod.alpha", "ludeon.rimworld.anomaly", "ludeon.rimworld.royalty");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            _saveHeaderService.ReadDetailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new SaveDetails(
                    "Crashland", "Cassandra", "Rough",
                    100, 200, 5500,
                    "43378,0",
                    15954.16, 14946.57, 1007.59,
                    5, 1, 12,
                    Array.Empty<string>()));

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            vm.Details.HasSelection.Should().BeTrue();
            vm.Details.HasError.Should().BeFalse();
            vm.Details.SaveName.Should().Be("Colony");
            vm.Details.FilePath.Should().Be(save.Path);

            vm.Details.ScenarioText.Should().Be("Crashland");
            vm.Details.StorytellerText.Should().Be("Cassandra");
            vm.Details.DifficultyText.Should().Be("Rough");
            vm.Details.InGameDateText.Should().Be("Year 5500, Spring, day 1, 00:00");
            vm.Details.StartingTileText.Should().Be("43378,0");
            vm.Details.WealthTotalText.Should().Be(15954.16.ToString("N0", CultureInfo.CurrentCulture));
            vm.Details.FreeColonistsText.Should().Be("5");
            vm.Details.PrisonersText.Should().Be("1");
            vm.Details.CompletedResearchText.Should().Be("12");

            // The DLCs come from the mod ids of the save's meta block.
            vm.Details.ActiveDlcText.Should().Be("Royalty, Anomaly");
        }

        [AvaloniaFact]
        public async Task SelectingNull_ClearsTheDetails()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("Colony", DateTime.UtcNow);
            SetHeader(save, "mod.alpha");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            vm.Details.HasSelection.Should().BeTrue();

            vm.SelectedSave = null;

            vm.Details.HasSelection.Should().BeFalse();
            vm.Details.ShowHint.Should().BeTrue();
            vm.Details.SaveName.Should().BeEmpty();
        }

        [AvaloniaFact]
        public async Task ACorruptSave_ShowsTheHonestError()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("Corrupt", DateTime.UtcNow);
            // The file is there, but it cannot be read.
            File.WriteAllText(save.Path, "this is not a save");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            vm.Details.HasError.Should().BeTrue();
            vm.Details.ErrorMessage.Should().Be("Could not read this save.");
            vm.Details.ErrorReason.Should().Be("The save is corrupt or in a format this reader can't parse.");
        }

        [AvaloniaFact]
        public async Task ASaveThatVanished_SaysSoInsteadOfFailing()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("Vanished", DateTime.UtcNow);
            // The file does not exist - it was deleted after the listing.

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            vm.Details.HasError.Should().BeTrue();
            vm.Details.ErrorMessage.Should().Be("Could not read this save.");
            vm.Details.ErrorReason.Should().Be(
                "Save no longer exists — it may have been deleted or moved. Refresh the list.");
        }

        [AvaloniaFact]
        public async Task TheLoadingStates_ToggleWhileTheFolderIsRead()
        {
            var pending = new TaskCompletionSource<IReadOnlyList<SaveFileEntry>>();
            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>()).Returns(pending.Task);

            var vm = CreateViewModel();
            var refresh = vm.RefreshAsync(TestContext.Current.CancellationToken);

            vm.IsLoadingList.Should().BeTrue();
            vm.RefreshCommand.CanExecute(null).Should().BeFalse();

            pending.SetResult(Array.Empty<SaveFileEntry>());
            await refresh;

            vm.IsLoadingList.Should().BeFalse();
            vm.RefreshCommand.CanExecute(null).Should().BeTrue();
        }

        [AvaloniaFact]
        public async Task TheDetailsShowTheReadingStateWhileTheSaveIsRead()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("Slow", DateTime.UtcNow);
            SetHeader(save, "mod.alpha");

            var pending = new TaskCompletionSource<SaveDetails?>();
            _saveHeaderService.ReadDetailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(pending.Task);
            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);

            vm.Details.IsLoading.Should().BeTrue();
            vm.Details.HasError.Should().BeFalse();

            pending.SetResult(new SaveDetails(
                null, null, null, null, null, null, null,
                null, null, null, null, null, null, Array.Empty<string>()));
            await (vm.LoadTask ?? Task.CompletedTask);

            vm.Details.IsLoading.Should().BeFalse();
        }

        [AvaloniaFact]
        public async Task AnEmptyFolder_ShowsThePlaceholder_AndLookAgainReadsAgain()
        {
            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(Array.Empty<SaveFileEntry>());

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);

            vm.Saves.Should().BeEmpty();
            vm.ShowPlaceholder.Should().BeTrue();
            vm.StatusMessage.Should().Contain("No saves found");
            vm.Details.ShowHint.Should().BeTrue();

            // The "Look again" button runs the same refresh.
            await vm.RefreshAsync(TestContext.Current.CancellationToken);

            _ = _saveHeaderService.Received(2).ListSavesAsync(Arg.Any<CancellationToken>());
        }

        [AvaloniaFact]
        public async Task TheSelection_SurvivesTheDialogBeingOpenedAgain()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var newest = Entry("Newest", DateTime.UtcNow);
            var older = Entry("Older", DateTime.UtcNow.AddMinutes(-1));
            SetHeader(newest, "mod.alpha");
            SetHeader(older, "mod.alpha");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { newest, older });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            // The newest save is selected on its own.
            vm.SelectedSave.Should().NotBeNull();
            vm.SelectedSave!.Name.Should().Be("Newest");

            // The user picks the older save.
            vm.SelectedSave = vm.Saves[1];
            await (vm.LoadTask ?? Task.CompletedTask);
            SavesDialogViewModel.LastSelectedPath.Should().Be(older.Path);

            // The dialog is opened again - the same save is selected.
            var reopened = CreateViewModel();
            await reopened.RefreshAsync(TestContext.Current.CancellationToken);

            reopened.SelectedSave.Should().NotBeNull();
            reopened.SelectedSave!.Path.Should().Be(older.Path);
        }

        [AvaloniaFact]
        public async Task ASelectionThatIsGone_FallsBackToTheNewestSave()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var newest = Entry("Newest", DateTime.UtcNow);
            var older = Entry("Older", DateTime.UtcNow.AddMinutes(-1));
            SetHeader(newest, "mod.alpha");
            SetHeader(older, "mod.alpha");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { newest, older });

            // The save that was selected last is not there any more.
            SavesDialogViewModel.LastSelectedPath = Path.Combine(_root, "Gone.rws");

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);

            vm.SelectedSave.Should().NotBeNull();
            vm.SelectedSave!.Name.Should().Be("Newest");
        }

        [AvaloniaFact]
        public async Task TheListing_ShowsEightRows_AndTheExpanderRevealsTheRest()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var entries = Enumerable.Range(1, 12)
                .Select(i => Entry($"Save {i}", DateTime.UtcNow.AddMinutes(-i)))
                .ToList();

            foreach (var entry in entries)
            {
                SetHeader(entry, "mod.alpha");
            }

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>()).Returns(entries);

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);

            vm.InlineSaves.Should().HaveCount(8);
            vm.ExpandedSaves.Should().HaveCount(4);
            vm.VisibleSaves.Should().HaveCount(8);
            vm.HasMoreSaves.Should().BeTrue();
            vm.MoreCountText.Should().Be("+4 more");

            vm.IsExpanded = true;

            vm.VisibleSaves.Should().HaveCount(12);
            vm.VisibleSaves[0].Should().BeSameAs(vm.Saves[0]);
            vm.VisibleSaves[11].Should().BeSameAs(vm.Saves[11]);
        }

        [AvaloniaFact]
        public void TheDialog_ExposesNoCommandThatWrites()
        {
            var commands = typeof(SavesDialogViewModel)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => typeof(ICommand).IsAssignableFrom(property.PropertyType))
                .Select(property => property.Name)
                .ToList();

            commands.Should().Contain("RefreshCommand");
            commands.Should().Contain("CloseCommand");

            foreach (var forbidden in new[] { "Delete", "Rename", "Export", "Import", "Write", "Move", "Copy", "Backup" })
            {
                commands.Should().NotContain(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
