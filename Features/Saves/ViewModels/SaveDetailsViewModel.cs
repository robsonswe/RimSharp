#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RimSharp.AppDir.AppFiles;
using RimSharp.Services.ModManagement;

namespace RimSharp.Features.Saves.ViewModels
{
    /// <summary>
    /// The details panel of the saves dialog: the compatibility verdict, the
    /// story and the colonists of the selected save. Every field
    /// the save does not carry shows an em dash - a value is never invented.
    /// </summary>
    public sealed class SaveDetailsViewModel : ViewModelBase
    {
        /// <summary>Shown for every field the save does not carry.</summary>
        public const string UnknownValue = "—";

        /// <summary>Shown when the scan found no human pawn at all.</summary>
        public const string NoColonistsText = "No human pawns found.";

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
        private string _scenarioSummaryText = UnknownValue;
        private string _scenarioDescriptionText = UnknownValue;
        private string _storytellerText = UnknownValue;
        private string _difficultyText = UnknownValue;
        private string _factionTypeText = UnknownValue;
        private string _worldNameText = UnknownValue;
        private string _worldSeedText = UnknownValue;
        private string _planetModeText = UnknownValue;
        private string _inGameDateText = UnknownValue;
        private string _gameVersionText = UnknownValue;
        private string _activeDlcText = UnknownValue;

        private bool _colonistNamesLoaded;
        private bool _isLoadingColonistNames;
        private string _colonistNamesNote = string.Empty;

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

        /// <summary>The short summary of the scenario.</summary>
        public string ScenarioSummaryText => _scenarioSummaryText;

        /// <summary>The description of the scenario.</summary>
        public string ScenarioDescriptionText => _scenarioDescriptionText;

        /// <summary>The storyteller of the colony.</summary>
        public string StorytellerText => _storytellerText;

        /// <summary>The difficulty of the colony.</summary>
        public string DifficultyText => _difficultyText;

        /// <summary>The kind of the player faction ("Colony", "Tribe").</summary>
        public string FactionTypeText => _factionTypeText;

        /// <summary>The name of the world the colony lives on.</summary>
        public string WorldNameText => _worldNameText;

        /// <summary>The seed of the world.</summary>
        public string WorldSeedText => _worldSeedText;

        /// <summary>Whether the colony plays on a planet.</summary>
        public string PlanetModeText => _planetModeText;

        /// <summary>The in-game date of the save.</summary>
        public string InGameDateText => _inGameDateText;

        /// <summary>The game version the save was written by.</summary>
        public string GameVersionText => _gameVersionText;

        /// <summary>The official DLCs the save was played with, comma separated.</summary>
        public string ActiveDlcText => _activeDlcText;

        #endregion

        #region Colonist names

        /// <summary>
        /// The colonists of the selected save - empty until the scan of the
        /// save has finished. The names live deep inside the save, so they are
        /// read in the background while the save is selected.
        /// </summary>
        public ObservableCollection<SaveColonistItemViewModel> Colonists { get; } = new();

        /// <summary>True when the scan found at least one human pawn.</summary>
        public bool HasColonistNames => Colonists.Count > 0;

        /// <summary>True when the scan has finished - also when it found nothing.</summary>
        public bool ColonistNamesLoaded => _colonistNamesLoaded;

        /// <summary>True while the scan is running.</summary>
        public bool IsLoadingColonistNames => _isLoadingColonistNames;

        /// <summary>The note under the names - empty while nothing was scanned.</summary>
        public string ColonistNamesNote => _colonistNamesNote;

        /// <summary>True when there is a note to show.</summary>
        public bool HasColonistNamesNote => _colonistNamesNote.Length > 0;

        /// <summary>
        /// The placeholder of a save whose names were not read - the honest
        /// em dash, like every other field the save does not carry.
        /// </summary>
        public string ColonistNamesUnknownText => UnknownValue;

        /// <summary>
        /// True when the names are not known - the save could not be read, so
        /// no scan was started.
        /// </summary>
        public bool ShowColonistNamesUnknown => !_colonistNamesLoaded && !_isLoadingColonistNames;

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
            _scenarioSummaryText = OrUnknown(details.ScenarioSummary);
            _scenarioDescriptionText = OrUnknown(details.ScenarioDescription);
            _storytellerText = OrUnknown(details.StorytellerDef);
            _difficultyText = OrUnknown(details.DifficultyDef);
            _factionTypeText = FormatFactionType(details.PlayerFactionType);
            _worldNameText = OrUnknown(details.WorldName);
            _worldSeedText = OrUnknown(details.WorldSeed);
            _planetModeText = details.IsPlanetMode.HasValue
                ? (details.IsPlanetMode.Value ? "Yes" : "No")
                : UnknownValue;

            _inGameDateText = OrUnknown(ISaveHeaderService.FormatInGameDate(
                details.TicksGame,
                details.GameStartAbsTick,
                details.StartingYear,
                details.StartingTile,
                details.ReportedSeason));

            _activeDlcText = details.ActiveDlcNames is { Count: > 0 }
                ? string.Join(", ", details.ActiveDlcNames)
                : UnknownValue;

            RaiseFieldChanged();
        }

        /// <summary>
        /// Prepares the panel for the colonist name scan - the section shows
        /// that it is reading while the scan runs.
        /// </summary>
        internal void BeginColonistNamesLoad()
        {
            _isLoadingColonistNames = true;
            _colonistNamesNote = string.Empty;

            OnPropertyChanged(nameof(IsLoadingColonistNames));
            OnPropertyChanged(nameof(ShowColonistNamesUnknown));
            OnPropertyChanged(nameof(HasColonistNamesNote));
            OnPropertyChanged(nameof(ColonistNamesNote));
        }

        /// <summary>
        /// Fills the colonists the scan found. A scan that found nothing says
        /// so - it never shows a made-up name.
        /// </summary>
        internal void ApplyColonistNames(IReadOnlyList<SaveColonist> colonists)
        {
            _isLoadingColonistNames = false;
            _colonistNamesLoaded = true;

            Colonists.Clear();
            foreach (SaveColonist colonist in colonists)
            {
                Colonists.Add(new SaveColonistItemViewModel(colonist));
            }

            _colonistNamesNote = Colonists.Count == 0 ? NoColonistsText : string.Empty;

            OnPropertyChanged(nameof(IsLoadingColonistNames));
            OnPropertyChanged(nameof(ColonistNamesLoaded));
            OnPropertyChanged(nameof(HasColonistNames));
            OnPropertyChanged(nameof(ShowColonistNamesUnknown));
            OnPropertyChanged(nameof(HasColonistNamesNote));
            OnPropertyChanged(nameof(ColonistNamesNote));
        }

        /// <summary>
        /// Drops the colonists - a new save is selected, so the colonists of
        /// the old one must not stay on screen.
        /// </summary>
        internal void ResetColonistNames()
        {
            _isLoadingColonistNames = false;
            _colonistNamesLoaded = false;
            _colonistNamesNote = string.Empty;

            Colonists.Clear();

            OnPropertyChanged(nameof(IsLoadingColonistNames));
            OnPropertyChanged(nameof(ColonistNamesLoaded));
            OnPropertyChanged(nameof(HasColonistNames));
            OnPropertyChanged(nameof(ShowColonistNamesUnknown));
            OnPropertyChanged(nameof(HasColonistNamesNote));
            OnPropertyChanged(nameof(ColonistNamesNote));
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
            _scenarioSummaryText = UnknownValue;
            _scenarioDescriptionText = UnknownValue;
            _storytellerText = UnknownValue;
            _difficultyText = UnknownValue;
            _factionTypeText = UnknownValue;
            _worldNameText = UnknownValue;
            _worldSeedText = UnknownValue;
            _planetModeText = UnknownValue;
            _inGameDateText = UnknownValue;
            _gameVersionText = UnknownValue;
            _activeDlcText = UnknownValue;

            ResetColonistNames();
        }

        private void RaiseFieldChanged()
        {
            OnPropertyChanged(nameof(ScenarioText));
            OnPropertyChanged(nameof(ScenarioSummaryText));
            OnPropertyChanged(nameof(ScenarioDescriptionText));
            OnPropertyChanged(nameof(StorytellerText));
            OnPropertyChanged(nameof(DifficultyText));
            OnPropertyChanged(nameof(FactionTypeText));
            OnPropertyChanged(nameof(WorldNameText));
            OnPropertyChanged(nameof(WorldSeedText));
            OnPropertyChanged(nameof(PlanetModeText));
            OnPropertyChanged(nameof(InGameDateText));
            OnPropertyChanged(nameof(GameVersionText));
            OnPropertyChanged(nameof(ActiveDlcText));
        }

        private static string OrUnknown(string? value)
            => string.IsNullOrWhiteSpace(value) ? UnknownValue : value;

        /// <summary>
        /// Formats the faction of the player. The save carries the def of the
        /// faction; the label the player knows it by is used.
        /// </summary>
        private static string FormatFactionType(string? factionDef)
        {
            if (string.IsNullOrWhiteSpace(factionDef))
            {
                return UnknownValue;
            }

            return factionDef.Trim() switch
            {
                "PlayerColony" => "Colony",
                "PlayerTribe" => "Tribe",
                _ => factionDef.Trim()
            };
        }
    }
}
