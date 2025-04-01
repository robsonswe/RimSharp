// Update ModsViewModel.cs

using RimSharp.Models;
using RimSharp.Services;
using RimSharp.Handlers;         // Namespace for RelayCommand
using RimSharp.ViewModels;    // Namespace for ViewModelBase
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input; // Namespace for ICommand
using System.Collections.Generic; // For filtering
using System.Xml.Linq; // For XML handling
using System.IO;
using System; // For file handling
using System.Windows; // For MessageBox
using System.Diagnostics;

// Correct Namespace Declaration
namespace RimSharp.ViewModels.Modules.Mods
{
    // Correct Class Name
    public class ModsViewModel : ViewModelBase
    {
        private readonly IModService _modService;
        private readonly IPathService _pathService;

        private ModItem _selectedMod;
        private bool _isLoading;
        private string _activeSearchText = "";
        private string _inactiveSearchText = "";

        private bool _hasUnsavedChanges;

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set => SetProperty(ref _hasUnsavedChanges, value);
        }


        private List<ModItem> _allActiveMods = new(); // Derived from _virtualActiveMods
        private List<ModItem> _allInactiveMods = new();

        public int TotalActiveMods => _allActiveMods.Count;
        public int TotalInactiveMods => _allInactiveMods.Count;

        // This is the primary source of truth for active mods and their order
        private List<(ModItem Mod, int LoadOrder)> _virtualActiveMods = new();

        public ObservableCollection<ModItem> ActiveMods { get; } = new();
        public ObservableCollection<ModItem> InactiveMods { get; } = new();

        public ICommand SelectModCommand { get; }
        public ICommand ClearActiveListCommand { get; }
        public ICommand SortActiveListCommand { get; }
        public ICommand StripModsCommand { get; }
        public ICommand CreatePackCommand { get; }
        public ICommand FixIntegrityCommand { get; }
        public ICommand ImportListCommand { get; }
        public ICommand ImportSaveCommand { get; }
        public ICommand ExportListCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand RunGameCommand { get; }
        public ICommand OpenUrlCommand { get; }


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

        public ModsViewModel(IModService modService, IPathService pathService)
        {
            _modService = modService;
            _pathService = pathService;
            SelectModCommand = new RelayCommand(SelectMod);

            // Initialize new commands
            ClearActiveListCommand = new RelayCommand(ClearActiveList);
            SortActiveListCommand = new RelayCommand(SortActiveList);
            StripModsCommand = new RelayCommand(StripMods);
            CreatePackCommand = new RelayCommand(CreatePack);
            FixIntegrityCommand = new RelayCommand(FixIntegrity);
            ImportListCommand = new RelayCommand(ImportList);
            ImportSaveCommand = new RelayCommand(ImportSave);
            ExportListCommand = new RelayCommand(ExportList);
            SaveCommand = new RelayCommand(SaveMods);
            RunGameCommand = new RelayCommand(RunGame);
            OpenUrlCommand = new RelayCommand(OpenUrl);

            LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            IsLoading = true;
            try
            {
                ActiveMods.Clear();
                InactiveMods.Clear();
                _allActiveMods.Clear();
                _allInactiveMods.Clear();
                _virtualActiveMods.Clear(); // Clear the virtual list first

                await _modService.LoadModsAsync();

                var allMods = _modService.GetLoadedMods().ToList();
                var activeModsFromConfig = GetActiveModsFromConfig();

                // Build virtual active mods list with load order
                _virtualActiveMods = activeModsFromConfig
                    .Select((packageId, index) =>
                    {
                        var mod = allMods.FirstOrDefault(m =>
                            m.PackageId?.Equals(packageId, StringComparison.OrdinalIgnoreCase) == true); // Case-insensitive comparison
                        return mod != null ? (Mod: mod, LoadOrder: index) : default;
                    })
                    .Where(entry => entry.Mod != null) // Filter out any mods not found
                    .ToList();

                // Set IsActive flag for mods in the virtual list
                foreach (var (mod, _) in _virtualActiveMods)
                {
                    mod.IsActive = true;
                }

                // Separate into active and inactive lists using the virtual list as source
                _allActiveMods = _virtualActiveMods.Select(x => x.Mod).ToList();
                _allInactiveMods = allMods.Except(_allActiveMods).OrderBy(m => m.Name).ToList();

                // Notify that the totals have changed
                OnPropertyChanged(nameof(TotalActiveMods));
                OnPropertyChanged(nameof(TotalInactiveMods));

                // Populate the ObservableCollections for the UI
                FilterActiveMods();
                FilterInactiveMods();

                SelectedMod = ActiveMods.FirstOrDefault() ?? InactiveMods.FirstOrDefault();

                HasUnsavedChanges = false;

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading mods: {ex.Message}");
                HasUnsavedChanges = false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void OpenUrl(object parameter)
        {
            if (parameter is string url && !string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    // Ensure the URL has a proper scheme
                    var uri = url.StartsWith("http://") || url.StartsWith("https://")
                        ? new Uri(url)
                        : new Uri("http://" + url);

                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open URL: {ex.Message}");
                }
            }
        }

        private List<string> GetActiveModsFromConfig()
        {
            try
            {
                var configPath = Path.Combine(_pathService.GetConfigPath(), "ModsConfig.xml");
                if (!File.Exists(configPath))
                {
                    return new List<string>();
                }

                var doc = XDocument.Load(configPath);
                return doc.Root?.Element("activeMods")?.Elements("li")
                    .Select(x => x.Value.ToLowerInvariant()) // Normalize to lowercase
                    .ToList() ?? new List<string>();
            }
            catch (Exception ex)
            {
                // Log or handle the exception appropriately
                Debug.WriteLine($"Error reading ModsConfig.xml: {ex.Message}");
                return new List<string>();
            }
        }

        public string ActiveSearchText
        {
            get => _activeSearchText;
            set
            {
                if (SetProperty(ref _activeSearchText, value))
                {
                    FilterActiveMods();
                }
            }
        }

        public string InactiveSearchText
        {
            get => _inactiveSearchText;
            set
            {
                if (SetProperty(ref _inactiveSearchText, value))
                {
                    FilterInactiveMods();
                }
            }
        }

        private void FilterActiveMods()
        {
            // Update the UI list based on the _virtualActiveMods and search text
            ActiveMods.Clear();
            var filteredMods = string.IsNullOrWhiteSpace(_activeSearchText)
                ? _virtualActiveMods // Already ordered by LoadOrder
                : _virtualActiveMods.Where(x => x.Mod.Name.Contains(_activeSearchText, StringComparison.OrdinalIgnoreCase));
            // Keep original load order even when filtering

            foreach (var (mod, _) in filteredMods.OrderBy(x => x.LoadOrder)) // Ensure order is correct
            {
                ActiveMods.Add(mod);
            }
        }

        private void FilterInactiveMods()
        {
            // Update the UI list based on _allInactiveMods and search text
            InactiveMods.Clear();
            var filteredMods = string.IsNullOrWhiteSpace(_inactiveSearchText)
                ? _allInactiveMods.OrderBy(m => m.Name) // Sort inactive alphabetically
                : _allInactiveMods.Where(m => m.Name.Contains(_inactiveSearchText, System.StringComparison.OrdinalIgnoreCase))
                                   .OrderBy(m => m.Name);

            foreach (var mod in filteredMods)
            {
                InactiveMods.Add(mod);
            }
        }

        private void SelectMod(object parameter)
        {
            if (parameter is ModItem mod)
            {
                SelectedMod = mod;
            }
        }

        public Task RefreshDataAsync()
        {
            return LoadDataAsync();
        }

        // --- Updated ClearActiveList Method ---
        private void ClearActiveList(object parameter)
        {
            // Identify mods to remove (not Core or Expansion)
            var modsToRemove = _virtualActiveMods
                .Where(entry => !entry.Mod.IsCore && !entry.Mod.IsExpansion)
                .Select(entry => entry.Mod)
                .ToList();

            // If no mods to remove, exit early
            if (!modsToRemove.Any())
            {
                return;
            }

            // Identify essential mods to keep and maintain their relative order
            var modsToKeep = _virtualActiveMods
                .Where(entry => entry.Mod.IsCore || entry.Mod.IsExpansion)
                // Order by original load order to maintain relative sequence
                .OrderBy(entry => entry.LoadOrder)
                // Re-assign new load order starting from 0
                .Select((entry, newIndex) => (entry.Mod, LoadOrder: newIndex))
                .ToList();

            // Update the virtual active list
            _virtualActiveMods = modsToKeep;

            // Update the derived list of active mods
            _allActiveMods = _virtualActiveMods.Select(x => x.Mod).ToList();

            // Add the removed mods to the inactive list and update their status
            foreach (var mod in modsToRemove)
            {
                mod.IsActive = false;
                if (!_allInactiveMods.Contains(mod)) // Avoid duplicates if logic gets complex later
                {
                    _allInactiveMods.Add(mod);
                }
            }
            // Optional: Re-sort the full inactive list
            _allInactiveMods = _allInactiveMods.OrderBy(m => m.Name).ToList();


            // Notify property changes for counts
            OnPropertyChanged(nameof(TotalActiveMods));
            OnPropertyChanged(nameof(TotalInactiveMods));

            // Refresh the UI lists (ObservableCollections)
            FilterActiveMods();
            FilterInactiveMods();

            // Optional: Select the first remaining active mod or null
            SelectedMod = ActiveMods.FirstOrDefault();

            HasUnsavedChanges = true;

        }
        // --- End of Updated ClearActiveList Method ---

        public void MoveModUp(ModItem mod)
        {
            var itemIndex = _virtualActiveMods.FindIndex(x => x.Mod == mod);
            if (itemIndex > 0) // Can move up if not already at the top
            {
                // Swap load orders logically
                var itemAbove = _virtualActiveMods[itemIndex - 1];
                var currentItem = _virtualActiveMods[itemIndex];

                _virtualActiveMods[itemIndex - 1] = (itemAbove.Mod, currentItem.LoadOrder); // Item above gets current index
                _virtualActiveMods[itemIndex] = (currentItem.Mod, itemAbove.LoadOrder);   // Current item gets item above's index

                // Re-sort the list by the new LoadOrder to be sure
                _virtualActiveMods = _virtualActiveMods.OrderBy(x => x.LoadOrder).ToList();

                // Refresh the UI list
                FilterActiveMods();
                HasUnsavedChanges = true;
            }
        }


        public void MoveModDown(ModItem mod)
        {
            var itemIndex = _virtualActiveMods.FindIndex(x => x.Mod == mod);
            if (itemIndex >= 0 && itemIndex < _virtualActiveMods.Count - 1) // Can move down if not already at the bottom
            {
                // Swap load orders logically
                var itemBelow = _virtualActiveMods[itemIndex + 1];
                var currentItem = _virtualActiveMods[itemIndex];

                _virtualActiveMods[itemIndex + 1] = (itemBelow.Mod, currentItem.LoadOrder); // Item below gets current index
                _virtualActiveMods[itemIndex] = (currentItem.Mod, itemBelow.LoadOrder);   // Current item gets item below's index

                // Re-sort the list by the new LoadOrder to be sure
                _virtualActiveMods = _virtualActiveMods.OrderBy(x => x.LoadOrder).ToList();

                // Refresh the UI list
                FilterActiveMods();
                HasUnsavedChanges = true;
            }
        }

        public void AddModToActive(ModItem mod)
        {
            // Add to the end of the virtual list
            AddModToActiveAtPosition(mod, _virtualActiveMods.Count);
        }

        public void RemoveModFromActive(ModItem mod)
        {

              if (mod.IsCore)
            {
                // Optional: Log or show a message if you want feedback, but often silently failing is fine.
                Debug.WriteLine($"Attempt blocked: Cannot remove the Core mod '{mod.Name}'.");
                MessageBox.Show($"Cannot remove the Core game.");
                // Simply exit the method, doing nothing.
                return;
            }
            var itemIndex = _virtualActiveMods.FindIndex(x => x.Mod == mod);
            if (itemIndex != -1) // Mod found in the virtual active list
            {
                // Remove from virtual list
                _virtualActiveMods.RemoveAt(itemIndex);
                mod.IsActive = false;

                // Re-index remaining active mods
                for (int i = 0; i < _virtualActiveMods.Count; i++)
                {
                    var existing = _virtualActiveMods[i];
                    _virtualActiveMods[i] = (existing.Mod, i); // Assign new load order index
                }

                // Update derived active list
                _allActiveMods = _virtualActiveMods.Select(x => x.Mod).ToList();

                // Add to inactive list if not already there
                if (!_allInactiveMods.Contains(mod))
                {
                    _allInactiveMods.Add(mod);
                    _allInactiveMods = _allInactiveMods.OrderBy(m => m.Name).ToList(); // Keep inactive sorted
                }

                // Notify UI
                OnPropertyChanged(nameof(TotalActiveMods));
                OnPropertyChanged(nameof(TotalInactiveMods));
                FilterActiveMods();
                FilterInactiveMods();

                // Optionally adjust selection
                SelectedMod = ActiveMods.FirstOrDefault() ?? InactiveMods.FirstOrDefault();
                HasUnsavedChanges = true;

            }
        }

        public void ReorderActiveMod(ModItem mod, int newIndex)
        {
            if (mod == null) return;

            try
            {
                var currentItemIndex = _virtualActiveMods.FindIndex(x => x.Mod == mod);
                if (currentItemIndex == -1)
                {
                    Debug.WriteLine($"Reorder error: Mod '{mod.Name}' not found in virtual active list.");
                    return; // Mod not found
                }

                // Clamp newIndex to valid range (0 to Count, inclusive for inserting at end)
                newIndex = Math.Clamp(newIndex, 0, _virtualActiveMods.Count);

                // Determine the effective index where the item *would* be inserted
                // If moving down, the target index effectively decreases by 1 after removal
                int effectiveInsertIndex = (newIndex > currentItemIndex) ? newIndex - 1 : newIndex;
                // Clamp the effective insert index to the valid range *after removal* (0 to Count-1)
                effectiveInsertIndex = Math.Clamp(effectiveInsertIndex, 0, Math.Max(0, _virtualActiveMods.Count - 1));

                // *** Check if the effective position is actually different ***
                if (currentItemIndex != effectiveInsertIndex)
                {
                    // *** Mark changes as unsaved ONLY if position changes ***
                    HasUnsavedChanges = true;

                    // Get the item being moved
                    var itemToMove = _virtualActiveMods[currentItemIndex];

                    // Remove from old position
                    _virtualActiveMods.RemoveAt(currentItemIndex);

                    // Insert at the *actual* new index (use the original clamped 'newIndex' before adjustment logic)
                    // Need to recalculate insert index based on list size *after* removal
                    int actualInsertIndex = Math.Clamp(newIndex, 0, _virtualActiveMods.Count);
                    // Adjust if the drop target was after the original position
                    if (newIndex > currentItemIndex)
                    {
                        actualInsertIndex = Math.Clamp(newIndex - 1, 0, _virtualActiveMods.Count);
                    }
                    else
                    {
                        actualInsertIndex = Math.Clamp(newIndex, 0, _virtualActiveMods.Count);
                    }


                    _virtualActiveMods.Insert(actualInsertIndex, itemToMove);

                    // Reassign all load orders sequentially
                    for (int i = 0; i < _virtualActiveMods.Count; i++)
                    {
                        _virtualActiveMods[i] = (_virtualActiveMods[i].Mod, i);
                    }

                    // Refresh the UI list to reflect the new order
                    FilterActiveMods();
                    // Optionally ensure the moved item is visible
                    // ActiveMods.ScrollIntoView might be needed depending on UI implementation,
                    // but FilterActiveMods usually handles the update for ObservableCollection.
                }
                // Else: No change in effective position, do nothing, HasUnsavedChanges remains false (or its previous state).
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Reorder error: {ex.Message}");
                // Consider showing an error message to the user
                MessageBox.Show($"An error occurred during reordering: {ex.Message}", "Reorder Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                // Optional: Consider reloading data as a recovery mechanism if the list state might be corrupted
                // await LoadDataAsync();
            }
        }


        public void AddModToActiveAtPosition(ModItem mod, int position)
        {
            // Prevent adding if already active
            if (_virtualActiveMods.Any(x => x.Mod == mod)) return;

            // Remove from inactive list first
            _allInactiveMods.Remove(mod);

            // Clamp position
            position = Math.Clamp(position, 0, _virtualActiveMods.Count);

            // Insert into virtual list (temporarily without final load order)
            _virtualActiveMods.Insert(position, (mod, position)); // Use desired position as initial index

            // Reassign all load orders sequentially
            for (int i = 0; i < _virtualActiveMods.Count; i++)
            {
                _virtualActiveMods[i] = (_virtualActiveMods[i].Mod, i);
            }

            // Update derived list and mod status
            _allActiveMods = _virtualActiveMods.Select(x => x.Mod).ToList();
            mod.IsActive = true;

            // Notify UI
            OnPropertyChanged(nameof(TotalActiveMods));
            OnPropertyChanged(nameof(TotalInactiveMods));
            FilterActiveMods();
            FilterInactiveMods();

            // Set selection to the newly added mod
            SelectedMod = mod;
            HasUnsavedChanges = true;
            // Ensure it's visible in the active list UI (handled by FilterActiveMods)
        }

        private void SortActiveList(object parameter)
        {
            if (_virtualActiveMods.Count == 0) return;

            var previouslySelected = SelectedMod;

            // *** Store the original order of PackageIds (or another unique identifier) ***
            var originalOrder = _virtualActiveMods
                                    .Select(x => x.Mod.PackageId?.ToLowerInvariant() ?? Guid.NewGuid().ToString()) // Use PackageId (lowercase) or a fallback unique ID
                                    .ToList();

            // Sort the virtual list with priority: Core > Expansion > Others, then alphabetically
            // and assign new sequential load orders
            _virtualActiveMods = _virtualActiveMods
                .OrderByDescending(x => x.Mod.IsCore)       // Core mods first (true > false)
                .ThenByDescending(x => x.Mod.IsExpansion) // Then Expansion mods (true > false)
                .ThenBy(x => x.Mod.Name, StringComparer.OrdinalIgnoreCase) // Then sort alphabetically by name (case-insensitive)
                .Select((entry, index) => (entry.Mod, LoadOrder: index)) // Assign new load order index
                .ToList();

            // *** Get the new order of PackageIds ***
            var newOrder = _virtualActiveMods
                               .Select(x => x.Mod.PackageId?.ToLowerInvariant() ?? Guid.NewGuid().ToString())
                               .ToList();

            // *** Check if the order actually changed ***
            if (!originalOrder.SequenceEqual(newOrder))
            {
                // *** Mark changes as unsaved ONLY if the order has changed ***
                HasUnsavedChanges = true;
            }
            // Else: Order is the same, do nothing, HasUnsavedChanges remains false (or its previous state).


            // Update the derived active mods list (optional if only using virtual list directly)
            _allActiveMods = _virtualActiveMods.Select(x => x.Mod).ToList();

            // Refresh the filtered UI list for Active Mods
            FilterActiveMods(); // This will apply the new order from _virtualActiveMods

            // Restore selection if possible
            // Check if the previously selected mod is still in the (now sorted) virtual list
            SelectedMod = _virtualActiveMods.Any(x => x.Mod == previouslySelected)
                          ? previouslySelected
                          : ActiveMods.FirstOrDefault(); // Fallback to first item in the updated UI list

            // Notify that counts *might* have changed properties (though sorting shouldn't change counts)
            // OnPropertyChanged(nameof(TotalActiveMods)); // Count unlikely to change but doesn't hurt
        }


        private void StripMods(object parameter)
        {
            System.Windows.MessageBox.Show("Strip mods: Functionality not yet implemented.");
        }

        private void CreatePack(object parameter)
        {
            System.Windows.MessageBox.Show("Create pack: Functionality not yet implemented.");
        }

        private void FixIntegrity(object parameter)
        {
            System.Windows.MessageBox.Show("Fix integrity: Functionality not yet implemented.");
        }

        private void ImportList(object parameter)
        {
            System.Windows.MessageBox.Show("Import list: Functionality not yet implemented.");
        }

        private void ImportSave(object parameter)
        {
            System.Windows.MessageBox.Show("Import save: Functionality not yet implemented.");
        }

        private void ExportList(object parameter)
        {
            System.Windows.MessageBox.Show("Export list: Functionality not yet implemented.");
        }

        private void SaveMods(object parameter)
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

                // Check if the file exists, create a basic structure if not
                if (!File.Exists(configPath))
                {
                    doc = new XDocument(
                        new XElement("ModsConfigData",
                            new XElement("version", "1.0"), // Or get current game version if needed
                            new XElement("activeMods")
                        )
                    );
                    MessageBox.Show($"ModsConfig.xml not found. A new file will be created at: {configPath}", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    try
                    {
                        doc = XDocument.Load(configPath);
                    }
                    catch (Exception loadEx)
                    {
                        MessageBox.Show($"Error loading existing ModsConfig.xml: {loadEx.Message}\n\nUnable to save.", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }


                var activeModsElement = doc.Root?.Element("activeMods");

                // Ensure the activeMods element exists
                if (activeModsElement == null)
                {
                    // Try to add it if the root exists
                    if (doc.Root != null)
                    {
                        activeModsElement = new XElement("activeMods");
                        doc.Root.Add(activeModsElement);
                        MessageBox.Show("The 'activeMods' section was missing in ModsConfig.xml and has been added.", "Config Repaired", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        MessageBox.Show("Error: ModsConfig.xml seems corrupted (missing root element). Unable to save.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // Clear existing active mods within the XML element
                activeModsElement.RemoveAll();

                // Add mods in current virtual load order, ensuring PackageId is not null/empty and using lowercase
                foreach (var (mod, _) in _virtualActiveMods.OrderBy(x => x.LoadOrder))
                {
                    if (!string.IsNullOrEmpty(mod.PackageId))
                    {
                        activeModsElement.Add(new XElement("li", mod.PackageId.ToLowerInvariant()));
                    }
                    else
                    {
                        // Optionally warn about mods without PackageId
                        Debug.WriteLine($"Warning: Mod '{mod.Name}' has no PackageId and was not saved to active mods list.");
                    }
                }

                // Save the document
                doc.Save(configPath);
                MessageBox.Show("Mods configuration saved successfully!", "Save Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show($"Error: Permission denied when trying to save to {Path.Combine(_pathService.GetConfigPath(), "ModsConfig.xml")}.\n\nPlease check file/folder permissions or run the application as administrator.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An unexpected error occurred while saving mods configuration: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void RunGame(object parameter)
        {
            System.Windows.MessageBox.Show("Run game: Functionality not yet implemented.");
        }
    }
}