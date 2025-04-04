using RimSharp.Handlers;
using RimSharp.Models;
using RimSharp.Services;
using RimSharp.ViewModels.Modules.Mods.Commands;
using RimSharp.ViewModels.Modules.Mods.Data;
using RimSharp.ViewModels.Modules.Mods.Filtering;
using RimSharp.ViewModels.Modules.Mods.IO;
using RimSharp.ViewModels.Modules.Mods.Management;
using RimSharp.Views.Modules.Mods.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace RimSharp.ViewModels.Modules.Mods
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

        public ICommand ResolveDependenciesCommand { get; private set; }
        public ICommand CheckIncompatibilitiesCommand { get; private set; }


        public ModsViewModel(
            IModDataService dataService,
            IModFilterService filterService,
            IModCommandService commandService,
            IModListIOService ioService,
            IModListManager modListManager,
            IModIncompatibilityService incompatibilityService,
            IDialogService dialogService)
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
            OpenUrlCommand = new RelayCommand(OpenUrl, _ => SelectedMod != null); // Example condition
            ImportListCommand = new RelayCommand(async _ => await ExecuteImport(), _ => !_isLoading);
            ExportListCommand = new RelayCommand(async _ => await ExecuteExport(), _ => !_isLoading && _modListManager.VirtualActiveMods.Any()); // Condition export has active mods
            CheckIncompatibilitiesCommand = new RelayCommand(async _ => await ExecuteCheckIncompatibilities(), _ => !_isLoading && _modListManager.VirtualActiveMods.Any());

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
            await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            IsLoading = true;
            // Consider disabling commands while loading
            CommandManager.InvalidateRequerySuggested(); // Added for consistency
            try
            {
                // Filter service collections are automatically cleared by UpdateCollections
                // ActiveMods.Clear();
                // InactiveMods.Clear();

                var allMods = await _dataService.LoadAllModsAsync();
                var activeIdsFromConfig = _dataService.LoadActiveModIdsFromConfig();

                // Manager initializes its state and fires ListChanged, which triggers OnModListManagerChanged
                _modListManager.Initialize(allMods, activeIdsFromConfig);

                // Selection logic can remain, potentially select Core mod if available
                SelectedMod = _modListManager.VirtualActiveMods.FirstOrDefault(m => m.Mod.IsCore).Mod
                             ?? _modListManager.VirtualActiveMods.FirstOrDefault().Mod
                             ?? _modListManager.AllInactiveMods.FirstOrDefault();

                // --- ENSURE THIS IS HERE ---
                // Explicitly reset flag AFTER Initialize (which might have triggered ListChanged
                // and incorrectly set HasUnsavedChanges = true via the modified handler)
                HasUnsavedChanges = false;
                // ---------------------------
            }
            catch (Exception ex)
            {
                // Log error properly
                Debug.WriteLine($"Error loading mods: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Loading Error", $"Error loading mods: {ex.Message}"));
                HasUnsavedChanges = false; // Also ensure it's false on error
            }
            finally
            {
                IsLoading = false;
                // Re-evaluate CanExecute for commands
                CommandManager.InvalidateRequerySuggested();
            }
        }


        private async Task ExecuteResolveDependencies()
        {
            IsLoading = true;
            CommandManager.InvalidateRequerySuggested(); // Disable buttons during processing
            try
            {
                // Make this operation truly asynchronous by wrapping the CPU-bound work
                var result = await Task.Run(() => _modListManager.ResolveDependencies());
                var (addedMods, missingDependencies) = result;

                if (addedMods.Count > 0)
                {
                    HasUnsavedChanges = true;
                }

                if (missingDependencies.Count > 0)
                {
                    var message = new System.Text.StringBuilder();
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

                    RunOnUIThread(() =>
                    {
                        _dialogService.ShowWarning("Missing Dependencies", message.ToString());
                        CommandManager.InvalidateRequerySuggested();
                    });
                }
                else if (addedMods.Count == 0)
                {
                    RunOnUIThread(() =>
                    {
                        _dialogService.ShowInformation("Dependencies Check", "No missing dependencies found.");
                        CommandManager.InvalidateRequerySuggested();
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error resolving dependencies: {ex}");
                RunOnUIThread(() =>
                {
                    _dialogService.ShowError("Resolution Error", $"Error resolving dependencies: {ex.Message}");
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
            RunOnUIThread(CommandManager.InvalidateRequerySuggested);

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
                RunOnUIThread(() => _dialogService.ShowInformation("Save Successful", "Mod list saved successfully."));
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
