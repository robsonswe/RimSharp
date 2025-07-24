// --- Features/VramAnalysis/ViewModels/VramAnalysisViewModel.cs ---
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel; // IMPORTANT: Add this for ListSortDirection
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Core.Commands.Base;
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

        // MODIFIED: This will now hold ALL mods, acting as the master list.
        private List<VramModItemWrapper> _allVramMods = new();

        private ObservableCollection<VramModItemWrapper> _vramMods = new();
        public ObservableCollection<VramModItemWrapper> VramMods
        {
            get => _vramMods;
            set => SetProperty(ref _vramMods, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }
        
        // MODIFIED: This message is no longer used in the UI, but kept for other potential status updates.
        private string _statusMessage = "Ready. Click 'Calculate VRAM' to begin analysis.";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private long _totalVramCompressedActiveMods;
        public long TotalVramCompressedActiveMods
        {
            get => _totalVramCompressedActiveMods;
            set => SetProperty(ref _totalVramCompressedActiveMods, value);
        }

        private long _totalVramCompressedFavoriteMods;
        public long TotalVramCompressedFavoriteMods
        {
            get => _totalVramCompressedFavoriteMods;
            set => SetProperty(ref _totalVramCompressedFavoriteMods, value);
        }

        // --- NEW: Filter properties ---
        private bool _showOnlyActive;
        public bool ShowOnlyActive
        {
            get => _showOnlyActive;
            set
            {
                if (SetProperty(ref _showOnlyActive, value))
                {
                    ApplyFiltersAndSort();
                }
            }
        }

        private bool _showOnlyFavorites;
        public bool ShowOnlyFavorites
        {
            get => _showOnlyFavorites;
            set
            {
                if (SetProperty(ref _showOnlyFavorites, value))
                {
                    ApplyFiltersAndSort();
                }
            }
        }
        // --- END NEW ---

        private string? _currentSortColumn = "Mod.Name";
        public string? CurrentSortColumn 
        {
            get => _currentSortColumn;
            private set => SetProperty(ref _currentSortColumn, value);
        }
        
        private ListSortDirection _sortDirection = ListSortDirection.Ascending;
        public ListSortDirection SortDirection
        {
            get => _sortDirection;
            private set => SetProperty(ref _sortDirection, value);
        }

        public AsyncRelayCommand CalculateVramCommand { get; }
        public DelegateCommand<string> SortCommand { get; }

        public VramAnalysisViewModel(IModListManager modListManager, IDialogService dialogService, ILoggerService logger)
        {
            _modListManager = modListManager;
            _dialogService = dialogService;
            _logger = logger;

            CalculateVramCommand = CreateCancellableAsyncCommand(ExecuteVramCalculationAsync, () => !IsBusy, nameof(IsBusy));
            SortCommand = CreateCommand<string>(ExecuteSortCommand);

            _modListManager.ListChanged += OnModListChanged;
            LoadMods();
        }
        
        // MODIFIED: Now populates the master list and then calls ApplyFiltersAndSort.
        private void LoadMods()
        {
            _logger.LogInfo("Loading mods for VRAM analysis view.", nameof(VramAnalysisViewModel));
            var allMods = _modListManager.GetAllMods()
                .Where(m => m.ModType != ModType.Core && m.ModType != ModType.Expansion && m.Textures)
                .Select(m => new VramModItemWrapper(m));

            _allVramMods = new List<VramModItemWrapper>(allMods);
            
            // Reset filters to default state
            _showOnlyActive = false;
            _showOnlyFavorites = false;
            OnPropertyChanged(nameof(ShowOnlyActive));
            OnPropertyChanged(nameof(ShowOnlyFavorites));

            ApplyFiltersAndSort();
            UpdateTotalVramEstimates(); // Calculate totals from the full list
            StatusMessage = "Ready. Click 'Calculate VRAM' to begin analysis.";
        }
        
        private void OnModListChanged(object? sender, EventArgs e)
        {
            RunOnUIThread(LoadMods);
        }

        // MODIFIED: Now operates on _allVramMods, not the public collection.
        private async Task ExecuteVramCalculationAsync(CancellationToken ct)
        {
            IsBusy = true;
            StatusMessage = "Starting VRAM calculation...";
            ProgressDialogViewModel? progressVm = null;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                var modsToCalculate = _allVramMods.ToList(); // Work on a copy of the master list
                if (!modsToCalculate.Any())
                {
                    StatusMessage = "No mods with textures found to analyze.";
                    return;
                }
                
                RunOnUIThread(() =>
                {
                    progressVm = _dialogService.ShowProgressDialog("Calculating VRAM", "Preparing...", true, false, linkedCts);
                });
                await Task.Delay(50, CancellationToken.None);

                int processedCount = 0;
                int totalCount = modsToCalculate.Count;

                await Task.Run(async () =>
                {
                    foreach (var modWrapper in modsToCalculate)
                    {
                        linkedCts.Token.ThrowIfCancellationRequested();
                        processedCount++;
                        RunOnUIThread(() =>
                        {
                            progressVm?.UpdateProgress((int)((double)processedCount / totalCount * 100), $"Analyzing: {modWrapper.Mod.Name}");
                        });
                        
                        try
                        {
                            var texturePaths = VramEstimator.GetTexturePathsForMod(modWrapper.Mod.Path);
                            var result = VramEstimator.Calculate(texturePaths);

                            // No UI thread switch needed here as we are just updating data on the object.
                            // The object itself will raise PropertyChanged on the UI thread when its properties are set.
                            modWrapper.EstimatedVramUncompressed = result.EstimatedVramUncompressed;
                            modWrapper.EstimatedVramCompressed = result.EstimatedVramCompressed;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Failed to calculate VRAM for '{modWrapper.Mod.Name}': {ex.Message}", nameof(VramAnalysisViewModel));
                        }
                        await Task.Delay(10, linkedCts.Token);
                    }
                }, linkedCts.Token);

                StatusMessage = "VRAM calculation complete.";
                progressVm?.CompleteOperation("Calculation complete.");
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "VRAM calculation cancelled.";
                progressVm?.ForceClose();
            }
            catch (Exception ex)
            {
                StatusMessage = "An error occurred during VRAM calculation.";
                _logger.LogException(ex, StatusMessage, nameof(VramAnalysisViewModel));
                progressVm?.ForceClose();
                _dialogService.ShowError("Calculation Error", $"An error occurred: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                RunOnUIThread(UpdateTotalVramEstimates);
                progressVm?.Dispose();
            }
        }
        
        // MODIFIED: Now just updates sort properties and calls the central update method.
        private void ExecuteSortCommand(string? propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath)) return;

            if (CurrentSortColumn == propertyPath)
            {
                SortDirection = (SortDirection == ListSortDirection.Ascending) ? ListSortDirection.Descending : ListSortDirection.Ascending;
            }
            else
            {
                CurrentSortColumn = propertyPath;
                SortDirection = ListSortDirection.Ascending;
            }

            ApplyFiltersAndSort();
        }

        // MODIFIED: Now calculates from the master list _allVramMods.
        private void UpdateTotalVramEstimates()
        {
            long activeTotal = 0;
            long favoriteTotal = 0;

            foreach (var modWrapper in _allVramMods) // Iterate over the full list
            {
                if (modWrapper.Mod.IsActive)
                {
                    activeTotal += modWrapper.EstimatedVramCompressed;
                }
                if (modWrapper.Mod.IsFavorite)
                {
                    favoriteTotal += modWrapper.EstimatedVramCompressed;
                }
            }

            TotalVramCompressedActiveMods = activeTotal;
            TotalVramCompressedFavoriteMods = favoriteTotal;
        }

        // --- NEW: Central method to apply filters and sorting ---
        private void ApplyFiltersAndSort()
        {
            IEnumerable<VramModItemWrapper> view = _allVramMods;

            if (ShowOnlyActive)
            {
                view = view.Where(m => m.Mod.IsActive);
            }
            if (ShowOnlyFavorites)
            {
                view = view.Where(m => m.Mod.IsFavorite);
            }

            IOrderedEnumerable<VramModItemWrapper> sortedView;
            if (SortDirection == ListSortDirection.Ascending)
            {
                sortedView = view.OrderBy(mod => GetPropertyValue(mod, CurrentSortColumn));
            }
            else
            {
                sortedView = view.OrderByDescending(mod => GetPropertyValue(mod, CurrentSortColumn));
            }

            RunOnUIThread(() =>
            {
                VramMods = new ObservableCollection<VramModItemWrapper>(sortedView);
            });
        }

        private object? GetPropertyValue(VramModItemWrapper modWrapper, string? propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath)) return null;
            object? currentObject = modWrapper;
            foreach (var part in propertyPath.Split('.'))
            {
                if (currentObject == null) return null;
                var prop = currentObject.GetType().GetProperty(part);
                if (prop == null)
                {
                    _logger.LogWarning($"Property '{part}' not found in path '{propertyPath}' for type '{currentObject.GetType().Name}'.", nameof(VramAnalysisViewModel));
                    return null;
                }
                currentObject = prop.GetValue(currentObject);
            }
            return currentObject;
        }
        
        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                if (_modListManager != null)
                {
                    _modListManager.ListChanged -= OnModListChanged;
                }
            }
            base.Dispose(disposing);
        }
    }
}