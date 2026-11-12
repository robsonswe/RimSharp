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

        /// <summary>Counts the colonist name scans the dialog did.</summary>
        private int ColonistNameScans()
            => _saveHeaderService.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ISaveHeaderService.ReadColonistNamesAsync));

        /// <summary>The details of a save that could be read.</summary>
        private static SaveDetails ReadableDetails()
            => new SaveDetails(
                "Crashland", "Cassandra", "Rough",
                100, 200, 5500,
                "43378,0", "Spring",
                "Maia Octus", "danielle",
                "PlayerTribe",
                "Five lost tribespeople attempt to rebuild.",
                "Your tribe was destroyed by the great blood machines.",
                false,
                Array.Empty<string>());

        /// <summary>A colonist of the test save.</summary>
        private static SaveColonist Colonist(
            string first, string? nick, string? last,
            string? gender = null,
            string? xenotype = null,
            double? biologicalAge = null,
            IReadOnlyList<string>? traits = null,
            IReadOnlyList<SaveColonistSkill>? skills = null,
            IReadOnlyList<SaveColonistCondition>? conditions = null)
            => new SaveColonist(
                first, nick, last, gender, xenotype, biologicalAge,
                traits ?? Array.Empty<string>(),
                skills ?? Array.Empty<SaveColonistSkill>(),
                conditions ?? Array.Empty<SaveColonistCondition>());

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
            vm.Details.CompatSummaryText.Should().Be("Can't be compared");
            vm.Details.CompatNote.Should().Be("No mods recorded in this save — compatibility can't be judged.");
            vm.Details.ModsInSaveText.Should().Be("Mods in save: 0");
            vm.Details.HasDiff.Should().BeFalse();
        }

        [AvaloniaFact]
        public async Task ASelectionWithAnUninstalledMod_ShowsTheUninstalledVerdictAndTheUninstalledIds()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("Uninstalled", DateTime.UtcNow);
            SetHeader(save, "mod.alpha", "mod.gone");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            vm.Details.CompatSummaryText.Should().Be("Differs - some mods are not installed");
            vm.Details.ModsInSaveText.Should().Be("Mods in save: 2");
            vm.Details.HasUninstalledMods.Should().BeTrue();
            vm.Details.UninstalledModsText.Should().Be("Uninstalled: mod.gone");
            vm.Details.DiffSummaryText.Should().Be("1 removed");
        }

        [AvaloniaFact]
        public async Task TheModCardDefaultsToTheSavesOwnList_WithoutTheStatusTags()
        {
            SetModContext(
                installed: new[] { "mod.alpha", "mod.beta", "mod.delta" },
                active: new[] { "mod.alpha", "mod.delta" });

            var save = Entry("Own", DateTime.UtcNow);
            SetHeader(save, "mod.alpha", "mod.beta", "mod.gone");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            // The default list is the save's own: the active-but-not-saved mod
            // is not in it, and no row carries a status tag.
            vm.Details.ModRows.Should().HaveCount(3);
            vm.Details.ModRows.Select(row => row.PackageId)
                .Should().Equal("mod.alpha", "mod.beta", "mod.gone");
            vm.Details.ModRows.Should().OnlyContain(row => !row.HasStatus);
            vm.Details.HasDiff.Should().BeTrue();
        }

        [AvaloniaFact]
        public async Task TheDifferencesView_ShowsTheCurrentListFirst_AndThenTheSaveOnlyMods()
        {
            SetModContext(
                installed: new[] { "mod.alpha", "mod.beta", "mod.delta" },
                active: new[] { "mod.alpha", "mod.delta" });

            var save = Entry("Diff", DateTime.UtcNow);
            SetHeader(save, "mod.alpha", "mod.beta", "mod.gone");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            vm.Details.ShowDifferences = true;

            // The differences view is the current list in its current order,
            // tagged, then the mods the save has but the list no longer does,
            // in the order of the save.
            vm.Details.ModRows.Should().HaveCount(4);
            vm.Details.ModRows.Select(row => (row.PackageId, row.StatusText))
                .Should().Equal(
                    ("mod.alpha", string.Empty),
                    ("mod.delta", "Added"),
                    ("mod.beta", "Removed"),
                    ("mod.gone", "Uninstalled"));

            // Back to the save's own list: the tags and the added mod leave.
            vm.Details.ShowDifferences = false;
            vm.Details.ModRows.Select(row => row.PackageId)
                .Should().Equal("mod.alpha", "mod.beta", "mod.gone");
            vm.Details.ModRows.Should().OnlyContain(row => !row.HasStatus);
        }

        [AvaloniaFact]
        public async Task AReorderedSave_TagsTheMovedModsInTheDifferencesView()
        {
            SetModContext(
                installed: new[] { "mod.alpha", "mod.beta", "mod.gamma", "mod.delta" },
                active: new[] { "mod.delta", "mod.alpha", "mod.beta", "mod.gamma" });

            var save = Entry("Order", DateTime.UtcNow);
            SetHeader(save, "mod.alpha", "mod.beta", "mod.gamma", "mod.delta");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            // The last mod moved to the front - the save differs, so the
            // differences view is available.
            vm.Details.HasDiff.Should().BeTrue();
            vm.Details.DiffSummaryText.Should().Be("3 reordered");

            vm.Details.ShowDifferences = true;

            // The rows follow the current list; the moved mod and the two mods
            // whose neighbours changed carry the Reordered tag.
            vm.Details.ModRows.Select(row => (row.PackageId, row.StatusText))
                .Should().Equal(
                    ("mod.delta", "Reordered"),
                    ("mod.alpha", "Reordered"),
                    ("mod.beta", string.Empty),
                    ("mod.gamma", "Reordered"));

            // The default view stays the save's own order, untagged.
            vm.Details.ShowDifferences = false;
            vm.Details.ModRows.Select(row => row.PackageId)
                .Should().Equal("mod.alpha", "mod.beta", "mod.gamma", "mod.delta");
            vm.Details.ModRows.Should().OnlyContain(row => !row.HasStatus);
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
            vm.Details.CompatSummaryText.Should().Be("Could not be read");
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

            vm.Details.CompatSummaryText.Should().Be("Differs from your active mods and DLCs");
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

            vm.Details.CompatSummaryText.Should().Be("Matches your active mods and DLCs");
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
                    null, null, null, null, null, null,
                    null, null, null, null, null, null,
                    null, null,
                    Array.Empty<string>()));

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            vm.Details.GameVersionText.Should().Be("1.7.4990 rev1065");
            vm.Details.ScenarioText.Should().Be("—");
            vm.Details.ScenarioSummaryText.Should().Be("—");
            vm.Details.ScenarioDescriptionText.Should().Be("—");
            vm.Details.StorytellerText.Should().Be("—");
            vm.Details.DifficultyText.Should().Be("—");
            vm.Details.FactionTypeText.Should().Be("—");
            vm.Details.WorldNameText.Should().Be("—");
            vm.Details.WorldSeedText.Should().Be("—");
            vm.Details.PlanetModeText.Should().Be("—");
            vm.Details.InGameDateText.Should().Be("—");
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
                    "43378,0", "Spring",
                    "Maia Octus", "danielle",
                    "PlayerTribe",
                    "Five lost tribespeople attempt to rebuild.",
                    "Your tribe was destroyed by the great blood machines.",
                    false,
                    Array.Empty<string>()));

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            vm.Details.HasSelection.Should().BeTrue();
            vm.Details.HasError.Should().BeFalse();
            vm.Details.SaveName.Should().Be("Colony");
            vm.Details.FilePath.Should().Be(save.Path);

            vm.Details.ScenarioText.Should().Be("Crashland");
            vm.Details.ScenarioSummaryText.Should().Be("Five lost tribespeople attempt to rebuild.");
            vm.Details.ScenarioDescriptionText.Should().Be("Your tribe was destroyed by the great blood machines.");
            vm.Details.StorytellerText.Should().Be("Cassandra");
            vm.Details.DifficultyText.Should().Be("Rough");
            vm.Details.FactionTypeText.Should().Be("Tribe");
            vm.Details.WorldNameText.Should().Be("Maia Octus");
            vm.Details.WorldSeedText.Should().Be("danielle");
            vm.Details.PlanetModeText.Should().Be("No");

            // The date follows the game's own calendar: the quadrum (the game
            // has no season calendar), the day of the quadrum, the hour - and
            // the season the game itself reported for the save.
            vm.Details.InGameDateText.Should().Be("Year 5500, Aprimay, day 1, 00:07, Spring");

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
                null, null, null, null, null, null,
                null, null, null, null, null, null,
                null, null,
                Array.Empty<string>()));
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

            // The scan path is read-only as well: the service only reads.
            var serviceMethods = typeof(ISaveHeaderService)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(method => method.Name)
                .ToList();

            foreach (var forbidden in new[] { "Delete", "Rename", "Export", "Import", "Write", "Move", "Copy", "Backup" })
            {
                serviceMethods.Should().NotContain(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
            }
        }

        [AvaloniaFact]
        public async Task SelectingASave_ScansTheColonistNamesOnce()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("Colony", DateTime.UtcNow);
            SetHeader(save, "mod.alpha");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            _saveHeaderService.ReadDetailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ReadableDetails());

            _saveHeaderService.ReadColonistNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new List<SaveColonist>
                {
                    Colonist("Hayden", "Hayhouse", "Simons"),
                    Colonist("Kelux", null, "Hybridge")
                });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);
            await (vm.ColonistNamesTask ?? Task.CompletedTask);

            // The selection alone starts the scan - exactly once.
            ColonistNameScans().Should().Be(1);
            _ = _saveHeaderService.Received(1).ReadColonistNamesAsync(save.Path, Arg.Any<CancellationToken>());

            vm.Details.Colonists.Should().HaveCount(2);
            vm.Details.Colonists[0].NameText.Should().Be("Hayden \"Hayhouse\" Simons");
            vm.Details.Colonists[1].NameText.Should().Be("Kelux Hybridge");
            vm.Details.HasColonistNames.Should().BeTrue();
            vm.Details.ColonistNamesLoaded.Should().BeTrue();
            vm.Details.HasColonistNamesNote.Should().BeFalse();
        }

        [AvaloniaFact]
        public async Task TheColonists_ShowTheirAgeTraitsSkillsAndConditions()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("Colony", DateTime.UtcNow);
            SetHeader(save, "mod.alpha");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            _saveHeaderService.ReadDetailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ReadableDetails());

            _saveHeaderService.ReadColonistNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new List<SaveColonist>
                {
                    Colonist("Hayden", "Hayhouse", "Simons",
                        gender: "Female",
                        xenotype: "Baseliner",
                        biologicalAge: 27.6,
                        traits: new[] { "Kind", "Jealous" },
                        skills: new[]
                        {
                            new SaveColonistSkill("Shooting", 11, "Major"),
                            new SaveColonistSkill("Plants", 0, null)
                        },
                        conditions: new[]
                        {
                            new SaveColonistCondition("Stab", 3d, "Hediff_Injury", "LowPain"),
                            new SaveColonistCondition("BadBack", 0.33d, "HediffWithComps", null)
                        })
                });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);
            await (vm.ColonistNamesTask ?? Task.CompletedTask);

            SaveColonistItemViewModel colonist = vm.Details.Colonists[0];

            // The gender and the xenotype are their own compact lines.
            colonist.HasGender.Should().BeTrue();
            colonist.GenderText.Should().Be("Gender: Female");
            colonist.HasXenotype.Should().BeTrue();
            colonist.XenotypeText.Should().Be("Xenotype: Baseliner");

            // The years are full years: the game truncates the ticks towards
            // zero (27.6 years are 27, not 28).
            colonist.HasAge.Should().BeTrue();
            colonist.AgeText.Should().Be("Age: 27");

            colonist.HasTraits.Should().BeTrue();
            colonist.TraitsText.Should().Be("Traits: Kind, Jealous");

            // The passion is marked: "++" for a major one, nothing for none.
            colonist.HasSkills.Should().BeTrue();
            colonist.SkillsText.Should().Be("Skills: Shooting 11++, Plants 0");

            // The severity of an injury is points, the other one a percentage.
            colonist.HasConditions.Should().BeTrue();
            colonist.ConditionsText.Should().Be("Conditions: Stab (severity 3, LowPain), BadBack (33%)");
        }

        [AvaloniaFact]
        public async Task TheColonists_CarryNoStatusSuffix()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("Colony", DateTime.UtcNow);
            SetHeader(save, "mod.alpha");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            _saveHeaderService.ReadDetailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ReadableDetails());

            _saveHeaderService.ReadColonistNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new List<SaveColonist>
                {
                    Colonist("Meredith", "Latch", "Abernathy", gender: "Female")
                });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);
            await (vm.ColonistNamesTask ?? Task.CompletedTask);

            // Every pawn stays in the list - but none of them is labeled: the
            // name line carries the name and nothing else.
            SaveColonistItemViewModel colonist = vm.Details.Colonists[0];
            colonist.NameText.Should().Be("Meredith \"Latch\" Abernathy");
            colonist.NameText.Should().NotContain("slave");
        }

        [AvaloniaFact]
        public async Task AColonistWithoutData_HasNoLinesForIt()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("Colony", DateTime.UtcNow);
            SetHeader(save, "mod.alpha");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            _saveHeaderService.ReadDetailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ReadableDetails());

            _saveHeaderService.ReadColonistNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new List<SaveColonist> { Colonist("Kiriko", null, "O'Connell") });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);
            await (vm.ColonistNamesTask ?? Task.CompletedTask);

            // A line without data is left out - only the name is shown. The
            // gender is the exception: a human always has one.
            SaveColonistItemViewModel colonist = vm.Details.Colonists[0];
            colonist.NameText.Should().Be("Kiriko O'Connell");
            colonist.HasXenotype.Should().BeFalse();
            colonist.XenotypeText.Should().BeEmpty();
            colonist.HasAge.Should().BeFalse();
            colonist.AgeText.Should().BeEmpty();
            colonist.HasTraits.Should().BeFalse();
            colonist.HasSkills.Should().BeFalse();
            colonist.HasConditions.Should().BeFalse();
        }

        [AvaloniaFact]
        public async Task TheColonistBlocks_ShowTheGender()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("Colony", DateTime.UtcNow);
            SetHeader(save, "mod.alpha");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            _saveHeaderService.ReadDetailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ReadableDetails());

            // The scribe omits the gender element when the value equals its
            // default - the parser reads such a pawn as male.
            _saveHeaderService.ReadColonistNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new List<SaveColonist> { Colonist("Tobias", "Toby", "Ytterstad", gender: "Male") });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);
            await (vm.ColonistNamesTask ?? Task.CompletedTask);

            SaveColonistItemViewModel colonist = vm.Details.Colonists[0];
            colonist.HasGender.Should().BeTrue();
            colonist.GenderText.Should().Be("Gender: Male");
        }

        [AvaloniaFact]
        public async Task TheColonistNames_AreReadInTheBackground_AndDoNotBlockTheDetails()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("Colony", DateTime.UtcNow);
            SetHeader(save, "mod.alpha");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            _saveHeaderService.ReadDetailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ReadableDetails());

            var pending = new TaskCompletionSource<IReadOnlyList<SaveColonist>>();
            _saveHeaderService.ReadColonistNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(pending.Task);

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            // The details are already shown - the scan runs behind them.
            vm.Details.ScenarioText.Should().Be("Crashland");
            vm.Details.IsLoadingColonistNames.Should().BeTrue();
            vm.Details.Colonists.Should().BeEmpty();

            pending.SetResult(new List<SaveColonist>
            {
                Colonist("Hayden", "Hayhouse", "Simons")
            });
            await (vm.ColonistNamesTask ?? Task.CompletedTask);

            vm.Details.Colonists.Should().HaveCount(1);
            vm.Details.Colonists[0].NameText.Should().Be("Hayden \"Hayhouse\" Simons");
            vm.Details.IsLoadingColonistNames.Should().BeFalse();
        }

        [AvaloniaFact]
        public async Task ChangingTheSelectionMidScan_DropsTheStaleResult()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var first = Entry("First", DateTime.UtcNow);
            var second = Entry("Second", DateTime.UtcNow.AddMinutes(-1));
            SetHeader(first, "mod.alpha");
            SetHeader(second, "mod.alpha");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { first, second });

            _saveHeaderService.ReadDetailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ReadableDetails());

            var pendingFirst = new TaskCompletionSource<IReadOnlyList<SaveColonist>>();
            var pendingSecond = new TaskCompletionSource<IReadOnlyList<SaveColonist>>();
            _saveHeaderService.ReadColonistNamesAsync(first.Path, Arg.Any<CancellationToken>())
                .Returns(pendingFirst.Task);
            _saveHeaderService.ReadColonistNamesAsync(second.Path, Arg.Any<CancellationToken>())
                .Returns(pendingSecond.Task);

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);
            Task? firstScan = vm.ColonistNamesTask;

            // The user picks the other save while the first scan still runs.
            vm.SelectedSave = vm.Saves[1];
            await (vm.LoadTask ?? Task.CompletedTask);

            // The new selection scans on its own.
            ColonistNameScans().Should().Be(2);

            // The result of the first scan arrives late - it is dropped.
            pendingFirst.SetResult(new List<SaveColonist>
            {
                Colonist("Hayden", "Hayhouse", "Simons")
            });
            await (firstScan ?? Task.CompletedTask);

            vm.Details.Colonists.Should().BeEmpty();
            vm.Details.ColonistNamesLoaded.Should().BeFalse();
            vm.Details.IsLoadingColonistNames.Should().BeTrue();

            // The scan of the new save still delivers its own result.
            pendingSecond.SetResult(new List<SaveColonist>
            {
                Colonist("Kiriko", null, "O'Connell")
            });
            await (vm.ColonistNamesTask ?? Task.CompletedTask);

            vm.Details.Colonists.Should().HaveCount(1);
            vm.Details.Colonists[0].NameText.Should().Be("Kiriko O'Connell");
        }

        [AvaloniaFact]
        public async Task SelectingAnotherSave_ResetsTheColonistNames()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var first = Entry("First", DateTime.UtcNow);
            var second = Entry("Second", DateTime.UtcNow.AddMinutes(-1));
            SetHeader(first, "mod.alpha");
            SetHeader(second, "mod.alpha");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { first, second });

            _saveHeaderService.ReadDetailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ReadableDetails());

            var pendingSecond = new TaskCompletionSource<IReadOnlyList<SaveColonist>>();
            _saveHeaderService.ReadColonistNamesAsync(first.Path, Arg.Any<CancellationToken>())
                .Returns(new List<SaveColonist> { Colonist("Hayden", "Hayhouse", "Simons") });
            _saveHeaderService.ReadColonistNamesAsync(second.Path, Arg.Any<CancellationToken>())
                .Returns(pendingSecond.Task);

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);
            await (vm.ColonistNamesTask ?? Task.CompletedTask);

            vm.Details.Colonists.Should().HaveCount(1);

            // Another save is selected - the names of the old one are gone.
            vm.SelectedSave = vm.Saves[1];
            await (vm.LoadTask ?? Task.CompletedTask);

            vm.Details.Colonists.Should().BeEmpty();
            vm.Details.ColonistNamesLoaded.Should().BeFalse();
            vm.Details.ShowColonistNamesUnknown.Should().BeFalse();
            vm.Details.IsLoadingColonistNames.Should().BeTrue();
        }

        [AvaloniaFact]
        public async Task AnEmptyScan_SaysSoHonestly()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("Empty", DateTime.UtcNow);
            SetHeader(save, "mod.alpha");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            _saveHeaderService.ReadDetailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ReadableDetails());

            _saveHeaderService.ReadColonistNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new List<SaveColonist>());

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);
            await (vm.ColonistNamesTask ?? Task.CompletedTask);

            ColonistNameScans().Should().Be(1);
            vm.Details.Colonists.Should().BeEmpty();
            vm.Details.HasColonistNames.Should().BeFalse();
            vm.Details.ColonistNamesLoaded.Should().BeTrue();
            vm.Details.ColonistNamesNote.Should().Be("No human pawns found.");
            vm.Details.ShowColonistNamesUnknown.Should().BeFalse();
        }

        [AvaloniaFact]
        public async Task ASaveThatCannotBeRead_IsNotScanned()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            // No header is registered - the save cannot be read at all.
            var save = Entry("Broken", DateTime.UtcNow);

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            // An unreadable save is not scanned - the section stays unknown.
            ColonistNameScans().Should().Be(0);
            vm.Details.HasError.Should().BeTrue();
            vm.Details.ShowColonistNamesUnknown.Should().BeTrue();
        }

        [AvaloniaFact]
        public async Task ASaveWithOnlyAHeader_IsNotScanned()
        {
            SetModContext(installed: new[] { "mod.alpha" }, active: new[] { "mod.alpha" });

            var save = Entry("HeaderOnly", DateTime.UtcNow);
            SetHeader(save, "mod.alpha");

            _saveHeaderService.ListSavesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<SaveFileEntry> { save });

            // The head window could not be read - only the header is there.
            _saveHeaderService.ReadDetailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((SaveDetails?)null);

            var vm = CreateViewModel();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            await (vm.LoadTask ?? Task.CompletedTask);

            // The save did not parse - no whole file read is spent on it, the
            // section shows the honest em dash like every other unknown field.
            ColonistNameScans().Should().Be(0);
            vm.Details.ShowSections.Should().BeTrue();
            vm.Details.ShowColonistNamesUnknown.Should().BeTrue();
        }
    }
}
