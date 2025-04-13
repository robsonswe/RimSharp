using RimSharp.Core.Commands;
using RimSharp.Features.ModManager.Dialogs.DuplicateMods;
using RimSharp.Features.ModManager.Dialogs.Incompatibilities;
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.MyApp.AppFiles;
using RimSharp.MyApp.Dialogs;
using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    public partial class ModActionsViewModel
    {
        private void InitializeToolsAnalysisCommands()
        {
            ResolveDependenciesCommand = CreateCancellableAsyncCommand(
                ExecuteResolveDependencies,
                CanExecuteSimpleCommands,
                nameof(IsParentLoading));

            CheckIncompatibilitiesCommand = CreateCancellableAsyncCommand(
                ExecuteCheckIncompatibilities,
                CanExecuteCheckIncompatibilities,
                nameof(IsParentLoading));

            CheckDuplicatesCommand = CreateCommand(
                ExecuteCheckDuplicates,
                CanExecuteSimpleCommands,
                nameof(IsParentLoading));
        }

        private void InitializePlaceholderCommands()
        {
            StripModsCommand = CreateCommand(
                () => _dialogService.ShowInformation("Not Implemented", "Strip mods: Functionality not yet implemented."),
                () => true,
                nameof(IsParentLoading));

            FixIntegrityCommand = CreateCommand(
                () => _dialogService.ShowInformation("Not Implemented", "Fix integrity: Functionality not yet implemented."),
                () => true,
                nameof(IsParentLoading));

            RunGameCommand = CreateCommand(
                () => _dialogService.ShowInformation("Not Implemented", "Run game: Functionality not yet implemented."),
                () => true,
                nameof(IsParentLoading));
        }
        private bool CanExecuteCheckIncompatibilities() => !IsParentLoading && _modListManager.VirtualActiveMods.Any();

        private async Task ExecuteResolveDependencies(CancellationToken ct)
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                ct.ThrowIfCancellationRequested();
                var result = await Task.Run(() => _modListManager.ResolveDependencies(), ct);
                ct.ThrowIfCancellationRequested();

                var (addedMods, missingDependencies) = result;

                var message = new StringBuilder();
                if (addedMods.Count > 0)
                {
                    message.AppendLine("Dependencies automatically added:");
                    message.AppendLine();
                    foreach (var mod in addedMods) message.AppendLine($"- {mod.Name} ({mod.PackageId})");
                    message.AppendLine();
                }
                if (missingDependencies.Count > 0)
                {
                    message.AppendLine("MISSING Dependencies:");
                    message.AppendLine();
                    foreach (var dep in missingDependencies)
                    {
                        message.AppendLine($"- {dep.displayName} ({dep.packageId})");
                        message.AppendLine($"  Required by: {string.Join(", ", dep.requiredBy)}");
                        if (!string.IsNullOrEmpty(dep.steamUrl)) message.AppendLine($"  Workshop URL: {dep.steamUrl}");
                        message.AppendLine();
                    }
                }

                await RunOnUIThreadAsync(() =>
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

        private async Task ExecuteCheckIncompatibilities(CancellationToken ct)
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                ct.ThrowIfCancellationRequested();
                var activeMods = _modListManager.VirtualActiveMods.Select(entry => entry.Mod).ToList();
                var incompatibilities = await Task.Run(() => _incompatibilityService.FindIncompatibilities(activeMods), ct);
                ct.ThrowIfCancellationRequested();

                if (incompatibilities.Count == 0)
                {
                    await RunOnUIThreadAsync(() => _dialogService.ShowInformation("Compatibility Check", "No incompatibilities found."));
                    return;
                }

                var groups = await Task.Run(() => _incompatibilityService.GroupIncompatibilities(incompatibilities), ct);
                ct.ThrowIfCancellationRequested();

                if (groups.Count == 0)
                {
                    await RunOnUIThreadAsync(() => _dialogService.ShowInformation("Compatibility Check", "Incompatibilities found but could not be grouped for resolution."));
                    return;
                }

                await RunOnUIThreadAsync(() => ShowIncompatibilityDialog(groups));
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

        private void ExecuteCheckDuplicates()
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
                    RunOnUIThread(() => ShowDuplicateModsDialog(actualDuplicateGroups));
                }
                else
                {
                    RunOnUIThread(() => _dialogService.ShowInformation("Duplicates Check", "No duplicate mods found based on Package ID."));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking duplicates: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Duplicates Error", $"Error checking for duplicate mods: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private void ShowIncompatibilityDialog(List<IncompatibilityGroup> groups)
        {
            var dialogViewModel = new ModIncompatibilityDialogViewModel(
                groups,
                ApplyIncompatibilityResolutions,
                () => { }
            );
            var dialog = new ModIncompatibilityDialogView(dialogViewModel);
            dialog.Owner = Application.Current?.MainWindow;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dialog.ShowDialog();
        }

        private void ApplyIncompatibilityResolutions(List<ModItem> modsToRemove)
        {
            if (modsToRemove == null || modsToRemove.Count == 0) return;
            try
            {
                _modListManager.DeactivateMods(modsToRemove);
                _dialogService.ShowInformation("Incompatibilities Resolved", $"Resolved incompatibilities by deactivating {modsToRemove.Count} mod(s).");
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
                pathsToDelete => DeleteDuplicateModsAsyncInternal(pathsToDelete),
                () => { });
            var view = new DuplicateModDialogView(dialogViewModel);
            view.Owner = Application.Current?.MainWindow;
            view.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            view.ShowDialog();
        }
    }
}