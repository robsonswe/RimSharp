using RimSharp.Models;
using RimSharp.Services;
using RimSharp.Handlers;
using RimSharp.ViewModels.Modules.Mods.Management; // Added namespace
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Collections.Generic;
using System.Xml.Linq;
using System.IO;
using System;
using System.Windows;
using System.Diagnostics;

public class DropModArgs
{
    public ModItem DroppedItem { get; set; }
    public string TargetListName { get; set; }
    public int DropIndex { get; set; }
}

namespace RimSharp.ViewModels.Modules.Mods
{
    public class ModsViewModel : ViewModelBase, IDisposable // Implement IDisposable if subscribing to events
    {
        private readonly IModService _modService;
        private readonly IPathService _pathService;
        private readonly IModListManager _modListManager; // Injected dependency

        private ModItem _selectedMod;
        private bool _isLoading;
        private string _activeSearchText = "";
        private string _inactiveSearchText = "";
        private bool _hasUnsavedChanges;

        // ObservableCollections remain for UI binding
        public ObservableCollection<ModItem> ActiveMods { get; } = new();
        public ObservableCollection<ModItem> InactiveMods { get; } = new();

        // Properties bound to UI (Counts are derived now)
        public int TotalActiveMods => ActiveMods.Count; // Or get from _modListManager if filtering is complex
        public int TotalInactiveMods => InactiveMods.Count; // Or get from _modListManager

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
            get => _activeSearchText;
            set
            {
                if (SetProperty(ref _activeSearchText, value))
                {
                    FilterAndUpdateActiveMods(); // Update filtering method name
                }
            }
        }

        public async Task RefreshDataAsync()
        {
            await LoadDataAsync();
        }

        public string InactiveSearchText
        {
            get => _inactiveSearchText;
            set
            {
                if (SetProperty(ref _inactiveSearchText, value))
                {
                    FilterAndUpdateInactiveMods(); // Update filtering method name
                }
            }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set => SetProperty(ref _hasUnsavedChanges, value);
        }


        // Commands (Declaration remains similar)
        public ICommand SelectModCommand { get; }
        public ICommand ClearActiveListCommand { get; }
        public ICommand SortActiveListCommand { get; }
        public ICommand StripModsCommand { get; } // Keep stubbed commands here
        public ICommand CreatePackCommand { get; }
        public ICommand FixIntegrityCommand { get; }
        public ICommand ImportListCommand { get; }
        public ICommand ExportListCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand RunGameCommand { get; }
        public ICommand OpenUrlCommand { get; }
        public ICommand FilterInactiveCommand { get; }
        public ICommand FilterActiveCommand { get; }
        public ICommand ActivateModCommand { get; }
        public ICommand DeactivateModCommand { get; }
        public ICommand DropModCommand { get; }


        // Constructor - Inject IModListManager
        public ModsViewModel(IModService modService, IPathService pathService, IModListManager modListManager)
        {
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _modListManager = modListManager ?? throw new ArgumentNullException(nameof(modListManager));

            // Subscribe to changes from the manager
            _modListManager.ListChanged += OnModListChanged;

            // Initialize Commands
            SelectModCommand = new RelayCommand(SelectMod);
            ClearActiveListCommand = new RelayCommand(ExecuteClearActiveList); // Rename methods
            SortActiveListCommand = new RelayCommand(ExecuteSortActiveList);
            SaveCommand = new RelayCommand(ExecuteSaveMods);
            DropModCommand = new RelayCommand<DropModArgs>(ExecuteDropMod);
            ActivateModCommand = new RelayCommand<ModItem>(mod => _modListManager.ActivateMod(mod)); // Delegate directly
            DeactivateModCommand = new RelayCommand<ModItem>(mod => _modListManager.DeactivateMod(mod)); // Delegate directly
            OpenUrlCommand = new RelayCommand(OpenUrl);

            // Stubbed/Placeholder commands
            StripModsCommand = new RelayCommand(StripMods);
            CreatePackCommand = new RelayCommand(CreatePack);
            FixIntegrityCommand = new RelayCommand(FixIntegrity);
            ImportListCommand = new RelayCommand(ImportList);
            ExportListCommand = new RelayCommand(ExportList);
            RunGameCommand = new RelayCommand(RunGame);
            FilterInactiveCommand = new RelayCommand(ExecuteFilterInactive);
            FilterActiveCommand = new RelayCommand(ExecuteFilterActive);


            // Load data asynchronously
            _ = LoadDataAsync(); // Use discard _ for fire-and-forget async void pattern in constructor
        }

        // --- Data Loading and Initialization ---
        private async Task LoadDataAsync()
        {
            IsLoading = true;
            try
            {
                // Clear UI lists immediately
                ActiveMods.Clear();
                InactiveMods.Clear();
                OnPropertyChanged(nameof(TotalActiveMods));
                OnPropertyChanged(nameof(TotalInactiveMods));

                await _modService.LoadModsAsync();

                var allMods = _modService.GetLoadedMods().ToList();
                var activeIdsFromConfig = GetActiveModsFromConfig();

                // Initialize the manager with the loaded data
                _modListManager.Initialize(allMods, activeIdsFromConfig);

                // The OnModListChanged handler will populate the collections

                SelectedMod = ActiveMods.FirstOrDefault() ?? InactiveMods.FirstOrDefault();
                HasUnsavedChanges = false; // Initial load doesn't count as unsaved change

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading mods: {ex.Message}", "Loading Error", MessageBoxButton.OK, MessageBoxImage.Error);
                HasUnsavedChanges = false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        // Helper to read config (remains in VM as it uses PathService)
        private List<string> GetActiveModsFromConfig()
        {
            try
            {
                var configPath = Path.Combine(_pathService.GetConfigPath(), "ModsConfig.xml");
                if (!File.Exists(configPath)) return new List<string>();

                var doc = XDocument.Load(configPath);
                return doc.Root?.Element("activeMods")?.Elements("li")
                    .Select(x => x.Value.ToLowerInvariant()) // Already normalized
                    .ToList() ?? new List<string>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading ModsConfig.xml: {ex.Message}");
                // Consider showing a warning to the user here as well
                MessageBox.Show($"Warning: Could not read active mods from ModsConfig.xml.\nReason: {ex.Message}\nStarting with an empty active list.",
                               "Config Read Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return new List<string>();
            }
        }

        // --- Event Handler for Manager Changes ---
        private void OnModListChanged(object sender, EventArgs e)
        {
            // Update the ObservableCollections based on the manager's current state
            FilterAndUpdateActiveMods();
            FilterAndUpdateInactiveMods();

            // Set unsaved changes flag (unless it was the initial load, handled in LoadDataAsync)
            // We might need a flag to ignore the first event if LoadDataAsync triggers it implicitly
            // Or, set HasUnsavedChanges = true only within command handlers that call manager methods. Let's do that.
            // HasUnsavedChanges = true; // Set this in the command handlers instead
        }

        // --- Filtering and UI Update ---

        private void FilterAndUpdateActiveMods()
        {
            var previouslySelected = SelectedMod; // Preserve selection

            ActiveMods.Clear(); // Clear the UI list

            // Get the source list from the manager
            var sourceMods = _modListManager.VirtualActiveMods;

            // Apply search filter
            var filteredMods = string.IsNullOrWhiteSpace(_activeSearchText)
                ? sourceMods
                : sourceMods.Where(x => x.Mod.Name.Contains(_activeSearchText, StringComparison.OrdinalIgnoreCase));

            // Add filtered and ordered mods to the ObservableCollection
            foreach (var (mod, _) in filteredMods.OrderBy(x => x.LoadOrder))
            {
                ActiveMods.Add(mod);
            }

            OnPropertyChanged(nameof(TotalActiveMods)); // Update count property

            // Restore selection if possible
            if (ActiveMods.Contains(previouslySelected))
            {
                SelectedMod = previouslySelected;
            }
            // Don't reset selection if not found, it might be in the other list
        }

        private void FilterAndUpdateInactiveMods()
        {
            var previouslySelected = SelectedMod;

            InactiveMods.Clear();

            // Get source from manager
            var sourceMods = _modListManager.AllInactiveMods;

            // Apply search filter (inactive list is already sorted alphabetically by manager)
            var filteredMods = string.IsNullOrWhiteSpace(_inactiveSearchText)
                ? sourceMods
                : sourceMods.Where(m => m.Name.Contains(_inactiveSearchText, System.StringComparison.OrdinalIgnoreCase));
            // .OrderBy(m => m.Name); // Manager should already keep it sorted

            foreach (var mod in filteredMods)
            {
                InactiveMods.Add(mod);
            }

            OnPropertyChanged(nameof(TotalInactiveMods)); // Update count property

            if (InactiveMods.Contains(previouslySelected))
            {
                SelectedMod = previouslySelected;
            }
        }


        // --- Command Implementations (Delegating or Simple) ---

        private void SelectMod(object parameter)
        {
            if (parameter is ModItem mod) SelectedMod = mod;
        }

        private void ExecuteDropMod(DropModArgs args)
        {
            if (args?.DroppedItem == null || string.IsNullOrEmpty(args.TargetListName)) return;

            ModItem draggedMod = args.DroppedItem;
            int dropIndex = args.DropIndex;
            string targetList = args.TargetListName;
            bool changeMade = false;

            Debug.WriteLine($"Drop executed: Item '{draggedMod.Name}', Target: {targetList}, Index: {dropIndex}");

            if (targetList.Equals("Active", StringComparison.OrdinalIgnoreCase))
            {
                // Check if the mod is currently active using the manager's list
                bool isCurrentlyActive = _modListManager.VirtualActiveMods.Any(x => x.Mod == draggedMod);

                if (isCurrentlyActive)
                {
                    // Reorder within active list
                    Debug.WriteLine($"Reordering active mod '{draggedMod.Name}' to index {dropIndex}");
                    _modListManager.ReorderMod(draggedMod, dropIndex); // Manager handles logic
                    changeMade = true; // Assuming ReorderMod indicates change via event or return value if needed
                }
                else
                {
                    // Activate at specific position
                    Debug.WriteLine($"Adding inactive mod '{draggedMod.Name}' to active list at index {dropIndex}");
                    _modListManager.ActivateModAt(draggedMod, dropIndex);
                    changeMade = true;
                }
            }
            else if (targetList.Equals("Inactive", StringComparison.OrdinalIgnoreCase))
            {
                // Check if the mod is currently active using the manager's list
                bool isCurrentlyActive = _modListManager.VirtualActiveMods.Any(x => x.Mod == draggedMod);

                if (isCurrentlyActive)
                {
                    // Deactivate
                    Debug.WriteLine($"Removing active mod '{draggedMod.Name}' (dropped onto inactive)");
                    _modListManager.DeactivateMod(draggedMod);
                    changeMade = true;
                }
                else
                {
                    // Dropping inactive onto inactive = no-op
                    Debug.WriteLine($"Mod '{draggedMod.Name}' dropped onto inactive list, but it was already inactive. No action taken.");
                }
            }
            else { Debug.WriteLine($"Drop target list name '{targetList}' not recognized."); }

            if (changeMade) HasUnsavedChanges = true;

            // Selection update (optional, UI might handle this via binding update)
            // Check BOTH lists after the potential change
            if (ActiveMods.Contains(draggedMod)) SelectedMod = draggedMod;
            else if (InactiveMods.Contains(draggedMod)) SelectedMod = draggedMod;
        }


        private void ExecuteClearActiveList(object parameter)
        {
            // Confirmation dialog? Recommended for destructive actions.
            var result = MessageBox.Show("This will remove all non-Core and non-Expansion mods from the active list.\nAre you sure?",
                                        "Confirm Clear", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            _modListManager.ClearActiveList(); // Delegate to manager
            HasUnsavedChanges = true; // Mark change
                                      // Event handler updates UI collections
            SelectedMod = ActiveMods.FirstOrDefault(); // Select first remaining active mod
        }


        private void ExecuteSortActiveList(object parameter)
        {
            bool orderChanged = _modListManager.SortActiveList(); // Delegate to manager

            if (orderChanged)
            {
                HasUnsavedChanges = true; // Mark change
                MessageBox.Show("Active mods sorted based on defined rules.", "Sort Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                SelectedMod = ActiveMods.FirstOrDefault(); // Reselect first after sort
            }
            else
            {
                // Check if sorting failed (e.g., cycle detected) - manager might need to expose status
                // For now, assume failure means no change or message shown by manager internally/logged
                MessageBox.Show("Mods are already correctly sorted or a sorting error occurred (check logs for cycles).",
                               "Sort Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            // Event handler updates UI collections
        }


        private void ExecuteSaveMods(object parameter)
        {
            try
            {
                var configDir = _pathService.GetConfigPath();
                if (string.IsNullOrEmpty(configDir) || !Directory.Exists(configDir))
                {
                    MessageBox.Show("Error: Config directory path is not set or invalid.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var configPath = Path.Combine(configDir, "ModsConfig.xml");
                XDocument doc;
                bool fileExisted = File.Exists(configPath);

                // Load or Create XML Document Logic (same as before)
                if (!fileExisted)
                { /* Create new doc */
                    doc = new XDocument(new XElement("ModsConfigData", new XElement("version", "1.0"), new XElement("activeMods")));
                }
                else
                {
                    try { doc = XDocument.Load(configPath); }
                    catch (Exception loadEx)
                    {
                        // Ask to overwrite if corrupted etc. (same logic as before)
                        var result = MessageBox.Show($"Error loading existing ModsConfig.xml: {loadEx.Message}\n\nOverwrite with the current active mod list?", "Load Error", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (result == MessageBoxResult.Yes)
                        {
                            doc = new XDocument(new XElement("ModsConfigData", new XElement("version", "1.0"), new XElement("activeMods")));
                            fileExisted = false;
                        }
                        else { return; } // Abort
                    }
                }
                // Find/Create activeMods element (same logic as before)
                var activeModsElement = doc.Root?.Element("activeMods");
                if (activeModsElement == null)
                { /* Try to create it or error */
                    if (doc.Root != null)
                    {
                        activeModsElement = new XElement("activeMods");
                        doc.Root.Add(activeModsElement);
                    }
                    else
                    {
                        MessageBox.Show("Error: ModsConfig.xml structure invalid.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error); return;
                    }
                }

                // --- Get active mods from MANAGER ---
                activeModsElement.RemoveAll(); // Clear existing
                foreach (var (mod, _) in _modListManager.VirtualActiveMods.OrderBy(x => x.LoadOrder)) // Use manager's list
                {
                    if (!string.IsNullOrEmpty(mod.PackageId))
                    {
                        activeModsElement.Add(new XElement("li", mod.PackageId.ToLowerInvariant()));
                    }
                    else
                    {
                        Debug.WriteLine($"Warning: Mod '{mod.Name}' has no PackageId and was not saved.");
                    }
                }

                // Save the document
                doc.Save(configPath);
                HasUnsavedChanges = false; // Reset flag on successful save!

                // Show confirmation message (same as before)
                MessageBox.Show($"Mods configuration saved successfully{(fileExisted ? "!" : " to new file")}!", "Save Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show($"Error: Permission denied saving to {Path.Combine(_pathService.GetConfigPath(), "ModsConfig.xml")}.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An unexpected error occurred saving mods configuration: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // --- Other Commands (Remain largely the same or are simple) ---

        private void OpenUrl(object parameter)
        {
            if (parameter is string url && !string.IsNullOrWhiteSpace(url))
            { /* ... Process.Start logic ... */
                try
                {
                    var uri = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                        ? new Uri(url)
                        : new Uri("http://" + url);
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch (Exception ex) { MessageBox.Show($"Could not open URL: {ex.Message}"); }
            }
        }

        private void ExecuteFilterInactive(object parameter) => MessageBox.Show("Filter Inactive Mods - Not Yet Implemented");
        private void ExecuteFilterActive(object parameter) => MessageBox.Show("Filter Active Mods - Not Yet Implemented");
        private void StripMods(object parameter) => MessageBox.Show("Strip mods: Functionality not yet implemented.");
        private void CreatePack(object parameter) => MessageBox.Show("Create pack: Functionality not yet implemented.");
        private void FixIntegrity(object parameter) => MessageBox.Show("Fix integrity: Functionality not yet implemented.");
        private void ImportList(object parameter) => MessageBox.Show("Import list: Functionality not yet implemented.");
        private void ExportList(object parameter) => MessageBox.Show("Export list: Functionality not yet implemented.");
        private void RunGame(object parameter) => MessageBox.Show("Run game: Functionality not yet implemented.");

        // --- IDisposable for event unsubscription ---
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Unsubscribe from the manager's event
                if (_modListManager != null)
                {
                    _modListManager.ListChanged -= OnModListChanged;
                }
            }
        }

        ~ModsViewModel()
        {
            Dispose(false);
        }
    }
}