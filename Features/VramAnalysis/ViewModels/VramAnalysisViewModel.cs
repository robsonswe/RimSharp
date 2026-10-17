// Features/VramAnalysis/ViewModels/VramAnalysisViewModel.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
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
    public class VramAnalysisViewModel : ViewModelBase, IVramAnalysisViewModel
    {
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService;
        private readonly ILoggerService _logger;
        private readonly IPathService _pathService;
        private readonly ISystemInfoService _systemInfoService;
        public long UserVram { get; private set; }
        public bool IsUserVramAvailable => UserVram > 0;
        private List<VramModItemWrapper> _allVramMods = new();
        private List<ModItem> _gameBaselineMods = new();
        private Task? _baselineTask;
        private int _baselineRunning;
        public ObservableCollection<VramModItemWrapper> VramMods { get => _vramMods; set => SetProperty(ref _vramMods, value); }
        private ObservableCollection<VramModItemWrapper> _vramMods = new();
        public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
        private bool _isBusy;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
        private string _statusMessage = "Ready. Click 'Calculate VRAM' to begin analysis.";
        public long? TotalVramCompressedActiveMods { get => _totalVramCompressedActiveMods; set => SetProperty(ref _totalVramCompressedActiveMods, value); }
        private long? _totalVramCompressedActiveMods;
        public long? TotalVramCompressedFavoriteMods { get => _totalVramCompressedFavoriteMods; set => SetProperty(ref _totalVramCompressedFavoriteMods, value); }
        private long? _totalVramCompressedFavoriteMods;
        public long? MaxTotalVramCompressedActiveMods { get => _maxTotalVramCompressedActiveMods; set => SetProperty(ref _maxTotalVramCompressedActiveMods, value); }
        private long? _maxTotalVramCompressedActiveMods;
        public long? MaxTotalVramCompressedFavoriteMods { get => _maxTotalVramCompressedFavoriteMods; set => SetProperty(ref _maxTotalVramCompressedFavoriteMods, value); }
        private long? _maxTotalVramCompressedFavoriteMods;
        public int? TotalTextureCountActiveMods { get => _totalTextureCountActiveMods; set => SetProperty(ref _totalTextureCountActiveMods, value); }
        private int? _totalTextureCountActiveMods;
        public int? MaxTotalTextureCountActiveMods { get => _maxTotalTextureCountActiveMods; set => SetProperty(ref _maxTotalTextureCountActiveMods, value); }
        private int? _maxTotalTextureCountActiveMods;
        public int? TotalTextureCountFavoriteMods { get => _totalTextureCountFavoriteMods; set => SetProperty(ref _totalTextureCountFavoriteMods, value); }
        private int? _totalTextureCountFavoriteMods;
        public int? MaxTotalTextureCountFavoriteMods { get => _maxTotalTextureCountFavoriteMods; set => SetProperty(ref _maxTotalTextureCountFavoriteMods, value); }
        private int? _maxTotalTextureCountFavoriteMods;

        public int? TotalInAtlasCountActiveMods { get => _totalInAtlasCountActiveMods; set => SetProperty(ref _totalInAtlasCountActiveMods, value); }
        private int? _totalInAtlasCountActiveMods;
        public int? MaxTotalInAtlasCountActiveMods { get => _maxTotalInAtlasCountActiveMods; set => SetProperty(ref _maxTotalInAtlasCountActiveMods, value); }
        private int? _maxTotalInAtlasCountActiveMods;

        public long? CoreVramCompressed { get => _coreVramCompressed; set => SetProperty(ref _coreVramCompressed, value); }
        private long? _coreVramCompressed;
        public long? CoreVramUncompressed { get => _coreVramUncompressed; set => SetProperty(ref _coreVramUncompressed, value); }
        private long? _coreVramUncompressed;
        public int? CoreTextureCount { get => _coreTextureCount; set => SetProperty(ref _coreTextureCount, value); }
        private int? _coreTextureCount;
        public int? CoreInAtlasCount { get => _coreInAtlasCount; set => SetProperty(ref _coreInAtlasCount, value); }
        private int? _coreInAtlasCount;

        public long? ExpansionsVramCompressed { get => _expansionsVramCompressed; set => SetProperty(ref _expansionsVramCompressed, value); }
        private long? _expansionsVramCompressed;
        public long? ExpansionsVramUncompressed { get => _expansionsVramUncompressed; set => SetProperty(ref _expansionsVramUncompressed, value); }
        private long? _expansionsVramUncompressed;
        public int? ExpansionsTextureCount { get => _expansionsTextureCount; set => SetProperty(ref _expansionsTextureCount, value); }
        private int? _expansionsTextureCount;
        public int? ExpansionsInAtlasCount { get => _expansionsInAtlasCount; set => SetProperty(ref _expansionsInAtlasCount, value); }
        private int? _expansionsInAtlasCount;

        public long? TotalVramWithGameCompressed { get => _totalVramWithGameCompressed; set => SetProperty(ref _totalVramWithGameCompressed, value); }
        private long? _totalVramWithGameCompressed;
        public long? MaxTotalVramWithGameCompressed { get => _maxTotalVramWithGameCompressed; set => SetProperty(ref _maxTotalVramWithGameCompressed, value); }
        private long? _maxTotalVramWithGameCompressed;
        public bool IsOverBudget { get => _isOverBudget; set => SetProperty(ref _isOverBudget, value); }
        private bool _isOverBudget;

        // True once the per-mod values have been produced by a completed VRAM calculation.
        private bool _modsCalculated;

        // Read-only values for the summary panel. They follow the current view (typical) or the max view.
        public long? DisplayedTotalVram => ShowMaxVram ? MaxTotalVramWithGameCompressed : TotalVramWithGameCompressed;
        public long? DisplayedModsVram => ShowMaxVram ? MaxTotalVramCompressedActiveMods : TotalVramCompressedActiveMods;
        public long? DisplayedFavoritesVram => ShowMaxVram ? MaxTotalVramCompressedFavoriteMods : TotalVramCompressedFavoriteMods;
        public int? DisplayedTextureCount => ShowMaxVram ? MaxTotalTextureCountActiveMods : TotalTextureCountActiveMods;
        public int? DisplayedInAtlasCount => ShowMaxVram ? MaxTotalInAtlasCountActiveMods : TotalInAtlasCountActiveMods;

        // Budget values for the gauge, expressed in MB so the bar can use them directly.
        public double BudgetCapacityMb => UserVram / (1024.0 * 1024.0);
        public double BudgetUsageMb => (DisplayedTotalVram ?? 0) / (1024.0 * 1024.0);
        public string BudgetUsagePercentText => DisplayedTotalVram is null || UserVram <= 0 ? "–" : $"{(double)DisplayedTotalVram / UserVram * 100.0:0}%";
        public string VramEstimateModeLabel => ShowMaxVram ? "Max estimate" : "Current estimate";

        public bool ShowOnlyActive { get => _showOnlyActive; set { if (SetProperty(ref _showOnlyActive, value)) ApplyFiltersAndSort(); } }
        private bool _showOnlyActive;
        public bool ShowOnlyFavorites { get => _showOnlyFavorites; set { if (SetProperty(ref _showOnlyFavorites, value)) ApplyFiltersAndSort(); } }
        private bool _showOnlyFavorites;
        public bool ShowMaxVram { get => _showMaxVram; set { if (SetProperty(ref _showMaxVram, value)) NotifyBudgetChanged(); } }
        private bool _showMaxVram;
        public string? CurrentSortColumn { get => _currentSortColumn; private set => SetProperty(ref _currentSortColumn, value); }
        private string? _currentSortColumn = "Mod.Name";
        public ListSortDirection SortDirection { get => _sortDirection; private set => SetProperty(ref _sortDirection, value); }
        private ListSortDirection _sortDirection = ListSortDirection.Ascending;
        public AsyncRelayCommand CalculateVramCommand { get; }
        public DelegateCommand<string> SortCommand { get; }

        public VramAnalysisViewModel(IModListManager modListManager, IDialogService dialogService, ILoggerService logger, IPathService pathService, ISystemInfoService systemInfoService)
        {
            _modListManager = modListManager; 
            _dialogService = dialogService; 
            _logger = logger; 
            _pathService = pathService; 
            _systemInfoService = systemInfoService;

            CalculateVramCommand = CreateCancellableAsyncCommand(ExecuteVramCalculationAsync, () => !IsBusy, nameof(IsBusy));
            SortCommand = CreateCommand<string>(ExecuteSortCommand);

            _modListManager.ListChanged += OnModListChanged; 

            LoadSystemInfo(); 
            _ = Task.Run(LoadMods); 
            _baselineTask = Task.Run(RunBaselineCalculationAsync);
        }

        private void LoadSystemInfo() { Task.Run(() => { UserVram = _systemInfoService.GetPrimaryGpuVram(); OnPropertyChanged(nameof(UserVram)); OnPropertyChanged(nameof(IsUserVramAvailable)); NotifyBudgetChanged(); }); }
        private void LoadMods() { _gameBaselineMods = _modListManager.GetAllMods().Where(m => m.ModType == ModType.Core || m.ModType == ModType.Expansion).ToList(); _allVramMods = _modListManager.GetAllMods().Where(m => m.ModType != ModType.Core && m.ModType != ModType.Expansion && m.Textures).Select(m => new VramModItemWrapper(m)).ToList(); ShowOnlyActive = false; ShowOnlyFavorites = false; ShowMaxVram = false; _modsCalculated = false; ApplyFiltersAndSort(); UpdateTotalVramEstimates(); Task.Run(RunBaselineCalculationAsync); }
        private void OnModListChanged(object? sender, EventArgs e) => RunOnUIThread(LoadMods);

        private async Task ExecuteVramCalculationAsync(CancellationToken ct) 
        { 
            IsBusy = true; 
            StatusMessage = "Starting VRAM calculation..."; 
            ProgressDialogViewModel? progressVm = null; 
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct); 

            try 
            { 
                if (_baselineTask != null) { try { await _baselineTask; } catch { } }

                var modsToCalculate = _allVramMods.ToList(); 
                if (!modsToCalculate.Any()) { StatusMessage = "No mods with textures found to analyze."; return; } 

                await RunOnUIThreadAsync(() => { progressVm = _dialogService.ShowProgressDialog("Calculating VRAM", "Preparing...", true, false, linkedCts); }); 
                await Task.Delay(50, linkedCts.Token); 

                var activeModIds = _modListManager.VirtualActiveMods.Select(m => m.Mod.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase); 
                var majorVersion = _pathService.GetMajorGameVersion(); 
                int processedCount = 0; 

                await Task.Run(async () => 
                { 
                    foreach (var modWrapper in modsToCalculate) 
                    { 
                        linkedCts.Token.ThrowIfCancellationRequested(); 
                        processedCount++; 

                        RunOnUIThread(() => { progressVm?.UpdateProgress((int)((double)processedCount / modsToCalculate.Count * 100), $"Analyzing: {modWrapper.Mod.Name}"); }); 

                        var result = VramEstimator.Calculate(modWrapper.Mod.Path, majorVersion, activeModIds);
                        if (result.EstimatedVramCompressed == 0 && !string.IsNullOrWhiteSpace(result.Logs))
                        {
                            System.Diagnostics.Debug.WriteLine(result.Logs); // Prints to Visual Studio Output Window

                            try {

                                // _logger.LogMessage(result.Logs); 
                            } catch { }
                        }

                        RunOnUIThread(() => { 
                            modWrapper.EstimatedVramUncompressed = result.EstimatedVramUncompressed; 
                            modWrapper.EstimatedVramCompressed = result.EstimatedVramCompressed; 
                            modWrapper.MaxEstimatedVramUncompressed = result.MaxEstimatedVramUncompressed; 
                            modWrapper.MaxEstimatedVramCompressed = result.MaxEstimatedVramCompressed; 
                            modWrapper.ConditionalDependencies = result.ConditionalDependencies; 
                            modWrapper.TextureCount = result.TextureCount; 
                            modWrapper.MaxTextureCount = result.MaxTextureCount; 
                            modWrapper.InAtlasCount = result.InAtlasCount; 
                            modWrapper.MaxInAtlasCount = result.MaxInAtlasCount; 
                            modWrapper.Logs = result.Logs;
                        }); 
                        await Task.Delay(10, linkedCts.Token); 
                    } 
                }, linkedCts.Token); 

                _modsCalculated = true; 
                StatusMessage = "VRAM calculation complete."; 
                progressVm?.CompleteOperation("Calculation complete."); 
            } 
            catch (OperationCanceledException) { StatusMessage = "VRAM calculation cancelled."; progressVm?.ForceClose(); } 
            catch (Exception ex) { StatusMessage = "An error occurred."; _logger.LogException(ex, StatusMessage, nameof(VramAnalysisViewModel)); progressVm?.ForceClose(); await _dialogService.ShowError("Calculation Error", $"An error occurred: {ex.Message}"); } 
            finally { IsBusy = false; RunOnUIThread(UpdateTotalVramEstimates); progressVm?.Dispose(); } 
        }

        private async Task RunBaselineCalculationAsync() 
        { 
            if (Interlocked.CompareExchange(ref _baselineRunning, 1, 0) != 0) return; 
            try 
            { 
                long coreVramCompressed = 0, coreVramUncompressed = 0, expansionsVramCompressed = 0, expansionsVramUncompressed = 0;
                int coreTextureCount = 0, coreInAtlasCount = 0, expansionsTextureCount = 0, expansionsInAtlasCount = 0;
                var gameContentLog = new StringBuilder();

                await Task.Run(() => 
                { 
                    foreach (var gameMod in _gameBaselineMods)
                    { 
                        if (gameMod.ModType != ModType.Core && !gameMod.IsActive) continue; 

                        var gameResult = GameContentVramSource.Estimate(gameMod.Path, gameContentLog); 
                        if (gameMod.ModType == ModType.Core) 
                        { 
                            coreVramCompressed += gameResult.VramCompressed; 
                            coreVramUncompressed += gameResult.VramUncompressed; 
                            coreTextureCount += gameResult.TextureCount; 
                            coreInAtlasCount += gameResult.InAtlasCount; 
                        } 
                        else 
                        { 
                            expansionsVramCompressed += gameResult.VramCompressed; 
                            expansionsVramUncompressed += gameResult.VramUncompressed; 
                            expansionsTextureCount += gameResult.TextureCount; 
                            expansionsInAtlasCount += gameResult.InAtlasCount; 
                        } 
                    } 
                }); 

                RunOnUIThread(() => { 
                    CoreVramCompressed = coreVramCompressed; 
                    CoreVramUncompressed = coreVramUncompressed; 
                    CoreTextureCount = coreTextureCount; 
                    CoreInAtlasCount = coreInAtlasCount; 
                    ExpansionsVramCompressed = expansionsVramCompressed; 
                    ExpansionsVramUncompressed = expansionsVramUncompressed; 
                    ExpansionsTextureCount = expansionsTextureCount; 
                    ExpansionsInAtlasCount = expansionsInAtlasCount; 
                    UpdateTotalVramEstimates(); 
                }); 
            } 
            catch (Exception ex) { _logger.LogException(ex, "Game baseline calculation failed.", nameof(VramAnalysisViewModel)); } 
            finally { Interlocked.Exchange(ref _baselineRunning, 0); } 
        }

        private void ExecuteSortCommand(string? propertyPath) { if (string.IsNullOrEmpty(propertyPath)) return; SortDirection = (CurrentSortColumn == propertyPath && SortDirection == ListSortDirection.Ascending) ? ListSortDirection.Descending : ListSortDirection.Ascending; CurrentSortColumn = propertyPath; ApplyFiltersAndSort(); }

        private void UpdateTotalVramEstimates() {
            long activeTotal = 0, favoriteTotal = 0, maxActiveTotal = 0, maxFavoriteTotal = 0;
            int activeTextureTotal = 0, maxActiveTextureTotal = 0, favoriteTextureTotal = 0, maxFavoriteTextureTotal = 0;
            int activeInAtlasTotal = 0, maxActiveInAtlasTotal = 0; 
            foreach (var mod in _allVramMods) {
                if (mod.Mod.IsActive) {
                    activeTotal += mod.EstimatedVramCompressed ?? 0; maxActiveTotal += mod.MaxEstimatedVramCompressed ?? 0;
                    activeTextureTotal += mod.TextureCount ?? 0; maxActiveTextureTotal += mod.MaxTextureCount ?? 0;
                    activeInAtlasTotal += mod.InAtlasCount ?? 0; maxActiveInAtlasTotal += mod.MaxInAtlasCount ?? 0; 
                }
                if (mod.Mod.IsFavorite) { favoriteTotal += mod.EstimatedVramCompressed ?? 0; maxFavoriteTotal += mod.MaxEstimatedVramCompressed ?? 0; favoriteTextureTotal += mod.TextureCount ?? 0; maxFavoriteTextureTotal += mod.MaxTextureCount ?? 0; }
            }
            TotalVramCompressedActiveMods = _modsCalculated ? activeTotal : null; TotalVramCompressedFavoriteMods = _modsCalculated ? favoriteTotal : null;
            MaxTotalVramCompressedActiveMods = _modsCalculated ? maxActiveTotal : null; MaxTotalVramCompressedFavoriteMods = _modsCalculated ? maxFavoriteTotal : null;
            TotalTextureCountActiveMods = _modsCalculated ? activeTextureTotal : null; MaxTotalTextureCountActiveMods = _modsCalculated ? maxActiveTextureTotal : null;
            TotalTextureCountFavoriteMods = _modsCalculated ? favoriteTextureTotal : null; MaxTotalTextureCountFavoriteMods = _modsCalculated ? maxFavoriteTextureTotal : null;
            TotalInAtlasCountActiveMods = _modsCalculated ? activeInAtlasTotal : null; MaxTotalInAtlasCountActiveMods = _modsCalculated ? maxActiveInAtlasTotal : null; 

            TotalVramWithGameCompressed = SumOrNull(TotalVramCompressedActiveMods, CoreVramCompressed, ExpansionsVramCompressed);
            MaxTotalVramWithGameCompressed = SumOrNull(MaxTotalVramCompressedActiveMods, CoreVramCompressed, ExpansionsVramCompressed);
            IsOverBudget = TotalVramWithGameCompressed is long totalWithGame && IsUserVramAvailable && totalWithGame > UserVram;
            NotifyBudgetChanged();
        }

        private static long? SumOrNull(params long?[] values) {
            if (values.All(v => v is null)) return null;
            long sum = 0;
            foreach (var value in values) sum += value ?? 0;
            return sum;
        }

        private void NotifyBudgetChanged() {
            OnPropertyChanged(nameof(DisplayedTotalVram));
            OnPropertyChanged(nameof(DisplayedModsVram));
            OnPropertyChanged(nameof(DisplayedFavoritesVram));
            OnPropertyChanged(nameof(DisplayedTextureCount));
            OnPropertyChanged(nameof(DisplayedInAtlasCount));
            OnPropertyChanged(nameof(BudgetCapacityMb));
            OnPropertyChanged(nameof(BudgetUsageMb));
            OnPropertyChanged(nameof(BudgetUsagePercentText));
            OnPropertyChanged(nameof(VramEstimateModeLabel));
        }

        private void ApplyFiltersAndSort() {
            IEnumerable<VramModItemWrapper> view = _allVramMods;
            if (ShowOnlyActive) view = view.Where(m => m.Mod.IsActive);
            if (ShowOnlyFavorites) view = view.Where(m => m.Mod.IsFavorite);
            var sortProperty = CurrentSortColumn ?? "Mod.Name";
            if (ShowMaxVram) {
                if (sortProperty.Contains("EstimatedVram")) sortProperty = sortProperty.Replace("EstimatedVram", "MaxEstimatedVram");
                if (sortProperty.Contains("TextureCount")) sortProperty = sortProperty.Replace("TextureCount", "MaxTextureCount");
                if (sortProperty.Contains("InAtlasCount")) sortProperty = sortProperty.Replace("InAtlasCount", "MaxInAtlasCount"); 
            }
            var sortedView = SortDirection == ListSortDirection.Ascending ? view.OrderBy(mod => GetPropertyValue(mod, sortProperty)) : view.OrderByDescending(mod => GetPropertyValue(mod, sortProperty));
            RunOnUIThread(() => { VramMods = new ObservableCollection<VramModItemWrapper>(sortedView); });
        }

        private object? GetPropertyValue(VramModItemWrapper modWrapper, string propertyPath) { object? currentObject = modWrapper; foreach (var part in propertyPath.Split('.')) { if (currentObject == null) return null; var prop = currentObject.GetType().GetProperty(part); if (prop == null) return null; currentObject = prop.GetValue(currentObject); } return currentObject; }
        protected override void Dispose(bool disposing) { if (!_disposed && disposing) _modListManager.ListChanged -= OnModListChanged; base.Dispose(disposing); }
    }
}


