using RimSharp.Models;
using RimSharp.Services;
using RimSharp.Handlers;         // Namespace for RelayCommand
using RimSharp.ViewModels;    // Namespace for ViewModelBase
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input; // Namespace for ICommand
using System.Collections.Generic; // For filtering



// Correct Namespace Declaration
namespace RimSharp.ViewModels.Modules.Mods
{
    // Correct Class Name
    public class ModsViewModel : ViewModelBase // <--- RENAMED CLASS
    {
        private readonly IModService _modService;

        private ModItem _selectedMod;
        private bool _isLoading;
               private string _activeSearchText = "";
        private string _inactiveSearchText = "";    

        private List<ModItem> _allActiveMods = new();
        private List<ModItem> _allInactiveMods = new();

        public int TotalActiveMods => _allActiveMods.Count;
        public int TotalInactiveMods => _allInactiveMods.Count;




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
        public ModsViewModel(IModService modService)
        {
            _modService = modService;
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

            LoadDataAsync();
        }


private async Task LoadDataAsync()
{
    IsLoading = true;
    ActiveMods.Clear();
    InactiveMods.Clear();
    _allActiveMods.Clear();
    _allInactiveMods.Clear();

    await _modService.LoadModsAsync();
    
    var allMods = _modService.GetLoadedMods().ToList();
    
    _allActiveMods = allMods.Where(m => m.IsActive).OrderBy(m => m.Name).ToList();
    _allInactiveMods = allMods.Where(m => !m.IsActive).OrderBy(m => m.Name).ToList();
    
    // Notify that the totals have changed
    OnPropertyChanged(nameof(TotalActiveMods));
    OnPropertyChanged(nameof(TotalInactiveMods));
    
    FilterActiveMods(); // Apply initial filter (shows all when search is empty)
    FilterInactiveMods();

    SelectedMod = ActiveMods.FirstOrDefault() ?? InactiveMods.FirstOrDefault();
    IsLoading = false;
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
                ? _allActiveMods
                : _allActiveMods.Where(m => m.Name.Contains(_activeSearchText, System.StringComparison.OrdinalIgnoreCase));
                
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
            // TODO: Implement save mods logic
            System.Windows.MessageBox.Show("Save mods functionality will go here");
        }

        private void RunGame(object parameter)
        {
            // TODO: Implement run game logic
            System.Windows.MessageBox.Show("Run game functionality will go here");
        }

    }
}
