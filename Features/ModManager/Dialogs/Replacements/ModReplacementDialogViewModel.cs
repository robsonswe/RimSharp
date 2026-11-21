using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;

namespace RimSharp.Features.ModManager.Dialogs.Replacements
{
    public enum ModReplacementDialogResult
    {
        Cancel,
        Download
    }

    public enum ReplacementFilterKind
    {
        All,
        ToDownload,
        AlreadyInstalled
    }

    public class ReplacementSelectionViewModel : ViewModelBase
    {
        public ModItem OriginalMod { get; }
        public ModReplacementInfo ReplacementInfo { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public ReplacementSelectionViewModel(ModItem original, ModReplacementInfo replacement)
        {
            OriginalMod = original ?? throw new ArgumentNullException(nameof(original));
            ReplacementInfo = replacement ?? throw new ArgumentNullException(nameof(replacement));
            IsSelected = true; // Default to selected
        }
    }

public class ModReplacementDialogViewModel : DialogViewModelBase<ModReplacementDialogResult>
    {
        private readonly IModDictionaryService? _modDictionaryService;

        private ObservableCollection<ModReplacementItem>? _replacements = new ObservableCollection<ModReplacementItem>();
        public ObservableCollection<ModReplacementItem>? Replacements
        {
            get => _replacements;
            set
            {

                if (_replacements != null)
                {
                    foreach (var item in _replacements)
                    {
                        if (item != null)
                            item.PropertyChanged -= ReplacementItem_PropertyChanged;
                    }
                }

                if (SetProperty(ref _replacements, value))
                {
                    // Subscribe to new collection
                    if (_replacements != null)
                    {
                        foreach (var item in _replacements)
                        {
                            if (item != null)
                                item.PropertyChanged += ReplacementItem_PropertyChanged;
                        }
                        UpdateSelectedCount(); // Initial count
                    }
                }
            }
        }
        private ObservableCollection<ModReplacementItem> _alreadyInstalledReplacements = new ObservableCollection<ModReplacementItem>();
        public ObservableCollection<ModReplacementItem> AlreadyInstalledReplacements
        {
            get => _alreadyInstalledReplacements;
            set => SetProperty(ref _alreadyInstalledReplacements, value);
        }

        private bool _hasAlreadyInstalledReplacements;
        public bool HasAlreadyInstalledReplacements
        {
            get => _hasAlreadyInstalledReplacements;
            private set => SetProperty(ref _hasAlreadyInstalledReplacements, value);
        }

        private bool _hasRegularReplacements;
        public bool HasRegularReplacements
        {
            get => _hasRegularReplacements;
            private set => SetProperty(ref _hasRegularReplacements, value);
        }

        public bool HasAnyReplacements => HasRegularReplacements || HasAlreadyInstalledReplacements;

        // --- Filter state & computed footer/label properties -------------------

        private ReplacementFilterKind _activeFilter = ReplacementFilterKind.All;
        public ReplacementFilterKind ActiveFilter
        {
            get => _activeFilter;
            set
            {
                if (SetProperty(ref _activeFilter, value))
                {
                    OnPropertyChanged(nameof(IsFilterAll));
                    OnPropertyChanged(nameof(IsFilterToDownload));
                    OnPropertyChanged(nameof(IsFilterAlreadyInstalled));
                    OnPropertyChanged(nameof(ShowRegularSection));
                    OnPropertyChanged(nameof(ShowInstalledSection));
                    OnPropertyChanged(nameof(HasNoVisibleItems));
                }
            }
        }

        public bool IsFilterAll => ActiveFilter == ReplacementFilterKind.All;
        public bool IsFilterToDownload => ActiveFilter == ReplacementFilterKind.ToDownload;
        public bool IsFilterAlreadyInstalled => ActiveFilter == ReplacementFilterKind.AlreadyInstalled;

        public bool ShowRegularSection => ActiveFilter != ReplacementFilterKind.AlreadyInstalled && HasRegularReplacements;
        public bool ShowInstalledSection => ActiveFilter != ReplacementFilterKind.ToDownload && HasAlreadyInstalledReplacements;
        public bool HasNoVisibleItems => !ShowRegularSection && !ShowInstalledSection;

        public int RegularCount => Replacements?.Count ?? 0;
        public int AlreadyInstalledCount => AlreadyInstalledReplacements?.Count ?? 0;
        public int TotalCount => RegularCount + AlreadyInstalledCount;

        public string FilterAllLabel => $"All ({TotalCount})";
        public string FilterToDownloadLabel => $"To Download ({RegularCount})";
        public string FilterInstalledLabel => $"Already Installed ({AlreadyInstalledCount})";

        public bool HasSelection => SelectedCount > 0;
        public string SelectedSummaryText => SelectedCount == 1
            ? "1 replacement selected"
            : $"{SelectedCount} replacements selected";
        public string DownloadButtonText => $"Download Selected ({SelectedCount})";

        private ICommand? _setFilterCommand;
        public ICommand SetFilterCommand => _setFilterCommand ??= CreateCommand<string>(param =>
        {
            if (Enum.TryParse<ReplacementFilterKind>(param, out var filter))
                ActiveFilter = filter;
        });

        private int _selectedCount;
        public int SelectedCount
        {
            get => _selectedCount;
            private set => SetProperty(ref _selectedCount, value); // Make setter private
        }

        private ICommand? _selectAllCommand;
        public ICommand SelectAllCommand => _selectAllCommand ??= CreateCommand(SelectAll);

        private ICommand? _selectNoneCommand;
        public ICommand SelectNoneCommand => _selectNoneCommand ??= CreateCommand(SelectNone);

        public ModReplacementDialogViewModel(
            IEnumerable<(ModItem Original, ModReplacementInfo Replacement, long OriginalUpdate, long ReplacementUpdate, string ReplacementSteamTitle, string ReplacementSteamAuthor)> replacements,
            IEnumerable<ModItem> allInstalledMods,
            IModDictionaryService? modDictionaryService = null,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? fetchedVersionsBySteamId = null)
            : base("Mod Replacements Available")
        {
            _modDictionaryService = modDictionaryService;

            var installedSteamModsBySteamId = allInstalledMods
                .Where(m => !string.IsNullOrEmpty(m.SteamId))
                .GroupBy(m => m.SteamId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var installedLocalModsByPackageId = allInstalledMods
                .Where(m => string.IsNullOrEmpty(m.SteamId) && !string.IsNullOrEmpty(m.PackageId))
                .GroupBy(m => m.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var itemGroups = replacements
                .Select(r =>
                {
                    var replacementInfo = r.Replacement;
                    ModItem? installedReplacement = null;

                    if (!string.IsNullOrEmpty(replacementInfo.ReplacementSteamId))
                    {
                        installedSteamModsBySteamId.TryGetValue(replacementInfo.ReplacementSteamId, out installedReplacement);
                    }
                    else if (!string.IsNullOrEmpty(replacementInfo.ReplacementModId))
                    {
                        installedLocalModsByPackageId.TryGetValue(replacementInfo.ReplacementModId, out installedReplacement);
                    }

                    bool alreadyInstalled = installedReplacement != null;

                    // Resolve a missing PackageId at display time only. Never mutate
                    // replacementInfo.ReplacementModId: it lives in the shared
                    // ModReplacementService cache and is reused across rows/calls.
                    // The same dictionary entry also supplies the ModInfo version
                    // tier, so the dictionary is consulted at most once per row and
                    // the existing "not consulted" guarantees remain intact.
                    string? resolvedPackageId = null;
                    ModDictionaryEntry? modInfoEntry = null;
                    if (string.IsNullOrWhiteSpace(replacementInfo.ReplacementModId))
                    {
                        // Source 1: installed mod matched by SteamId (wins).
                        if (installedReplacement != null && !string.IsNullOrWhiteSpace(installedReplacement.PackageId))
                            resolvedPackageId = installedReplacement.PackageId;
                        // Source 2: ModInfo dictionary database by SteamId.
                        else if (!string.IsNullOrWhiteSpace(replacementInfo.ReplacementSteamId))
                        {
                            modInfoEntry = _modDictionaryService?.GetEntryBySteamId(replacementInfo.ReplacementSteamId.Trim());
                            resolvedPackageId = modInfoEntry?.PackageId;
                        }
                    }

                    // Steam-fetched versions for the replacement (authoritative).
                    // The analysis already fetched these from Steam; look them up by
                    // the trimmed SteamId so no extra network call is made here.
                    IReadOnlyList<string>? fetchedReplacementVersions = null;
                    if (fetchedVersionsBySteamId != null && !string.IsNullOrWhiteSpace(replacementInfo.ReplacementSteamId))
                    {
                        fetchedVersionsBySteamId.TryGetValue(replacementInfo.ReplacementSteamId.Trim(), out fetchedReplacementVersions);
                    }

                    return new ModReplacementItem(r.Original, r.Replacement)
                    {
                        OriginalLastUpdate = r.OriginalUpdate,
                        ReplacementLastUpdate = r.ReplacementUpdate,
                        IsSelected = !alreadyInstalled,
                        ReplacementAlreadyInstalled = alreadyInstalled,
                        InstalledReplacement = installedReplacement,
                        SteamTitle = r.ReplacementSteamTitle,
                        SteamAuthor = r.ReplacementSteamAuthor,
                        ResolvedReplacementPackageId = resolvedPackageId,
                        FetchedReplacementVersions = fetchedReplacementVersions,
                        ModInfoReplacementVersions = modInfoEntry?.Versions
                    };
                })
                .ToList();

            var regularItems = itemGroups
                .Where(item => !item.ReplacementAlreadyInstalled)
                .ToList();

            var alreadyInstalledItems = itemGroups
                .Where(item => item.ReplacementAlreadyInstalled)
                .ToList();

            Debug.WriteLine($"[ModReplacementVM] Found {regularItems.Count} regular and {alreadyInstalledItems.Count} already installed replacements.");

            foreach (var item in regularItems)
            {
                item.PropertyChanged += ReplacementItem_PropertyChanged;
            }
            Replacements = new ObservableCollection<ModReplacementItem>(regularItems);
            AlreadyInstalledReplacements = new ObservableCollection<ModReplacementItem>(alreadyInstalledItems);

            HasRegularReplacements = regularItems.Count > 0;
            HasAlreadyInstalledReplacements = alreadyInstalledItems.Count > 0;
        }

        private void ReplacementItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ModReplacementItem.IsSelected))
            {
                UpdateSelectedCount();
            }
        }

        private void SelectAll()
        {
            if (Replacements == null) return;
            foreach (var item in Replacements)
            {
                item.IsSelected = true;
            }
        }

        private void SelectNone()
        {
            if (Replacements == null) return;
            foreach (var item in Replacements)
            {
                item.IsSelected = false;
            }
        }

        private void UpdateSelectedCount()
        {
            SelectedCount = Replacements?.Count(r => r.IsSelected) ?? 0;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedSummaryText));
            OnPropertyChanged(nameof(DownloadButtonText));
        }

        public List<ReplacementSelectionViewModel> GetSelectedReplacements()
        {
            return Replacements?
                .Where(r => r.IsSelected)
                .Select(r => new ReplacementSelectionViewModel(r.OriginalMod, r.ReplacementInfo))
                .ToList() ?? new List<ReplacementSelectionViewModel>();
        }
    }
}


