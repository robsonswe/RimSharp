using RimSharp.Models;
using RimSharp.Services;
using RimSharp.Handlers;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace RimSharp.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IModService _modService;
        private readonly IPathService _pathService;
        
        private string _selectedTab = "Mods";
        private ModItem _selectedMod;
        private bool _isLoading;
        
        public ObservableCollection<ModItem> ActiveMods { get; } = new();
        public ObservableCollection<ModItem> InactiveMods { get; } = new();
        
        public PathSettings PathSettings { get; }
        
        public ICommand SwitchTabCommand { get; }
        public ICommand SelectModCommand { get; }
        public ICommand BrowsePathCommand { get; }
        
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }
        
        public MainViewModel(IModService modService, IPathService pathService)
        {
            _modService = modService;
            _pathService = pathService;
            
            PathSettings = new PathSettings
            {
                GameVersion = _pathService.GetGameVersion(),
                GamePath = _pathService.GetGamePath(),
                ConfigPath = _pathService.GetConfigPath(),
                ModsPath = _pathService.GetModsPath()
            };
            
            // Initialize commands
            SwitchTabCommand = new RelayCommand(SwitchTab);
            SelectModCommand = new RelayCommand(SelectMod);
            BrowsePathCommand = new RelayCommand(BrowsePath);
            
            // Load data asynchronously
            LoadDataAsync();
        }
        
        private async Task LoadDataAsync()
        {
            IsLoading = true;
            
            await Task.Run(() =>
            {
                _modService.LoadMods();
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    LoadDummyData();
                });
            });
            
            IsLoading = false;
        }
        
        private void LoadDummyData()
        {
            // Add core mods
            ActiveMods.Add(new ModItem { Name = "Core", IsCore = true });
            ActiveMods.Add(new ModItem { Name = "Royalty [Official DLC]", IsCore = true });
            ActiveMods.Add(new ModItem { Name = "Ideology [Official DLC]", IsCore = true });
            ActiveMods.Add(new ModItem { Name = "Biotech [Official DLC]", IsCore = true });
            
            // Add inactive mods (sample from your HTML)
            InactiveMods.Add(new ModItem { Name = "(Dirty) Windows", IsDirty = true });
            InactiveMods.Add(new ModItem { Name = "1-2-3 Personalities M1" });
            // Add more dummy mods as needed...
            
            SelectedMod = InactiveMods.FirstOrDefault();
        }
        
        private void SwitchTab(object parameter)
        {
            SelectedTab = parameter as string;
        }
        
        private void SelectMod(object parameter)
        {
            if (parameter is ModItem mod)
            {
                SelectedMod = mod;
            }
        }
        
        private void BrowsePath(object parameter)
        {
            // TODO: Implement path browsing
        }
        
        // Properties with change notification
        public string SelectedTab
        {
            get => _selectedTab;
            set => SetProperty(ref _selectedTab, value);
        }
        
        public ModItem SelectedMod
        {
            get => _selectedMod;
            set => SetProperty(ref _selectedMod, value);
        }
    }
}