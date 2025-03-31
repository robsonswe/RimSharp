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

        private List<ModItem> _allActiveMods = new();
        private List<ModItem> _allInactiveMods = new();

        public int TotalActiveMods => _allActiveMods.Count;
        public int TotalInactiveMods => _allInactiveMods.Count;

        private List<(ModItem Mod, int LoadOrder)> _virtualActiveMods = new();





        public ObservableCollection<ModItem> ActiveMods { get; } = new();
        public ObservableCollection<ModItem> InactiveMods { get; } = new();

        public ICommand SelectModCommand { get; } // Keep only mod-specific commands
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

        // Correct Constructor Name
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
                _virtualActiveMods.Clear();

                await _modService.LoadModsAsync();

                var allMods = _modService.GetLoadedMods().ToList();
                var activeModsFromConfig = GetActiveModsFromConfig();

                // Debug output
                Console.WriteLine($"Found {activeModsFromConfig.Count} active mods in config");
                Console.WriteLine($"Found {allMods.Count} total mods");

                // Build virtual active mods list with load order
                for (int i = 0; i < activeModsFromConfig.Count; i++)
                {
                    var packageId = activeModsFromConfig[i];
                    var mod = allMods.FirstOrDefault(m =>
                m.PackageId?.ToLowerInvariant() == packageId); // Case-insensitive comparison

                    if (mod != null)
                    {
                        _virtualActiveMods.Add((mod, i));
                        mod.IsActive = true;
                        Console.WriteLine($"Added active mod: {mod.Name} ({mod.PackageId})");
                    }
                    else
                    {
                        Console.WriteLine($"Could not find mod with packageId: {packageId}");
                    }
                }

                // Separate into active and inactive lists
                _allActiveMods = _virtualActiveMods.Select(x => x.Mod).ToList();
                _allInactiveMods = allMods.Except(_allActiveMods).OrderBy(m => m.Name).ToList();

                Console.WriteLine($"Active mods count: {_allActiveMods.Count}");
                Console.WriteLine($"Inactive mods count: {_allInactiveMods.Count}");

                // Notify that the totals have changed
                OnPropertyChanged(nameof(TotalActiveMods));
                OnPropertyChanged(nameof(TotalInactiveMods));

                // Apply sorting based on load order
                FilterActiveMods();
                FilterInactiveMods();

                SelectedMod = ActiveMods.FirstOrDefault() ?? InactiveMods.FirstOrDefault();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading mods: {ex.Message}");
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
            catch
            {
                return new List<string>();
            }
        }
        private void LoadDummyData()
        {
            // Add core mods
            ActiveMods.Add(new ModItem { Name = "Core", IsCore = true });
            ActiveMods.Add(new ModItem { Name = "Royalty [Official DLC]", IsCore = true });
            ActiveMods.Add(new ModItem { Name = "Ideology [Official DLC]", IsCore = true });
            ActiveMods.Add(new ModItem { Name = "Biotech [Official DLC]", IsCore = true });

            // Add inactive mods
            InactiveMods.Add(new ModItem { Name = "(Dirty) Windows" });
            InactiveMods.Add(new ModItem { Name = "1-2-3 Personalities M1" });
            InactiveMods.Add(new ModItem { Name = "A Dog Said... Animal Prosthetics" });
            InactiveMods.Add(new ModItem { Name = "Achtung!" });

            SelectedMod = InactiveMods.FirstOrDefault();
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
            ActiveMods.Clear();
            var filteredMods = string.IsNullOrWhiteSpace(_activeSearchText)
                ? _virtualActiveMods.OrderBy(x => x.LoadOrder).Select(x => x.Mod)
                : _virtualActiveMods.Where(x => x.Mod.Name.Contains(_activeSearchText, StringComparison.OrdinalIgnoreCase))
                                  .OrderBy(x => x.LoadOrder)
                                  .Select(x => x.Mod);

            foreach (var mod in filteredMods)
            {
                ActiveMods.Add(mod);
            }
        }

        private void FilterInactiveMods()
        {
            InactiveMods.Clear();
            var filteredMods = string.IsNullOrWhiteSpace(_inactiveSearchText)
                ? _allInactiveMods
                : _allInactiveMods.Where(m => m.Name.Contains(_inactiveSearchText, System.StringComparison.OrdinalIgnoreCase));

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
            // Consider clearing lists before loading again if service doesn't handle it
            // ActiveMods.Clear();
            // InactiveMods.Clear();
            return LoadDataAsync();
        }

        private void ClearActiveList(object parameter)
        {
            // Move all active mods to inactive
            _allInactiveMods.AddRange(_allActiveMods);
            _allActiveMods.Clear();

            // Notify property changes
            OnPropertyChanged(nameof(TotalActiveMods));
            OnPropertyChanged(nameof(TotalInactiveMods));

            // Update the filtered lists
            FilterActiveMods();
            FilterInactiveMods();
        }

        public void MoveModUp(ModItem mod)
        {
            var item = _virtualActiveMods.FirstOrDefault(x => x.Mod == mod);
            if (item != default && item.LoadOrder > 0)
            {
                // Swap with the mod above
                var itemAbove = _virtualActiveMods.First(x => x.LoadOrder == item.LoadOrder - 1);
                _virtualActiveMods.Remove(item);
                _virtualActiveMods.Remove(itemAbove);

                _virtualActiveMods.Add((item.Mod, item.LoadOrder - 1));
                _virtualActiveMods.Add((itemAbove.Mod, itemAbove.LoadOrder + 1));

                // Rebuild the list with correct order
                _virtualActiveMods = _virtualActiveMods.OrderBy(x => x.LoadOrder).ToList();
                FilterActiveMods();
            }
        }

        public void MoveModDown(ModItem mod)
        {
            var item = _virtualActiveMods.FirstOrDefault(x => x.Mod == mod);
            if (item != default && item.LoadOrder < _virtualActiveMods.Count - 1)
            {
                // Swap with the mod below
                var itemBelow = _virtualActiveMods.First(x => x.LoadOrder == item.LoadOrder + 1);
                _virtualActiveMods.Remove(item);
                _virtualActiveMods.Remove(itemBelow);

                _virtualActiveMods.Add((item.Mod, item.LoadOrder + 1));
                _virtualActiveMods.Add((itemBelow.Mod, itemBelow.LoadOrder - 1));

                // Rebuild the list with correct order
                _virtualActiveMods = _virtualActiveMods.OrderBy(x => x.LoadOrder).ToList();
                FilterActiveMods();
            }
        }

        public void AddModToActive(ModItem mod)
        {
            if (!_virtualActiveMods.Any(x => x.Mod == mod))
            {
                _virtualActiveMods.Add((mod, _virtualActiveMods.Count));
                mod.IsActive = true;

                // Update the lists
                _allActiveMods = _virtualActiveMods.Select(x => x.Mod).ToList();
                _allInactiveMods.Remove(mod);

                OnPropertyChanged(nameof(TotalActiveMods));
                OnPropertyChanged(nameof(TotalInactiveMods));
                FilterActiveMods();
                FilterInactiveMods();
            }
        }

        public void RemoveModFromActive(ModItem mod)
        {
            var item = _virtualActiveMods.FirstOrDefault(x => x.Mod == mod);
            if (item != default)
            {
                _virtualActiveMods.Remove(item);
                mod.IsActive = false;

                // Update load orders for remaining mods
                for (int i = 0; i < _virtualActiveMods.Count; i++)
                {
                    var existing = _virtualActiveMods[i];
                    _virtualActiveMods[i] = (existing.Mod, i);
                }

                // Update the lists
                _allActiveMods = _virtualActiveMods.Select(x => x.Mod).ToList();
                _allInactiveMods.Add(mod);

                OnPropertyChanged(nameof(TotalActiveMods));
                OnPropertyChanged(nameof(TotalInactiveMods));
                FilterActiveMods();
                FilterInactiveMods();
            }
        }


        private void SortActiveList(object parameter)
        {
            // TODO: Implement sort active list logic
            var sorted = ActiveMods.OrderBy(m => m.Name).ToList();
            ActiveMods.Clear();
            foreach (var mod in sorted)
            {
                ActiveMods.Add(mod);
            }
        }

        private void StripMods(object parameter)
        {
            // TODO: Implement strip mods logic
            System.Windows.MessageBox.Show("Strip mods functionality will go here");
        }

        private void CreatePack(object parameter)
        {
            // TODO: Implement create pack logic
            System.Windows.MessageBox.Show("Create pack functionality will go here");
        }

        private void FixIntegrity(object parameter)
        {
            // TODO: Implement fix integrity logic
            System.Windows.MessageBox.Show("Fix integrity functionality will go here");
        }

        private void ImportList(object parameter)
        {
            // TODO: Implement import list logic
            System.Windows.MessageBox.Show("Import list functionality will go here");
        }

        private void ImportSave(object parameter)
        {
            // TODO: Implement import save logic
            System.Windows.MessageBox.Show("Import save functionality will go here");
        }

        private void ExportList(object parameter)
        {
            // TODO: Implement export list logic
            System.Windows.MessageBox.Show("Export list functionality will go here");
        }

        private void SaveMods(object parameter)
        {
            try
            {
                var configPath = Path.Combine(_pathService.GetConfigPath(), "ModsConfig.xml");
                if (!File.Exists(configPath)) return;

                var doc = XDocument.Load(configPath);
                var activeModsElement = doc.Root.Element("activeMods");

                // Clear existing active mods
                activeModsElement?.RemoveAll();

                // Add mods in current load order
                foreach (var (mod, _) in _virtualActiveMods.OrderBy(x => x.LoadOrder))
                {
                    activeModsElement?.Add(new XElement("li", mod.PackageId));
                }

                doc.Save(configPath);
                MessageBox.Show("Mods configuration saved successfully!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving mods configuration: {ex.Message}");
            }
        }

        private void RunGame(object parameter)
        {
            // TODO: Implement run game logic
            System.Windows.MessageBox.Show("Run game functionality will go here");
        }

    }
}
