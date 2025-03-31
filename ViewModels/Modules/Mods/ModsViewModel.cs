using RimSharp.Models;
using RimSharp.Services;
using RimSharp.Handlers;         // Namespace for RelayCommand
using RimSharp.ViewModels;    // Namespace for ViewModelBase
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input; // Namespace for ICommand


// Correct Namespace Declaration
namespace RimSharp.ViewModels.Modules.Mods
{
    // Correct Class Name
    public class ModsViewModel : ViewModelBase // <--- RENAMED CLASS
    {
        private readonly IModService _modService;

        private ModItem _selectedMod;
        private bool _isLoading;

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

            await _modService.LoadModsAsync();
            
            var allMods = _modService.GetLoadedMods().ToList();
            
            foreach (var mod in allMods.Where(m => m.IsActive).OrderBy(m => m.Name))
            {
                ActiveMods.Add(mod);
            }
            
            foreach (var mod in allMods.Where(m => !m.IsActive).OrderBy(m => m.Name))
            {
                InactiveMods.Add(mod);
            }

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
            // TODO: Implement clear active list logic
            ActiveMods.Clear();
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
