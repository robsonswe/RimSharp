#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimSharp.AppDir.AppFiles;
using RimSharp.Services.ModManagement;

namespace RimSharp.Features.Saves.ViewModels
{
    /// <summary>
    /// The details panel of the saves dialog: the compatibility verdict, the
    /// story, the playground and the numbers of the selected save. Every field
    /// the save does not carry shows an em dash - a value is never invented.
    /// </summary>
    public sealed class SaveDetailsViewModel : ViewModelBase
    {
        /// <summary>Shown for every field the save does not carry.</summary>
        public const string UnknownValue = "—";

        private bool _hasSelection;
        private bool _isLoading;
        private bool _hasError;
        private string _errorMessage = string.Empty;
        private string _errorReason = string.Empty;
        private string _saveName = string.Empty;
        private string _filePath = string.Empty;

        private string _compatText = string.Empty;
        private string _compatBrushKey = "BrushStateNeutral";
        private string _compatNote = string.Empty;
        private string _modsInSaveText = string.Empty;
        private string _diffSummaryText = string.Empty;
        private string _addedModsText = string.Empty;
        private string _removedModsText = string.Empty;
        private string _missingModsText = string.Empty;

        private string _scenarioText = UnknownValue;
        private string _storytellerText = UnknownValue;
        private string _difficultyText = UnknownValue;
        private string _inGameDateText = UnknownValue;
        private string _startingTileText = UnknownValue;
        private string _gameVersionText = UnknownValue;
        private string _wealthTotalText = UnknownValue;
        private string _wealthItemsText = UnknownValue;
        private string _wealthBuildingsText = UnknownValue;
        private string _freeColonistsText = UnknownValue;
        private string _prisonersText = UnknownValue;
        private string _completedResearchText = UnknownValue;
        private string _activeDlcText = UnknownValue;

        #region State

        /// <summary>True while a save is selected (also while it is being read).</summary>
        public bool HasSelection => _hasSelection;

        /// <summary>True while the selected save is being read.</summary>
        public bool IsLoading => _isLoading;

        /// <summary>True when the selected save could not be read.</summary>
        public bool HasError => _hasError;

        /// <summary>The headline of the error state.</summary>
        public string ErrorMessage => _errorMessage;

        /// <summary>The reason behind the error state.</summary>
        public string ErrorReason => _errorReason;

        /// <summary>The name of the selected save.</summary>
        public string SaveName => _saveName;

        /// <summary>The full path of the selected save.</summary>
        public string FilePath => _filePath;

        /// <summary>True when nothing is selected - the panel shows its hint.</summary>
        public bool ShowHint => !_hasSelection && !_isLoading && !_hasError;

        /// <summary>True when the four sections can be shown.</summary>
        public bool ShowSections => _hasSelection && !_isLoading && !_hasError;

        #endregion

        #region Mods and compatibility

        /// <summary>The badge label of the selected save.</summary>
        public string CompatText => _compatText;

        /// <summary>The palette key the badge is painted with.</summary>
        public string CompatBrushKey => _compatBrushKey;

        /// <summary>The honest note for the states that cannot be judged.</summary>
        public string CompatNote => _compatNote;

        /// <summary>True when there is an honest note to show.</summary>
        public bool HasCompatNote => _compatNote.Length > 0;

        /// <summary>"Mods in save: {N}".</summary>
        public string ModsInSaveText => _modsInSaveText;

        /// <summary>"{N} added, {N} removed".</summary>
        public string DiffSummaryText => _diffSummaryText;

        /// <summary>True when a diff summary is shown.</summary>
        public bool HasDiff => _diffSummaryText.Length > 0;

        /// <summary>The mod ids that are active now but were not in the save.</summary>
        public string AddedModsText => _addedModsText;

        /// <summary>True when there are added mods to list.</summary>
        public bool HasAddedMods => _addedModsText.Length > 0;

        /// <summary>The mod ids that were in the save but are not active now.</summary>
        public string RemovedModsText => _removedModsText;

        /// <summary>True when there are removed mods to list.</summary>
        public bool HasRemovedMods => _removedModsText.Length > 0;

        /// <summary>The mod ids the save uses that are not installed anymore.</summary>
        public string MissingModsText => _missingModsText;

        /// <summary>True when there are missing mods to list.</summary>
        public bool HasMissingMods => _missingModsText.Length > 0;

        #endregion

        #region Fields

        /// <summary>The scenario the colony was started with.</summary>
        public string ScenarioText => _scenarioText;

        /// <summary>The storyteller of the colony.</summary>
        public string StorytellerText => _storytellerText;

        /// <summary>The difficulty of the colony.</summary>
        public string DifficultyText => _difficultyText;

        /// <summary>The in-game date of the save.</summary>
        public string InGameDateText => _inGameDateText;

        /// <summary>The tile the colony started on.</summary>
        public string StartingTileText => _startingTileText;

        /// <summary>The game version the save was written by.</summary>
        public string GameVersionText => _gameVersionText;

        /// <summary>The total wealth of the colony.</summary>
        public string WealthTotalText => _wealthTotalText;

        /// <summary>The wealth held in items.</summary>
        public string WealthItemsText => _wealthItemsText;

        /// <summary>The wealth held in buildings.</summary>
        public string WealthBuildingsText => _wealthBuildingsText;

        /// <summary>The number of free colonists.</summary>
        public string FreeColonistsText => _freeColonistsText;

        /// <summary>The number of prisoners.</summary>
        public string PrisonersText => _prisonersText;

        /// <summary>The number of completed research projects.</summary>
        public string CompletedResearchText => _completedResearchText;

        /// <summary>The official DLCs the save was played with, comma separated.</summary>
        public string ActiveDlcText => _activeDlcText;

        #endregion

        /// <summary>
        /// Prepares the panel for a save that is about to be read: the header is
        /// shown, the body waits for the read to finish.
        /// </summary>
        internal void BeginLoading(string saveName, string filePath)
        {
            _hasSelection = true;
            _isLoading = true;
            _hasError = false;
            _errorMessage = string.Empty;
            _errorReason = string.Empty;
            _saveName = saveName;
            _filePath = filePath;

            ResetFields();

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(ErrorReason));
            OnPropertyChanged(nameof(SaveName));
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(ShowHint));
            OnPropertyChanged(nameof(ShowSections));
            OnPropertyChanged(nameof(CompatText));
            OnPropertyChanged(nameof(CompatBrushKey));
            OnPropertyChanged(nameof(CompatNote));
            OnPropertyChanged(nameof(HasCompatNote));
            OnPropertyChanged(nameof(ModsInSaveText));
            OnPropertyChanged(nameof(DiffSummaryText));
            OnPropertyChanged(nameof(HasDiff));
            OnPropertyChanged(nameof(AddedModsText));
            OnPropertyChanged(nameof(HasAddedMods));
            OnPropertyChanged(nameof(RemovedModsText));
            OnPropertyChanged(nameof(HasRemovedMods));
            OnPropertyChanged(nameof(MissingModsText));
            OnPropertyChanged(nameof(HasMissingMods));
            RaiseFieldChanged();
        }

        /// <summary>Shows the honest error state instead of the sections.</summary>
        internal void ApplyError(string message, string reason)
        {
            _isLoading = false;
            _hasError = true;
            _errorMessage = message;
            _errorReason = reason;

            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(ErrorReason));
            OnPropertyChanged(nameof(ShowHint));
            OnPropertyChanged(nameof(ShowSections));
        }

        /// <summary>Fills the compatibility block of the details panel.</summary>
        internal void ApplyCompat(
            SaveCompat compat,
            int modsInSave,
            IReadOnlyList<string> added,
            IReadOnlyList<string> removed,
            IReadOnlyList<string> missing)
        {
            _compatText = compat.ToString();
            _compatBrushKey = compat switch
            {
                SaveCompat.Compatible => "BrushStateSuccess",
                SaveCompat.Changed => "BrushStateWarning",
                SaveCompat.Missing => "BrushStateWarning",
                SaveCompat.Unreadable => "BrushStateDanger",
                _ => "BrushStateNeutral"
            };

            _compatNote = compat switch
            {
                SaveCompat.Unknown => "No mods recorded in this save — compatibility can't be judged.",
                SaveCompat.Unreadable => "Could not read this save — the file is corrupt or in an unsupported format.",
                _ => string.Empty
            };

            _modsInSaveText = $"Mods in save: {modsInSave}";

            bool showDiff = compat is SaveCompat.Changed or SaveCompat.Missing;
            _diffSummaryText = showDiff ? $"{added.Count} added, {removed.Count} removed" : string.Empty;

            _addedModsText = showDiff && added.Count > 0 ? $"Added: {string.Join(", ", added)}" : string.Empty;
            _removedModsText = showDiff && removed.Count > 0 ? $"Removed: {string.Join(", ", removed)}" : string.Empty;
            _missingModsText = compat == SaveCompat.Missing && missing.Count > 0
                ? $"Missing: {string.Join(", ", missing)}"
                : string.Empty;

            OnPropertyChanged(nameof(CompatText));
            OnPropertyChanged(nameof(CompatBrushKey));
            OnPropertyChanged(nameof(CompatNote));
            OnPropertyChanged(nameof(HasCompatNote));
            OnPropertyChanged(nameof(ModsInSaveText));
            OnPropertyChanged(nameof(DiffSummaryText));
            OnPropertyChanged(nameof(HasDiff));
            OnPropertyChanged(nameof(AddedModsText));
            OnPropertyChanged(nameof(HasAddedMods));
            OnPropertyChanged(nameof(RemovedModsText));
            OnPropertyChanged(nameof(HasRemovedMods));
            OnPropertyChanged(nameof(MissingModsText));
            OnPropertyChanged(nameof(HasMissingMods));
        }

        /// <summary>Ends the read - the sections are shown from now on.</summary>
        internal void EndLoading()
        {
            if (!_isLoading) return;

            _isLoading = false;
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(ShowHint));
            OnPropertyChanged(nameof(ShowSections));
        }

        /// <summary>Fills the story, playground and number fields from the save head.</summary>
        internal void ApplyDetails(SaveDetails? details, string gameVersion)
        {
            _gameVersionText = string.IsNullOrWhiteSpace(gameVersion) ? UnknownValue : gameVersion;

            if (details == null)
            {
                RaiseFieldChanged();
                return;
            }

            _scenarioText = OrUnknown(details.ScenarioName);
            _storytellerText = OrUnknown(details.StorytellerDef);
            _difficultyText = OrUnknown(details.DifficultyDef);

            _inGameDateText = OrUnknown(ISaveHeaderService.FormatInGameDate(
                details.TicksGame,
                details.GameStartAbsTick,
                details.StartingYear));

            _startingTileText = OrUnknown(details.StartingTile);

            _wealthTotalText = FormatNumber(details.WealthTotal);
            _wealthItemsText = FormatNumber(details.WealthItems);
            _wealthBuildingsText = FormatNumber(details.WealthBuildings);

            _freeColonistsText = details.FreeColonists.HasValue
                ? details.FreeColonists.Value.ToString(CultureInfo.CurrentCulture)
                : UnknownValue;

            _prisonersText = details.Prisoners.HasValue
                ? details.Prisoners.Value.ToString(CultureInfo.CurrentCulture)
                : UnknownValue;

            _completedResearchText = details.ResearchDefsCompleted.HasValue
                ? details.ResearchDefsCompleted.Value.ToString(CultureInfo.CurrentCulture)
                : UnknownValue;

            _activeDlcText = details.ActiveDlcNames is { Count: > 0 }
                ? string.Join(", ", details.ActiveDlcNames)
                : UnknownValue;

            RaiseFieldChanged();
        }

        /// <summary>Clears the panel - nothing is selected any more.</summary>
        internal void Clear()
        {
            _hasSelection = false;
            _isLoading = false;
            _hasError = false;
            _errorMessage = string.Empty;
            _errorReason = string.Empty;
            _saveName = string.Empty;
            _filePath = string.Empty;

            ResetFields();

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(ErrorReason));
            OnPropertyChanged(nameof(SaveName));
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(ShowHint));
            OnPropertyChanged(nameof(ShowSections));
            OnPropertyChanged(nameof(CompatText));
            OnPropertyChanged(nameof(CompatBrushKey));
            OnPropertyChanged(nameof(CompatNote));
            OnPropertyChanged(nameof(HasCompatNote));
            OnPropertyChanged(nameof(ModsInSaveText));
            OnPropertyChanged(nameof(DiffSummaryText));
            OnPropertyChanged(nameof(HasDiff));
            OnPropertyChanged(nameof(AddedModsText));
            OnPropertyChanged(nameof(HasAddedMods));
            OnPropertyChanged(nameof(RemovedModsText));
            OnPropertyChanged(nameof(HasRemovedMods));
            OnPropertyChanged(nameof(MissingModsText));
            OnPropertyChanged(nameof(HasMissingMods));
            RaiseFieldChanged();
        }

        private void ResetFields()
        {
            _compatText = string.Empty;
            _compatBrushKey = "BrushStateNeutral";
            _compatNote = string.Empty;
            _modsInSaveText = string.Empty;
            _diffSummaryText = string.Empty;
            _addedModsText = string.Empty;
            _removedModsText = string.Empty;
            _missingModsText = string.Empty;

            _scenarioText = UnknownValue;
            _storytellerText = UnknownValue;
            _difficultyText = UnknownValue;
            _inGameDateText = UnknownValue;
            _startingTileText = UnknownValue;
            _gameVersionText = UnknownValue;
            _wealthTotalText = UnknownValue;
            _wealthItemsText = UnknownValue;
            _wealthBuildingsText = UnknownValue;
            _freeColonistsText = UnknownValue;
            _prisonersText = UnknownValue;
            _completedResearchText = UnknownValue;
            _activeDlcText = UnknownValue;
        }

        private void RaiseFieldChanged()
        {
            OnPropertyChanged(nameof(ScenarioText));
            OnPropertyChanged(nameof(StorytellerText));
            OnPropertyChanged(nameof(DifficultyText));
            OnPropertyChanged(nameof(InGameDateText));
            OnPropertyChanged(nameof(StartingTileText));
            OnPropertyChanged(nameof(GameVersionText));
            OnPropertyChanged(nameof(WealthTotalText));
            OnPropertyChanged(nameof(WealthItemsText));
            OnPropertyChanged(nameof(WealthBuildingsText));
            OnPropertyChanged(nameof(FreeColonistsText));
            OnPropertyChanged(nameof(PrisonersText));
            OnPropertyChanged(nameof(CompletedResearchText));
            OnPropertyChanged(nameof(ActiveDlcText));
        }

        private static string OrUnknown(string? value)
            => string.IsNullOrWhiteSpace(value) ? UnknownValue : value;

        private static string FormatNumber(double? value)
            => value.HasValue ? value.Value.ToString("N0", CultureInfo.CurrentCulture) : UnknownValue;
    }
}
