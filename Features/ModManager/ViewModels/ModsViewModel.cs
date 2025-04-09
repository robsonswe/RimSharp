using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Core.Commands;
using RimSharp.Features.ModManager.Dialogs.DuplicateMods;
using RimSharp.Features.ModManager.Dialogs.Incompatibilities;
using RimSharp.Features.ModManager.Services.Commands;
using RimSharp.MyApp.AppFiles;
using RimSharp.MyApp.Dialogs;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;


namespace RimSharp.Features.ModManager.ViewModels
{
    public class ModsViewModel : ViewModelBase, IDisposable
    {
        private readonly IModDataService _dataService;
        private readonly IModFilterService _filterService;
        private readonly IModCommandService _commandService;
        private readonly IModListIOService _ioService;
        private readonly IModListManager _modListManager;

        private readonly IDialogService _dialogService;

        private readonly IModIncompatibilityService _incompatibilityService;

        //private readonly IModService _modService;

        private ModItem _selectedMod;
        private bool _isLoading;
        private bool _hasUnsavedChanges;

        public ObservableCollection<ModItem> ActiveMods => _filterService.ActiveMods;
        public ObservableCollection<ModItem> InactiveMods => _filterService.InactiveMods;

        public int TotalActiveMods => ActiveMods.Count;
        public int TotalInactiveMods => InactiveMods.Count;

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public ModItem SelectedMod
        {
            get => _selectedMod;
            set => SetProperty(ref _selectedMod, value);
        }

        public string ActiveSearchText
        {
            get => _filterService.ActiveSearchText;
            set
            {
                _filterService.ApplyActiveFilter(value);
                OnPropertyChanged(nameof(ActiveSearchText));
                OnPropertyChanged(nameof(TotalActiveMods));
            }
        }

        public string InactiveSearchText
        {
            get => _filterService.InactiveSearchText;
            set
            {
                _filterService.ApplyInactiveFilter(value);
                OnPropertyChanged(nameof(InactiveSearchText));
                OnPropertyChanged(nameof(TotalInactiveMods));
            }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set => SetProperty(ref _hasUnsavedChanges, value);
        }

        public ICommand SelectModCommand { get; private set; }
        public ICommand ClearActiveListCommand { get; private set; }
        public ICommand SortActiveListCommand { get; private set; }
        public ICommand StripModsCommand { get; private set; }
        public ICommand CreatePackCommand { get; private set; }
        public ICommand FixIntegrityCommand { get; private set; }
        public ICommand ImportListCommand { get; private set; }
        public ICommand ExportListCommand { get; private set; }
        public ICommand SaveCommand { get; private set; }
        public ICommand RunGameCommand { get; private set; }
        public ICommand OpenUrlCommand { get; private set; }
        public ICommand FilterInactiveCommand { get; private set; }
        public ICommand FilterActiveCommand { get; private set; }
        public ICommand ActivateModCommand { get; private set; }
        public ICommand DeactivateModCommand { get; private set; }
        public ICommand DropModCommand { get; private set; }
        public ICommand DeleteModCommand { get; private set; }

        public ICommand ResolveDependenciesCommand { get; private set; }
        public ICommand CheckIncompatibilitiesCommand { get; private set; }

        public ICommand CheckDuplicatesCommand { get; private set; }

        public ModsViewModel(
            IModDataService dataService,
            IModFilterService filterService,
            IModCommandService commandService,
            IModListIOService ioService,
            IModListManager modListManager,
            IModIncompatibilityService incompatibilityService,
            IDialogService dialogService,
            IModService modService)
        {
            // DI remains the same
            _dataService = dataService;
            _filterService = filterService;
            _commandService = commandService;
            _ioService = ioService;
            _modListManager = modListManager;
            _incompatibilityService = incompatibilityService;
            _dialogService = dialogService;


            _modListManager.ListChanged += OnModListManagerChanged;

            InitializeCommands();
            _ = LoadDataAsync();
        }

        private void InitializeCommands()
        {
            SelectModCommand = new RelayCommand(SelectMod);
            ClearActiveListCommand = new RelayCommand(async _ => await ExecuteClearActiveList(), _ => !_isLoading);
            SortActiveListCommand = new RelayCommand(async _ => await ExecuteSortActiveList(), _ => !_isLoading);
            SaveCommand = new RelayCommand(_ => ExecuteSaveMods(), _ => HasUnsavedChanges && !_isLoading);
            DropModCommand = new RelayCommand<DropModArgs>(async args => await ExecuteDropMod(args), args => !_isLoading);
            ActivateModCommand = new RelayCommand<ModItem>(mod => { _modListManager.ActivateMod(mod); HasUnsavedChanges = true; }, mod => mod != null && !_isLoading);
            DeactivateModCommand = new RelayCommand<ModItem>(mod => { _modListManager.DeactivateMod(mod); HasUnsavedChanges = true; }, mod => mod != null && !mod.IsCore && !_isLoading); // Prevent deactivating Core via button
            DeleteModCommand = new RelayCommand<ModItem>(async mod => await ExecuteDeleteModAsync(mod), CanExecuteDeleteMod);
            OpenUrlCommand = new RelayCommand(OpenUrl, _ => SelectedMod != null); // Example condition
            ImportListCommand = new RelayCommand(async _ => await ExecuteImport(), _ => !_isLoading);
            ExportListCommand = new RelayCommand(async _ => await ExecuteExport(), _ => !_isLoading && _modListManager.VirtualActiveMods.Any()); // Condition export has active mods
            CheckIncompatibilitiesCommand = new RelayCommand(async _ => await ExecuteCheckIncompatibilities(), _ => !_isLoading && _modListManager.VirtualActiveMods.Any());
            CheckDuplicatesCommand = new RelayCommand(_ => CheckForDuplicates());

            // Stubbed commands (consider adding CanExecute)
            StripModsCommand = new RelayCommand(StripMods, _ => !_isLoading);
            CreatePackCommand = new RelayCommand(CreatePack, _ => !_isLoading);
            FixIntegrityCommand = new RelayCommand(FixIntegrity, _ => !_isLoading);
            RunGameCommand = new RelayCommand(RunGame, _ => !_isLoading);
            FilterInactiveCommand = new RelayCommand(ExecuteFilterInactive, _ => !_isLoading);
            FilterActiveCommand = new RelayCommand(ExecuteFilterActive, _ => !_isLoading);
            ResolveDependenciesCommand = new RelayCommand(async _ => await ExecuteResolveDependencies(), _ => !_isLoading);
        }


        public async Task RefreshDataAsync()
        {
            Debug.WriteLine("[RefreshDataAsync] Starting refresh...");
            // IsLoading state is handled within LoadDataAsync
            await LoadDataAsync();
            Debug.WriteLine("[RefreshDataAsync] Refresh complete.");
        }


        private async Task LoadDataAsync()
        {
            IsLoading = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                var allMods = await _dataService.LoadAllModsAsync();
                var activeIdsFromConfig = _dataService.LoadActiveModIdsFromConfig();
                _modListManager.Initialize(allMods, activeIdsFromConfig);
                SelectedMod = _modListManager.VirtualActiveMods.FirstOrDefault(m => m.Mod.IsCore).Mod
                             ?? _modListManager.VirtualActiveMods.FirstOrDefault().Mod
                             ?? _modListManager.AllInactiveMods.FirstOrDefault();
                HasUnsavedChanges = false; // Reset after initial load
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading mods: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Loading Error", $"Error loading mods: {ex.Message}"));
                HasUnsavedChanges = false;
            }
            finally
            {
                IsLoading = false; // *** Ensure this is here ***
                CommandManager.InvalidateRequerySuggested(); // Re-evaluate commands
                                                             // Re-evaluate delete command specifically after load completes and path check is possible
                (DeleteModCommand as RelayCommand<ModItem>)?.RaiseCanExecuteChanged();
            }
        }

        private bool CanExecuteDeleteMod(ModItem mod)
        {
            // Can delete if:
            // 1. Mod exists
            // 2. It's not Core
            // 3. It's not an Expansion
            // 4. We are not currently loading/busy
            // 5. The path exists (important!)
            return mod != null
                && !mod.IsCore
                && !mod.IsExpansion
                && !string.IsNullOrEmpty(mod.Path) // Check if path is valid
                && Directory.Exists(mod.Path)     // Check if directory actually exists
                && !_isLoading;
        }

        private async Task ExecuteDeleteModAsync(ModItem mod)
        {
            // Double-check conditions (belt and suspenders)
            if (!CanExecuteDeleteMod(mod))
            {
                Debug.WriteLine($"[ExecuteDeleteModAsync] Precondition failed for mod '{mod?.Name}'. Path: '{mod?.Path}' Exists: {Directory.Exists(mod?.Path ?? "")}");
                _dialogService.ShowWarning("Deletion Blocked", $"Cannot delete mod '{mod?.Name}'. It might be Core/DLC, already deleted, or the application is busy.");
                return;
            }

            // Confirmation Dialog
            var result = _dialogService.ShowConfirmation(
                "Confirm Deletion",
                $"Are you sure you want to permanently delete the mod '{mod.Name}'?\n\nThis action cannot be undone.\n\nPath: {mod.Path}",
                showCancel: true);

            if (result != MessageDialogResult.OK) // Assuming OK means Yes/Confirm
            {
                Debug.WriteLine($"[ExecuteDeleteModAsync] Deletion cancelled by user for mod '{mod.Name}'.");
                return;
            }

            // --- Perform Deletion and Refresh ---
            IsLoading = true; // Indicate busy state
            CommandManager.InvalidateRequerySuggested(); // Disable buttons

            bool deletionSuccess = false;
            try
            {
                Debug.WriteLine($"[ExecuteDeleteModAsync] Attempting to delete directory: {mod.Path}");
                await Task.Run(() => Directory.Delete(mod.Path, recursive: true));
                deletionSuccess = true;
                Debug.WriteLine($"[ExecuteDeleteModAsync] Successfully deleted directory: {mod.Path}");
                // Optionally show success message (might be annoying if deleting many)
                _dialogService.ShowInformation("Deletion Successful", $"Mod '{mod.Name}' was deleted.");
            }
            catch (UnauthorizedAccessException authEx)
            {
                Debug.WriteLine($"[ExecuteDeleteModAsync] Authorization error deleting '{mod.Path}': {authEx.Message}");
                _dialogService.ShowError("Deletion Error", $"Permission denied when trying to delete mod '{mod.Name}'.\nCheck file permissions or if the folder is in use.\n\nError: {authEx.Message}");
            }
            catch (IOException ioEx)
            {
                Debug.WriteLine($"[ExecuteDeleteModAsync] IO error deleting '{mod.Path}': {ioEx.Message}");
                _dialogService.ShowError("Deletion Error", $"Could not delete mod '{mod.Name}'. The folder might be open or in use by another process.\n\nError: {ioEx.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExecuteDeleteModAsync] General error deleting '{mod.Path}': {ex}");
                _dialogService.ShowError("Deletion Error", $"An unexpected error occurred while deleting mod '{mod.Name}'.\n\nError: {ex.Message}");
            }
            finally
            {
                // Regardless of deletion success/failure, attempt to refresh the list.
                // The refresh will pick up the change if deletion succeeded.
                if (deletionSuccess)
                {
                    Debug.WriteLine("[ExecuteDeleteModAsync] Deletion successful, initiating mod list refresh.");
                    await RefreshDataAsync(); // RefreshDataAsync handles setting IsLoading = false
                }
                else
                {
                    // If deletion failed, still reset loading state here
                    IsLoading = false;
                    CommandManager.InvalidateRequerySuggested();
                    Debug.WriteLine("[ExecuteDeleteModAsync] Deletion failed, resetting loading state without full refresh.");
                }
            }
        }

        private bool CanExecuteActivateDeactivate(ModItem mod)
        {
            return mod != null && !_isLoading;
        }



        private void CheckForDuplicates()
        {
            var allMods = _modListManager.GetAllMods().ToList();
            var duplicateGroups = allMods
                .GroupBy(m => m.PackageId?.ToLowerInvariant() ?? string.Empty)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicateGroups.Any())
            {
                var dialog = new DuplicateModDialogViewModel(
                    duplicateGroups,
                    pathsToDelete => DeleteDuplicateMods(pathsToDelete),
                    () => { /* Cancel callback */ });

                // Show the dialog directly instead of showing an information message first
                var view = new DuplicateModDialogView(dialog)
                {
                    Owner = Application.Current.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                view.ShowDialog();
            }
            else
            {
                _dialogService.ShowInformation("No duplicate mods found", "No duplicates");
            }
        }

        private async void DeleteDuplicateMods(List<string> pathsToDelete) // Make async void for refresh call
        {
            // --- Enhanced Debugging START ---
            if (pathsToDelete == null)
            {
                Debug.WriteLine("[DeleteDuplicateMods] ERROR: Received a null list.");
                _dialogService.ShowWarning("Deletion Warning", "Cannot perform deletion: received a null list of paths.");
                return;
            }
            Debug.WriteLine($"[DeleteDuplicateMods] Received list with {pathsToDelete.Count} items.");
            // ... (rest of existing debug logs) ...
            // --- Enhanced Debugging END ---

            // --- Information Gathering for Report ---
            var successfullyDeletedMods = new List<ModItem>(); // Store info of mods actually deleted
            var errorMessages = new List<string>(); // Store specific error messages
            var allModsLookup = _modListManager.GetAllMods() // Get all mods to look up details by path
                                 .Where(m => !string.IsNullOrEmpty(m.Path))
                                 .ToDictionary(m => m.Path, m => m, StringComparer.OrdinalIgnoreCase); // Use case-insensitive for Windows paths
                                                                                                       // ----------------------------------------

            foreach (var path in pathsToDelete)
            {
                // --- Critical Null Check ---
                if (path == null)
                {
                    Debug.WriteLine("[DeleteDuplicateMods] !!! CRITICAL ERROR: Encountered a NULL path in the list during iteration !!!");
                    // _dialogService.ShowError("Deletion Error", "A null path was encountered during deletion. Skipping this entry."); // Let's collect errors instead
                    errorMessages.Add("Internal Error: Encountered a null path reference during deletion process.");
                    continue; // Skip this null entry
                }
                // --- End Critical Null Check ---

                Debug.WriteLine($"[DeleteDuplicateMods] Processing path: '{path}'");
                allModsLookup.TryGetValue(path, out ModItem modInfo); // Try to get mod info for reporting
                string modIdentifier = modInfo != null ? $"{modInfo.Name} ({modInfo.PackageId})" : $"'{path}'";

                try
                {
                    if (Directory.Exists(path))
                    {
                        Debug.WriteLine($"[DeleteDuplicateMods] Directory exists. Attempting to delete {modIdentifier}...");
                        await Task.Run(() => Directory.Delete(path, true));  // Recursive delete
                        Debug.WriteLine($"[DeleteDuplicateMods] Successfully deleted {modIdentifier}.");
                        if (modInfo != null) // Add to success list only if we know what mod it was
                        {
                            successfullyDeletedMods.Add(modInfo);
                        }
                        else
                        {
                            // If we deleted a path but couldn't find its mod info (unlikely but possible), log it
                            Debug.WriteLine($"[DeleteDuplicateMods] WARNING: Deleted path '{path}' but couldn't find associated ModItem info.");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[DeleteDuplicateMods] Directory does not exist or is not accessible, skipping: {modIdentifier}");
                        // Optionally add a warning if non-existence is unexpected
                        // errorMessages.Add($"Could not delete {modIdentifier}: Path not found or inaccessible.");
                    }
                }
                catch (IOException ioEx)
                {
                    Debug.WriteLine($"[DeleteDuplicateMods] IO Error deleting {modIdentifier}: {ioEx.Message}");
                    errorMessages.Add($"I/O Error deleting {modIdentifier}: {ioEx.Message}");
                }
                catch (UnauthorizedAccessException authEx)
                {
                    Debug.WriteLine($"[DeleteDuplicateMods] Access Error deleting {modIdentifier}: {authEx.Message}");
                    errorMessages.Add($"Access Error deleting {modIdentifier}: {authEx.Message}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DeleteDuplicateMods] General Error deleting {modIdentifier}: {ex}");
                    errorMessages.Add($"Error deleting {modIdentifier}: {ex.Message}");
                }
            }

            Debug.WriteLine($"[DeleteDuplicateMods] Finished deletion attempts. Successes: {successfullyDeletedMods.Count}, Errors: {errorMessages.Count}");

            // --- Generate and Show Report ---
            var reportTitle = errorMessages.Any() ? "Deletion Complete with Errors" : "Deletion Complete";
            var reportMessage = new StringBuilder();
            var dialogType = MessageDialogType.Information; // Default to Information

            if (successfullyDeletedMods.Any())
            {
                reportMessage.AppendLine($"Successfully deleted {successfullyDeletedMods.Count} duplicate mod(s):");
                foreach (var mod in successfullyDeletedMods)
                {
                    reportMessage.AppendLine($"  - {mod.Name ?? "Unknown Name"}");
                    reportMessage.AppendLine($"    ID: {mod.PackageId ?? "N/A"}, Supported Versions: {GetSupportedVersionsString(mod)}, SteamID: {mod.SteamId ?? "N/A"}");
                    // reportMessage.AppendLine($"    Path: {mod.Path}"); // Optional: include path if helpful
                }
                reportMessage.AppendLine(); // Add a blank line for separation
            }
            else
            {
                // Only report "no items deleted" if there were also no errors.
                // If there were errors, the error section will explain why nothing might have been deleted.
                if (!errorMessages.Any())
                {
                    reportMessage.AppendLine("No duplicate mods were deleted.");
                    reportMessage.AppendLine();
                }
            }

            if (errorMessages.Any())
            {
                dialogType = MessageDialogType.Warning; // Change dialog type if errors occurred
                reportMessage.AppendLine("Errors encountered during deletion:");
                foreach (var error in errorMessages)
                {
                    reportMessage.AppendLine($"  - {error}");
                }
                reportMessage.AppendLine();
            }

            // Use RunOnUIThread as _dialogService might need it, although it often handles it internally.
            // Also, the refresh needs to happen after the dialog is closed.
            RunOnUIThread(() =>
            {
                _dialogService.ShowMessageWithCopy(reportTitle, reportMessage.ToString().Trim(), dialogType);

                // --- Refresh the mod list AFTER the dialog is closed ---
                // Use _ = to fire-and-forget the async refresh operation.
                // Ensure IsLoading states are handled within RefreshDataAsync.
                _ = RefreshDataAsync();
                Debug.WriteLine("[DeleteDuplicateMods] Mod list refresh initiated after dialog.");
            });
        }

        private string GetSupportedVersionsString(ModItem mod)
        {
            if (mod.SupportedVersions == null || !mod.SupportedVersions.Any())
                return "N/A";
            return string.Join(", ", mod.SupportedVersions);
        }



        private async Task ExecuteResolveDependencies()
        {
            IsLoading = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                var result = await Task.Run(() => _modListManager.ResolveDependencies());
                var (addedMods, missingDependencies) = result;

                var message = new System.Text.StringBuilder();

                // Show added dependencies if any
                if (addedMods.Count > 0)
                {
                    HasUnsavedChanges = true;
                    message.AppendLine("The following dependencies were automatically added:");
                    message.AppendLine();
                    foreach (var mod in addedMods)
                    {
                        message.AppendLine($"- {mod.Name} ({mod.PackageId})");
                    }
                    message.AppendLine();
                }

                // Show missing dependencies if any
                if (missingDependencies.Count > 0)
                {
                    message.AppendLine("The following dependencies are missing:");
                    message.AppendLine();
                    foreach (var dep in missingDependencies)
                    {
                        message.AppendLine($"- {dep.displayName} ({dep.packageId})");
                        message.AppendLine($"  Required by: {string.Join(", ", dep.requiredBy)}");
                        if (!string.IsNullOrEmpty(dep.steamUrl))
                        {
                            message.AppendLine($"  Workshop URL: {dep.steamUrl}");
                        }
                        message.AppendLine();
                    }
                }

                // Show appropriate message based on results
                if (message.Length == 0)
                {
                    RunOnUIThread(() =>
                    {
                        _dialogService.ShowInformation("Dependencies Check",
                            "No missing dependencies found and no new dependencies were added.");
                        CommandManager.InvalidateRequerySuggested();
                    });
                }
                else if (missingDependencies.Count == 0)
                {
                    RunOnUIThread(() =>
                    {
                        _dialogService.ShowInformation("Dependencies Added",
                            message.ToString().TrimEnd());
                        CommandManager.InvalidateRequerySuggested();
                    });
                }
                else
                {
                    RunOnUIThread(() =>
                    {
                        _dialogService.ShowMessageWithCopy("Dependencies Status",
                            message.ToString().TrimEnd(),
                            MessageDialogType.Warning);
                        CommandManager.InvalidateRequerySuggested();
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error resolving dependencies: {ex}");
                RunOnUIThread(() =>
                {
                    _dialogService.ShowError("Resolution Error",
                        $"Error resolving dependencies: {ex.Message}");
                    CommandManager.InvalidateRequerySuggested();
                });
            }
            finally
            {
                IsLoading = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }


        private async Task ExecuteCheckIncompatibilities()
        {
            IsLoading = true;
            CommandManager.InvalidateRequerySuggested(); // Disable buttons during processing
            try
            {
                // Get current active mods from the manager
                var activeMods = _modListManager.VirtualActiveMods.Select(entry => entry.Mod).ToList();

                // Find incompatibilities - wrap in Task.Run since it may be CPU-intensive
                var incompatibilities = await Task.Run(() => _incompatibilityService.FindIncompatibilities(activeMods));

                if (incompatibilities.Count == 0)
                {
                    RunOnUIThread(() =>
                        _dialogService.ShowInformation("Compatibility Check", "No incompatibilities found between active mods.")
                    );
                    return;
                }

                // Group incompatibilities for resolution - may also be CPU-intensive
                var groups = await Task.Run(() => _incompatibilityService.GroupIncompatibilities(incompatibilities));

                if (groups.Count == 0)
                {
                    RunOnUIThread(() =>
                        _dialogService.ShowInformation("Compatibility Check", "No incompatibility groups to resolve.")
                    );
                    return;
                }

                // Open the resolution dialog on the UI thread
                RunOnUIThread(() => ShowIncompatibilityDialog(groups));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking mod incompatibilities: {ex}");
                RunOnUIThread(() =>
                    _dialogService.ShowError("Compatibility Error", $"Error checking mod incompatibilities: {ex.Message}")
                );
            }
            finally
            {
                IsLoading = false;
                CommandManager.InvalidateRequerySuggested(); // Re-enable buttons
            }
        }


        // Add method to show the incompatibility dialog
        private void ShowIncompatibilityDialog(List<IncompatibilityGroup> groups)
        {
            // Create the dialog view model (this part is fine)
            var dialogViewModel = new ModIncompatibilityDialogViewModel(
                groups,
                ApplyIncompatibilityResolutions,
                () => { /* Cancel handler if needed */ }
            );

            // Instantiate the View using the constructor that takes the ViewModel
            var dialog = new ModIncompatibilityDialogView(dialogViewModel)
            {
                // DataContext is now set by the constructor, so no need to set it here again.
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            dialog.ShowDialog(); // Show modally
        }


        // Add method to handle applying resolutions
        private void ApplyIncompatibilityResolutions(List<ModItem> modsToRemove)
        {
            if (modsToRemove == null || modsToRemove.Count == 0)
                return;

            try
            {
                // Deactivate the mods that should be removed
                foreach (var mod in modsToRemove)
                {
                    _modListManager.DeactivateMod(mod);
                }

                // Set the unsaved changes flag
                HasUnsavedChanges = true;

                //MessageBox.Show($"Successfully resolved incompatibilities by deactivating {modsToRemove.Count} mods.",
                //               "Incompatibilities Resolved",
                //              MessageBoxButton.OK,
                //               MessageBoxImage.Information);
                _dialogService.ShowInformation("Incompatibilities Resolved", $"Successfully resolved incompatibilities by deactivating {modsToRemove.Count} mods.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying incompatibility resolutions: {ex}");
                //MessageBox.Show($"Error applying incompatibility resolutions: {ex.Message}",
                //               "Resolution Error",
                //                MessageBoxButton.OK,
                //               MessageBoxImage.Error);
                _dialogService.ShowError("Resolution Error", $"Error applying incompatibility resolutions: {ex.Message}");
            }
        }


        private void OnModListManagerChanged(object sender, EventArgs e)
        {
            // The manager's state changed, update the filter service's source data
            // This will cause the filter service to update its ObservableCollections
            _filterService.UpdateCollections(
                _modListManager.VirtualActiveMods,
                _modListManager.AllInactiveMods
            );

            // Update properties that depend on the counts (these are bound to filter service counts)
            OnPropertyChanged(nameof(TotalActiveMods));
            OnPropertyChanged(nameof(TotalInactiveMods));

            // --- CHANGE HERE ---
            // Set HasUnsavedChanges whenever the list changes via the manager.
            // The initial load case is handled by explicitly setting it to false
            // at the end of LoadDataAsync.
            HasUnsavedChanges = true;
            // -------------------

            // Re-evaluate command states
            RunOnUIThread(() =>
            {
                CommandManager.InvalidateRequerySuggested();
                // Explicitly update CanExecute for delete command as its conditions might change
                // (e.g., a mod becomes active/inactive, though path existence is the main external factor)
                (DeleteModCommand as RelayCommand<ModItem>)?.RaiseCanExecuteChanged();
            });

            Debug.WriteLine("ModsViewModel handled ModListManager ListChanged event.");
        }


        private void SelectMod(object parameter)
        {
            if (parameter is ModItem mod) SelectedMod = mod;
        }

        private async Task ExecuteDropMod(DropModArgs args)
        {
            // Assume CommandService handles interaction with ModListManager
            await _commandService.HandleDropCommand(args);
            // CommandService should ideally trigger ListChanged via manager, setting HasUnsavedChanges
            // If not, set it here: HasUnsavedChanges = true;
        }

        private async Task ExecuteClearActiveList()
        {
            // Assume CommandService handles interaction with ModListManager
            await _commandService.ClearActiveModsAsync();
            // CommandService should trigger ListChanged via manager, setting HasUnsavedChanges
            // If not, set it here: HasUnsavedChanges = true;
        }
        private async Task ExecuteSortActiveList()
        {
            IsLoading = true; // Indicate activity
            CommandManager.InvalidateRequerySuggested(); // Disable buttons during sort
            try
            {
                // Directly await the async method instead of wrapping it in Task.Run()
                await _commandService.SortActiveModsAsync();

                // The HasUnsavedChanges flag is set within the OnModListManagerChanged event handler
                // based on the ListChanged event triggered by the sort operation within the command service.
                Debug.WriteLine("Sort operation completed.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sorting mods: {ex}");
                RunOnUIThread(() =>
                    _dialogService.ShowError("Sort Error", $"Error sorting mods: {ex.Message}")
                );
            }
            finally
            {
                IsLoading = false;
                CommandManager.InvalidateRequerySuggested(); // Re-enable buttons
            }
        }

        private void ExecuteSaveMods()
        {
            IsLoading = true; // Indicate activity
            try
            {
                // --- CORRECTION: Save the state from the ModListManager ---
                var activeIdsToSave = _modListManager.VirtualActiveMods
                                        .Select(entry => entry.Mod.PackageId)
                                        .Where(id => !string.IsNullOrEmpty(id)) // Ensure no null/empty IDs
                                        .ToList();

                _dataService.SaveActiveModIdsToConfig(activeIdsToSave);
                // -----------------------------------------------------------
                HasUnsavedChanges = false;
                Debug.WriteLine("Mod list saved successfully.");
                //RunOnUIThread(() => _dialogService.ShowInformation("Save Successful", "Mod list saved successfully."));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving mods config: {ex}");
                RunOnUIThread(() =>
                                   // --- REPLACE MessageBox ---
                                   // MessageBox.Show($"Failed to save mod list: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxError));
                                   _dialogService.ShowError("Save Error", $"Failed to save mod list: {ex.Message}")
                               // -------------------------
                               );                // Optionally leave HasUnsavedChanges = true if save failed?
            }
            finally
            {
                IsLoading = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task ExecuteImport()
        {
            // Assume IOService interacts with ModListManager appropriately
            await _ioService.ImportModListAsync();
            // IOService should trigger ListChanged via manager, setting HasUnsavedChanges
            // If not, set it here: HasUnsavedChanges = true;
        }
        private async Task ExecuteExport()
        {
            // --- CORRECTION: Export the definitive list from the ModListManager ---
            var activeModsToExport = _modListManager.VirtualActiveMods.Select(entry => entry.Mod).ToList();
            if (!activeModsToExport.Any())
            {
                // MessageBox.Show("There are no active mods to export.", "Export List", MessageBoxButton.OK, MessageBoxImage.Information);
                _dialogService.ShowInformation("Export List", "There are no active mods to export.");
                return;
            }
            await _ioService.ExportModListAsync(activeModsToExport);
            // --------------------------------------------------------------------
        }

        private void OpenUrl(object parameter)
        {
            string target = null;
            if (parameter is string urlParam) // Use specific type check
            {
                target = urlParam;
            }
            else if (SelectedMod != null)
            {
                // Prioritize the explicit Url field from About.xml if present
                target = SelectedMod.Url;

                // CORRECTED: Use 'Path' instead of 'DirectoryPath'
                // If Url is empty, fall back to the local installation Path
                // Also check if the Path property itself is valid (not null or whitespace)
                if (string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(SelectedMod.Path))
                {
                    target = SelectedMod.Path; // Assign the local Path
                }
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                Debug.WriteLine("OpenUrl: No target specified or derived.");
                // MessageBox.Show("No URL or local path available for the selected mod.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                _dialogService.ShowInformation("Information", "No URL or local path available for the selected mod.");
                return;
            }

            try
            {
                // Use Directory.Exists for checking local paths robustly
                // Need System.IO namespace for Path and Directory
                if (Directory.Exists(target)) // Check if the target *is* an existing directory
                {
                    string fullPath = Path.GetFullPath(target); // Ensure canonical path
                                                                // Option 1: Open the folder in Explorer
                    ProcessStartInfo psi = new ProcessStartInfo(fullPath) { UseShellExecute = true };
                    // Option 2: Select the folder in Explorer (shows containing folder)
                    // ProcessStartInfo psi = new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"");
                    Process.Start(psi);
                }
                // Optional: Add check for file paths if 'target' could be a file
                // else if (File.Exists(target)) { ... Process.Start($"explorer.exe /select,\"{target}\""); ... }
                else // Assume it's a URL if not a known local directory/file
                {
                    // Ensure it looks like a URL
                    var uriString = target;
                    if (!uriString.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !uriString.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        // Simple check: if it contains common URL characters or top-level domains, prepend http://
                        // This is a basic heuristic and might not cover all cases perfectly.
                        if (uriString.Contains('.') && !uriString.Contains(" ") && !uriString.Contains("\\")) // Basic check if it might be a domain
                        {
                            uriString = "http://" + uriString;
                        }
                        else
                        {
                            // If it doesn't look like a URL and isn't a directory, show an error
                            throw new InvalidOperationException($"Target '{target}' is not a recognized directory or URL format.");
                        }
                    }

                    Process.Start(new ProcessStartInfo(uriString) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not open path/URL '{target}': {ex}");
                // MessageBox.Show($"Could not open path/URL: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _dialogService.ShowError("Error", $"Could not open path/URL: {ex.Message}");
            }
        }

        // Stubbed methods
        /*
        private void ExecuteFilterInactive(object parameter) => MessageBox.Show("Filter Inactive Mods - Not Yet Implemented");
        private void ExecuteFilterActive(object parameter) => MessageBox.Show("Filter Active Mods - Not Yet Implemented");
        private void StripMods(object parameter) => MessageBox.Show("Strip mods: Functionality not yet implemented.");
        private void CreatePack(object parameter) => MessageBox.Show("Create pack: Functionality not yet implemented.");
        private void FixIntegrity(object parameter) => MessageBox.Show("Fix integrity: Functionality not yet implemented.");
        private void RunGame(object parameter) => MessageBox.Show("Run game: Functionality not yet implemented.");
*/
        private void ExecuteFilterInactive(object parameter) => _dialogService.ShowInformation("Not Implemented", "Filter Inactive Mods - Not Yet Implemented");
        private void ExecuteFilterActive(object parameter) => _dialogService.ShowInformation("Not Implemented", "Filter Active Mods - Not Yet Implemented");
        private void StripMods(object parameter) => _dialogService.ShowInformation("Not Implemented", "Strip mods: Functionality not yet implemented.");
        private void CreatePack(object parameter) => _dialogService.ShowInformation("Not Implemented", "Create pack: Functionality not yet implemented.");
        private void FixIntegrity(object parameter) => _dialogService.ShowInformation("Not Implemented", "Fix integrity: Functionality not yet implemented.");
        private void RunGame(object parameter) => _dialogService.ShowInformation("Not Implemented", "Run game: Functionality not yet implemented.");

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _modListManager.ListChanged -= OnModListManagerChanged; // Use correct handler name
            }
        }


        ~ModsViewModel()
        {
            Dispose(false);
        }
    }
}
