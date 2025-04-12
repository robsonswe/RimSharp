using RimSharp.Core.Commands;
using RimSharp.Features.ModManager.Dialogs.DuplicateMods;
using RimSharp.Features.ModManager.Dialogs.Incompatibilities;
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities; // For IncompatibilityGroup
using RimSharp.MyApp.AppFiles; // For ViewModelBase/RunOnUIThread
using RimSharp.MyApp.Dialogs; // For MessageDialogResult, MessageDialogType
using RimSharp.Shared.Models; // For ModItem
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows; // For Application.Current etc.

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    // Mark the class as partial
    public partial class ModActionsViewModel
    {
        // Partial initialization method
        private void InitializeToolsAnalysisCommands()
        {
            ResolveDependenciesCommand = new AsyncRelayCommand(ExecuteResolveDependencies, CanExecuteSimpleCommands);
            CheckIncompatibilitiesCommand = new AsyncRelayCommand(ExecuteCheckIncompatibilities, CanExecuteCheckIncompatibilities);
            CheckDuplicatesCommand = new RelayCommand(ExecuteCheckDuplicates, CanExecuteSimpleCommands);
        }

        // Placeholder command initialization (can also be in a separate file if desired)
        private void InitializePlaceholderCommands()
        {
             StripModsCommand = new RelayCommand(_ => _dialogService.ShowInformation("Not Implemented", "Strip mods: Functionality not yet implemented."), _ => !IsParentLoading);
             FixIntegrityCommand = new RelayCommand(_ => _dialogService.ShowInformation("Not Implemented", "Fix integrity: Functionality not yet implemented."), _ => !IsParentLoading);
             RunGameCommand = new RelayCommand(_ => _dialogService.ShowInformation("Not Implemented", "Run game: Functionality not yet implemented."), _ => !IsParentLoading);
        }

        // --- Execution Methods ---
        private async Task ExecuteResolveDependencies(CancellationToken ct = default)
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                var result = await Task.Run(() => _modListManager.ResolveDependencies(), ct);
                var (addedMods, missingDependencies) = result;

                if (addedMods.Any()) HasUnsavedChangesRequest?.Invoke(this, true);

                var message = new StringBuilder();
                // --- Build Message ---
                if (addedMods.Count > 0)
                {
                    message.AppendLine("The following dependencies were automatically added:");
                    message.AppendLine();
                    foreach (var mod in addedMods) message.AppendLine($"- {mod.Name} ({mod.PackageId})");
                    message.AppendLine();
                }
                if (missingDependencies.Count > 0)
                {
                    message.AppendLine("The following dependencies are missing:");
                    message.AppendLine();
                    foreach (var dep in missingDependencies)
                    {
                        message.AppendLine($"- {dep.displayName} ({dep.packageId})");
                        message.AppendLine($"  Required by: {string.Join(", ", dep.requiredBy)}");
                        if (!string.IsNullOrEmpty(dep.steamUrl)) message.AppendLine($"  Workshop URL: {dep.steamUrl}");
                        message.AppendLine();
                    }
                }
                // --- Show Dialog ---
                RunOnUIThread(() =>
                {
                    if (message.Length == 0)
                        _dialogService.ShowInformation("Dependencies Check", "No missing dependencies found and no new dependencies were added.");
                    else if (missingDependencies.Count == 0)
                        _dialogService.ShowInformation("Dependencies Added", message.ToString().TrimEnd());
                    else
                        _dialogService.ShowMessageWithCopy("Dependencies Status", message.ToString().TrimEnd(), MessageDialogType.Warning);
                });
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ExecuteResolveDependencies] Operation cancelled.");
                RunOnUIThread(() => _dialogService.ShowWarning("Operation Cancelled", "Dependency resolution was cancelled."));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error resolving dependencies: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Resolution Error", $"Error resolving dependencies: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private async Task ExecuteCheckIncompatibilities(CancellationToken ct = default)
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                var activeMods = _modListManager.VirtualActiveMods.Select(entry => entry.Mod).ToList();
                var incompatibilities = await Task.Run(() => _incompatibilityService.FindIncompatibilities(activeMods), ct);
                ct.ThrowIfCancellationRequested();

                if (incompatibilities.Count == 0)
                {
                    RunOnUIThread(() => _dialogService.ShowInformation("Compatibility Check", "No incompatibilities found."));
                    return;
                }

                var groups = await Task.Run(() => _incompatibilityService.GroupIncompatibilities(incompatibilities), ct);
                ct.ThrowIfCancellationRequested();

                if (groups.Count == 0)
                {
                    RunOnUIThread(() => _dialogService.ShowInformation("Compatibility Check", "Incompatibilities found but could not be grouped for resolution."));
                    return;
                }

                RunOnUIThread(() => ShowIncompatibilityDialog(groups)); // Helper defined below
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ExecuteCheckIncompatibilities] Operation cancelled.");
                RunOnUIThread(() => _dialogService.ShowWarning("Operation Cancelled", "Incompatibility check was cancelled."));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking mod incompatibilities: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Compatibility Error", $"Error checking mod incompatibilities: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private void ExecuteCheckDuplicates() // Remains synchronous
        {
            IsLoadingRequest?.Invoke(this, true);
            List<IGrouping<string, ModItem>> actualDuplicateGroups = null;
            try
            {
                var allMods = _modListManager.GetAllMods().ToList();
                actualDuplicateGroups = allMods
                    .Where(m => !string.IsNullOrEmpty(m.PackageId))
                    .GroupBy(m => m.PackageId.ToLowerInvariant())
                    .Where(g => g.Count() > 1)
                    .ToList();

                if (actualDuplicateGroups.Any())
                {
                    ShowDuplicateModsDialog(actualDuplicateGroups); // Helper defined below
                }
                else
                {
                    _dialogService.ShowInformation("Duplicates Check", "No duplicate mods found based on Package ID.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking duplicates: {ex}");
                _dialogService.ShowError("Duplicates Error", $"Error checking for duplicate mods: {ex.Message}");
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }


        // --- Helper Methods for Dialogs/Callbacks (Specific to Tools/Analysis) ---
        private void ShowIncompatibilityDialog(List<IncompatibilityGroup> groups)
        {
            var dialogViewModel = new ModIncompatibilityDialogViewModel(
                groups,
                ApplyIncompatibilityResolutions, // Callback defined below
                () => { /* Cancel handler */ }
            );
            var dialog = new ModIncompatibilityDialogView(dialogViewModel);
            dialog.Owner = Application.Current.MainWindow;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dialog.ShowDialog();
        }

        private void ApplyIncompatibilityResolutions(List<ModItem> modsToRemove) // Remains synchronous
        {
            if (modsToRemove == null || modsToRemove.Count == 0) return;
            try
            {
                foreach (var mod in modsToRemove)
                {
                    _modListManager.DeactivateMod(mod);
                }
                HasUnsavedChangesRequest?.Invoke(this, true);
                _dialogService.ShowInformation("Incompatibilities Resolved", $"Resolved incompatibilities by deactivating {modsToRemove.Count} mods.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying incompatibility resolutions: {ex}");
                _dialogService.ShowError("Resolution Error", $"Error applying incompatibility resolutions: {ex.Message}");
            }
        }

        private void ShowDuplicateModsDialog(List<IGrouping<string, ModItem>> duplicateGroups)
        {
            var dialogViewModel = new DuplicateModDialogViewModel(
                    duplicateGroups,
                    // Pass the call to the async void helper method in ModDeletionHelper.cs
                    pathsToDelete => DeleteDuplicateModsAsyncInternal(pathsToDelete),
                    () => { /* Cancel callback */ });

            var view = new DuplicateModDialogView(dialogViewModel);
            view.Owner = Application.Current.MainWindow;
            view.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            view.ShowDialog();
        }
    }
}