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
        
        private string _statusMessage = "Ready. Click 'Calculate VRAM' to begin analysis.";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // --- NEW: Total VRAM properties for summary bar ---
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
        // --- END NEW ---

        // --- Sorting Properties and Command ---
        private string? _currentSortColumn; // Stores the name of the column currently sorted by
        private ListSortDirection _sortDirection = ListSortDirection.Ascending; // Stores the current sort direction

        public AsyncRelayCommand CalculateVramCommand { get; }
        public DelegateCommand<string> SortCommand { get; } // Command for column header clicks

        // MODIFIED: Map property paths to friendly header names
        private static readonly Dictionary<string, string> _sortPropertyToHeaderMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Mod.Name", "Mod Name" },
            { "Mod.SizeInfo.TotalSize", "Total Size" },
            { "Mod.SizeInfo.MinTextureSize", "Disk Size (DDS)" },      // MODIFIED
            { "Mod.SizeInfo.MaxTextureSize", "Disk Size (PNG)" },      // MODIFIED
            { "EstimatedVramUncompressed", "VRAM (Uncompressed)" },
            { "EstimatedVramCompressed", "VRAM (Compressed)" }
        };

        public VramAnalysisViewModel(IModListManager modListManager, IDialogService dialogService, ILoggerService logger)
        {
            _modListManager = modListManager;
            _dialogService = dialogService;
            _logger = logger;

            CalculateVramCommand = CreateCancellableAsyncCommand(ExecuteVramCalculationAsync, () => !IsBusy, nameof(IsBusy));
            SortCommand = CreateCommand<string>(ExecuteSortCommand);

            _modListManager.ListChanged += OnModListChanged;
            LoadMods();
            UpdateTotalVramEstimates(); // Initial call to populate summary bar
        }

        private void LoadMods()
        {
            _logger.LogInfo("Loading mods for VRAM analysis view.", nameof(VramAnalysisViewModel));
            var allMods = _modListManager.GetAllMods()
                .Where(m => m.ModType != ModType.Core && m.ModType != ModType.Expansion && m.Textures) // Exclude Core/DLC, only include mods with textures
                .OrderBy(m => m.Name) // Default sort by Name ascending
                .Select(m => new VramModItemWrapper(m));

            VramMods = new ObservableCollection<VramModItemWrapper>(allMods);
            _currentSortColumn = "Mod.Name"; // Initialize current sort column
            _sortDirection = ListSortDirection.Ascending; // Initialize sort direction
            StatusMessage = $"Ready. Found {VramMods.Count} mods with textures to analyze.";
            UpdateTotalVramEstimates(); // Call after VramMods is populated
        }

        private void OnModListChanged(object? sender, EventArgs e)
        {
            RunOnUIThread(LoadMods);
        }

        private async Task ExecuteVramCalculationAsync(CancellationToken ct)
        {
            IsBusy = true;
            StatusMessage = "Starting VRAM calculation...";
            ProgressDialogViewModel? progressVm = null;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                var modsToCalculate = VramMods.ToList(); // Work on a copy
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
                        // Ensure progress updates are on UI thread
                        RunOnUIThread(() =>
                        {
                            progressVm?.UpdateProgress((int)((double)processedCount / totalCount * 100), $"Analyzing: {modWrapper.Mod.Name}");
                        });
                        
                        try
                        {
                            var texturePaths = VramEstimator.GetTexturePathsForMod(modWrapper.Mod.Path);
                            var result = VramEstimator.Calculate(texturePaths);

                            // Ensure property updates are on UI thread
                            RunOnUIThread(() =>
                            {
                                modWrapper.EstimatedVramUncompressed = result.EstimatedVramUncompressed;
                                modWrapper.EstimatedVramCompressed = result.EstimatedVramCompressed;
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Failed to calculate VRAM for '{modWrapper.Mod.Name}': {ex.Message}", nameof(VramAnalysisViewModel));
                        }
                         // A small delay to keep the UI responsive
                        await Task.Delay(10, linkedCts.Token);
                    }
                }, linkedCts.Token); // Pass linked token to Task.Run

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
                RunOnUIThread(UpdateTotalVramEstimates); // Recalculate totals after all individual updates are done
                // Dispose of the progress dialog ViewModel
                progressVm?.Dispose();
            }
        }

        // --- ExecuteSortCommand for column sorting ---
        private void ExecuteSortCommand(string? propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath)) return;

            // Toggle sort direction if clicking the same column
            if (_currentSortColumn == propertyPath)
            {
                _sortDirection = (_sortDirection == ListSortDirection.Ascending) ? ListSortDirection.Descending : ListSortDirection.Ascending;
            }
            else
            {
                // New column clicked, reset to ascending
                _currentSortColumn = propertyPath;
                _sortDirection = ListSortDirection.Ascending;
            }

            // Perform sorting using reflection to get the property value dynamically
            IOrderedEnumerable<VramModItemWrapper> sortedMods;

            if (_sortDirection == ListSortDirection.Ascending)
            {
                sortedMods = VramMods.OrderBy(mod => GetPropertyValue(mod, propertyPath));
            }
            else
            {
                sortedMods = VramMods.OrderByDescending(mod => GetPropertyValue(mod, propertyPath));
            }

            // Re-populate ObservableCollection on UI thread
            RunOnUIThread(() =>
            {
                VramMods = new ObservableCollection<VramModItemWrapper>(sortedMods);
                
                // NEW: Get the friendly header name for the status message
                string displayName = _sortPropertyToHeaderMap.TryGetValue(propertyPath, out string? headerText)
                    ? headerText
                    : propertyPath; // Fallback to propertyPath if not found

                StatusMessage = $"Sorted by '{displayName}' {(_sortDirection == ListSortDirection.Ascending ? "Ascending" : "Descending")}.";
                UpdateTotalVramEstimates(); // Recalculate totals after sorting
            });
        }

        // NEW: Method to calculate and update the total VRAM estimates
        private void UpdateTotalVramEstimates()
        {
            long activeTotal = 0;
            long favoriteTotal = 0;

            foreach (var modWrapper in VramMods)
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


        // Helper method to get property value using reflection for dynamic sorting
        private object? GetPropertyValue(VramModItemWrapper modWrapper, string propertyPath)
        {
            // This handles nested properties like "Mod.Name" or "Mod.SizeInfo.TotalSize"
            object? currentObject = modWrapper;
            foreach (var part in propertyPath.Split('.'))
            {
                if (currentObject == null) return null;
                var prop = currentObject.GetType().GetProperty(part);
                if (prop == null)
                {
                    _logger.LogWarning($"Property '{part}' not found in path '{propertyPath}' for type '{currentObject.GetType().Name}'.", nameof(VramAnalysisViewModel));
                    return null; // Or throw exception if this indicates a critical error
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