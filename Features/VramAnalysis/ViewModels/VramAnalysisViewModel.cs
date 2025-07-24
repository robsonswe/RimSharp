// Features/VramAnalysis/ViewModels/VramAnalysisViewModel.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Core.Commands.Base;
using RimSharp.Core.Services;
using RimSharp.Features.VramAnalysis.Tools;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.VramAnalysis.ViewModels
{
    public class VramAnalysisViewModel : ViewModelBase
    {
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService;
        private readonly ILoggerService _logger;
        private readonly IPathService _pathService;
        private readonly ISystemInfoService _systemInfoService;
        public long UserVram { get; private set; }
        public bool IsUserVramAvailable => UserVram > 0;
        private List<VramModItemWrapper> _allVramMods = new();
        public ObservableCollection<VramModItemWrapper> VramMods { get => _vramMods; set => SetProperty(ref _vramMods, value); }
        private ObservableCollection<VramModItemWrapper> _vramMods = new();
        public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
        private bool _isBusy;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
        private string _statusMessage = "Ready. Click 'Calculate VRAM' to begin analysis.";
        public long TotalVramCompressedActiveMods { get => _totalVramCompressedActiveMods; set => SetProperty(ref _totalVramCompressedActiveMods, value); }
        private long _totalVramCompressedActiveMods;
        public long TotalVramCompressedFavoriteMods { get => _totalVramCompressedFavoriteMods; set => SetProperty(ref _totalVramCompressedFavoriteMods, value); }
        private long _totalVramCompressedFavoriteMods;
        public long MaxTotalVramCompressedActiveMods { get => _maxTotalVramCompressedActiveMods; set => SetProperty(ref _maxTotalVramCompressedActiveMods, value); }
        private long _maxTotalVramCompressedActiveMods;
        public long MaxTotalVramCompressedFavoriteMods { get => _maxTotalVramCompressedFavoriteMods; set => SetProperty(ref _maxTotalVramCompressedFavoriteMods, value); }
        private long _maxTotalVramCompressedFavoriteMods;
        public int TotalTextureCountActiveMods { get => _totalTextureCountActiveMods; set => SetProperty(ref _totalTextureCountActiveMods, value); }
        private int _totalTextureCountActiveMods;
        public int MaxTotalTextureCountActiveMods { get => _maxTotalTextureCountActiveMods; set => SetProperty(ref _maxTotalTextureCountActiveMods, value); }
        private int _maxTotalTextureCountActiveMods;
        public int TotalTextureCountFavoriteMods { get => _totalTextureCountFavoriteMods; set => SetProperty(ref _totalTextureCountFavoriteMods, value); }
        private int _totalTextureCountFavoriteMods;
        public int MaxTotalTextureCountFavoriteMods { get => _maxTotalTextureCountFavoriteMods; set => SetProperty(ref _maxTotalTextureCountFavoriteMods, value); }
        private int _maxTotalTextureCountFavoriteMods;

        // --- NEW: Atlas Count Totals ---
        public int TotalInAtlasCountActiveMods { get => _totalInAtlasCountActiveMods; set => SetProperty(ref _totalInAtlasCountActiveMods, value); }
        private int _totalInAtlasCountActiveMods;
        public int MaxTotalInAtlasCountActiveMods { get => _maxTotalInAtlasCountActiveMods; set => SetProperty(ref _maxTotalInAtlasCountActiveMods, value); }
        private int _maxTotalInAtlasCountActiveMods;

        public bool ShowOnlyActive { get => _showOnlyActive; set { if (SetProperty(ref _showOnlyActive, value)) ApplyFiltersAndSort(); } }
        private bool _showOnlyActive;
        public bool ShowOnlyFavorites { get => _showOnlyFavorites; set { if (SetProperty(ref _showOnlyFavorites, value)) ApplyFiltersAndSort(); } }
        private bool _showOnlyFavorites;
        public bool ShowMaxVram { get => _showMaxVram; set => SetProperty(ref _showMaxVram, value); }
        private bool _showMaxVram;
        public string? CurrentSortColumn { get => _currentSortColumn; private set => SetProperty(ref _currentSortColumn, value); }
        private string? _currentSortColumn = "Mod.Name";
        public ListSortDirection SortDirection { get => _sortDirection; private set => SetProperty(ref _sortDirection, value); }
        private ListSortDirection _sortDirection = ListSortDirection.Ascending;
        public AsyncRelayCommand CalculateVramCommand { get; }
        public DelegateCommand<string> SortCommand { get; }

        public VramAnalysisViewModel(IModListManager modListManager, IDialogService dialogService, ILoggerService logger, IPathService pathService, ISystemInfoService systemInfoService)
        {
            _modListManager = modListManager; _dialogService = dialogService; _logger = logger; _pathService = pathService; _systemInfoService = systemInfoService;
            CalculateVramCommand = CreateCancellableAsyncCommand(ExecuteVramCalculationAsync, () => !IsBusy, nameof(IsBusy));
            SortCommand = CreateCommand<string>(ExecuteSortCommand);
            _modListManager.ListChanged += OnModListChanged; LoadSystemInfo(); LoadMods();
        }

        private void LoadSystemInfo() { Task.Run(() => { UserVram = _systemInfoService.GetPrimaryGpuVram(); OnPropertyChanged(nameof(UserVram)); OnPropertyChanged(nameof(IsUserVramAvailable)); }); }
        private void LoadMods() { _allVramMods = _modListManager.GetAllMods().Where(m => m.ModType != ModType.Core && m.ModType != ModType.Expansion && m.Textures).Select(m => new VramModItemWrapper(m)).ToList(); ShowOnlyActive = false; ShowOnlyFavorites = false; ShowMaxVram = false; ApplyFiltersAndSort(); UpdateTotalVramEstimates(); }
        private void OnModListChanged(object? sender, EventArgs e) => RunOnUIThread(LoadMods);
        private async Task ExecuteVramCalculationAsync(CancellationToken ct) { IsBusy = true; StatusMessage = "Starting VRAM calculation..."; ProgressDialogViewModel? progressVm = null; using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct); try { var modsToCalculate = _allVramMods.ToList(); if (!modsToCalculate.Any()) { StatusMessage = "No mods with textures found to analyze."; return; } RunOnUIThread(() => { progressVm = _dialogService.ShowProgressDialog("Calculating VRAM", "Preparing...", true, false, linkedCts); }); await Task.Delay(50, ct); var activeModIds = _modListManager.VirtualActiveMods.Select(m => m.Mod.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase); var majorVersion = _pathService.GetMajorGameVersion(); int processedCount = 0; await Task.Run(async () => { foreach (var modWrapper in modsToCalculate) { ct.ThrowIfCancellationRequested(); processedCount++; RunOnUIThread(() => { progressVm?.UpdateProgress((int)((double)processedCount / modsToCalculate.Count * 100), $"Analyzing: {modWrapper.Mod.Name}"); }); var result = VramEstimator.Calculate(modWrapper.Mod.Path, majorVersion, activeModIds); RunOnUIThread(() => { modWrapper.EstimatedVramUncompressed = result.EstimatedVramUncompressed; modWrapper.EstimatedVramCompressed = result.EstimatedVramCompressed; modWrapper.MaxEstimatedVramUncompressed = result.MaxEstimatedVramUncompressed; modWrapper.MaxEstimatedVramCompressed = result.MaxEstimatedVramCompressed; modWrapper.ConditionalDependencies = result.ConditionalDependencies; modWrapper.TextureCount = result.TextureCount; modWrapper.MaxTextureCount = result.MaxTextureCount; modWrapper.InAtlasCount = result.InAtlasCount; modWrapper.MaxInAtlasCount = result.MaxInAtlasCount; }); await Task.Delay(10, ct); } }, ct); StatusMessage = "VRAM calculation complete."; progressVm?.CompleteOperation("Calculation complete."); } catch (OperationCanceledException) { StatusMessage = "VRAM calculation cancelled."; progressVm?.ForceClose(); } catch (Exception ex) { StatusMessage = "An error occurred."; _logger.LogException(ex, StatusMessage, nameof(VramAnalysisViewModel)); progressVm?.ForceClose(); _dialogService.ShowError("Calculation Error", $"An error occurred: {ex.Message}"); } finally { IsBusy = false; RunOnUIThread(UpdateTotalVramEstimates); progressVm?.Dispose(); } }
        private void ExecuteSortCommand(string? propertyPath) { if (string.IsNullOrEmpty(propertyPath)) return; SortDirection = (CurrentSortColumn == propertyPath && SortDirection == ListSortDirection.Ascending) ? ListSortDirection.Descending : ListSortDirection.Ascending; CurrentSortColumn = propertyPath; ApplyFiltersAndSort(); }

        private void UpdateTotalVramEstimates() {
            long activeTotal = 0, favoriteTotal = 0, maxActiveTotal = 0, maxFavoriteTotal = 0;
            int activeTextureTotal = 0, maxActiveTextureTotal = 0, favoriteTextureTotal = 0, maxFavoriteTextureTotal = 0;
            int activeInAtlasTotal = 0, maxActiveInAtlasTotal = 0; // --- NEW ---
            foreach (var mod in _allVramMods) {
                if (mod.Mod.IsActive) {
                    activeTotal += mod.EstimatedVramCompressed; maxActiveTotal += mod.MaxEstimatedVramCompressed;
                    activeTextureTotal += mod.TextureCount; maxActiveTextureTotal += mod.MaxTextureCount;
                    activeInAtlasTotal += mod.InAtlasCount; maxActiveInAtlasTotal += mod.MaxInAtlasCount; // --- NEW ---
                }
                if (mod.Mod.IsFavorite) { favoriteTotal += mod.EstimatedVramCompressed; maxFavoriteTotal += mod.MaxEstimatedVramCompressed; favoriteTextureTotal += mod.TextureCount; maxFavoriteTextureTotal += mod.MaxTextureCount; }
            }
            TotalVramCompressedActiveMods = activeTotal; TotalVramCompressedFavoriteMods = favoriteTotal;
            MaxTotalVramCompressedActiveMods = maxActiveTotal; MaxTotalVramCompressedFavoriteMods = maxFavoriteTotal;
            TotalTextureCountActiveMods = activeTextureTotal; MaxTotalTextureCountActiveMods = maxActiveTextureTotal;
            TotalTextureCountFavoriteMods = favoriteTextureTotal; MaxTotalTextureCountFavoriteMods = maxFavoriteTextureTotal;
            TotalInAtlasCountActiveMods = activeInAtlasTotal; MaxTotalInAtlasCountActiveMods = maxActiveInAtlasTotal; // --- NEW ---
        }

        private void ApplyFiltersAndSort() {
            IEnumerable<VramModItemWrapper> view = _allVramMods;
            if (ShowOnlyActive) view = view.Where(m => m.Mod.IsActive);
            if (ShowOnlyFavorites) view = view.Where(m => m.Mod.IsFavorite);
            var sortProperty = CurrentSortColumn ?? "Mod.Name";
            if (ShowMaxVram) {
                if (sortProperty.Contains("EstimatedVram")) sortProperty = sortProperty.Replace("EstimatedVram", "MaxEstimatedVram");
                if (sortProperty.Contains("TextureCount")) sortProperty = sortProperty.Replace("TextureCount", "MaxTextureCount");
                if (sortProperty.Contains("InAtlasCount")) sortProperty = sortProperty.Replace("InAtlasCount", "MaxInAtlasCount"); // --- NEW ---
            }
            var sortedView = SortDirection == ListSortDirection.Ascending ? view.OrderBy(mod => GetPropertyValue(mod, sortProperty)) : view.OrderByDescending(mod => GetPropertyValue(mod, sortProperty));
            RunOnUIThread(() => { VramMods = new ObservableCollection<VramModItemWrapper>(sortedView); });
        }

        private object? GetPropertyValue(VramModItemWrapper modWrapper, string propertyPath) { object? currentObject = modWrapper; foreach (var part in propertyPath.Split('.')) { if (currentObject == null) return null; var prop = currentObject.GetType().GetProperty(part); if (prop == null) return null; currentObject = prop.GetValue(currentObject); } return currentObject; }
        protected override void Dispose(bool disposing) { if (!_disposed && disposing) _modListManager.ListChanged -= OnModListChanged; base.Dispose(disposing); }
    }
}