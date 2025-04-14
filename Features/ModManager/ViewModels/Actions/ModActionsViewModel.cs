using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Core.Commands;
using RimSharp.Features.ModManager.Dialogs.DuplicateMods;
using RimSharp.Features.ModManager.Dialogs.Incompatibilities;
using RimSharp.MyApp.AppFiles;
using RimSharp.MyApp.Dialogs;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using RimSharp.Features.ModManager.Dialogs.CustomizeMod;

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    // Mark the class as partial
    public partial class ModActionsViewModel : ViewModelBase // Ensure inherits from ViewModelBase
    {
        // Dependencies (Remain here)
        private readonly IModDataService _dataService;
        private readonly IModCommandService _commandService;
        private readonly IModListIOService _ioService;
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService;
        private readonly IModIncompatibilityService _incompatibilityService;
        private readonly IPathService _pathService;

        private readonly IModService _modService;

        // State properties (Remain here)
        private bool _isParentLoading;
        private bool _hasUnsavedChanges;
        private ModItem _selectedMod; // For single-item actions
        private IList _selectedItems; // For multi-item actions
        protected bool CanExecuteSimpleCommands() => !IsParentLoading && HasValidPaths;

        public bool IsParentLoading
        {
            get => _isParentLoading;
            set
            {
                // Use base SetProperty, command observation handles updates
                SetProperty(ref _isParentLoading, value);
                // Manual RaiseCanExecuteChangedForAllCommands() removed
            }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set
            {
                // Use base SetProperty, command observation handles updates
                if (SetProperty(ref _hasUnsavedChanges, value))
                {
                    // Request update in parent VM if needed (handled by event)
                    HasUnsavedChangesRequest?.Invoke(this, value);
                }
                // Manual RaiseCanExecuteChangedForAllCommands() removed
            }
        }

        public ModItem SelectedMod
        {
            get => _selectedMod;
            set
            {
                // Use base SetProperty, command observation handles updates
                SetProperty(ref _selectedMod, value);
                // Manual RaiseCanExecuteChangedForAllCommands() removed
            }
        }

        public IList SelectedItems // Bound from ListBox typically
        {
            get => _selectedItems;
            set
            {
                // Use base SetProperty, command observation handles updates
                SetProperty(ref _selectedItems, value);
                // Manual RaiseCanExecuteChangedForAllCommands() removed
            }
        }

        private bool _hasValidPaths;
        public bool HasValidPaths
        {
            get => _hasValidPaths;
            private set => SetProperty(ref _hasValidPaths, value);
        }


        // Command Properties (Declarations remain here)
        // List Management
        public ICommand ClearActiveListCommand { get; private set; }
        public ICommand SortActiveListCommand { get; private set; }
        public ICommand SaveCommand { get; private set; }
        public ICommand ImportListCommand { get; private set; }
        public ICommand ExportListCommand { get; private set; }

        // Mod Actions (Single/Multi)
        public ICommand DeleteModCommand { get; private set; } // Single
        public ICommand DeleteModsCommand { get; private set; } // Multi
        public ICommand OpenModFoldersCommand { get; private set; } // Multi
        public ICommand OpenUrlsCommand { get; private set; } // Multi
        public ICommand OpenWorkshopPagesCommand { get; private set; } // Multi
        public ICommand OpenOtherUrlsCommand { get; private set; } // Multi

        // Tools & Analysis
        public ICommand ResolveDependenciesCommand { get; private set; }
        public ICommand CheckIncompatibilitiesCommand { get; private set; }
        public ICommand CheckDuplicatesCommand { get; private set; }

        // Placeholders
        public ICommand StripModsCommand { get; private set; }
        public ICommand FixIntegrityCommand { get; private set; }
        public ICommand RunGameCommand { get; private set; }
        public ICommand CustomizeModCommand { get; private set; }

        // Installation
        public ICommand InstallFromZipCommand { get; private set; }
        public ICommand InstallFromGithubCommand { get; private set; }

        // Events (Remain here)
        public event EventHandler<bool> IsLoadingRequest;
        public event EventHandler RequestDataRefresh;
        public event EventHandler<bool> HasUnsavedChangesRequest;

        // Helper state (Example, might be better encapsulated if complex)
        private bool _installSuccess = false;

        // Constructor (Remains here)
        public ModActionsViewModel(
            IModDataService dataService,
            IModCommandService commandService,
            IModListIOService ioService,
            IModListManager modListManager,
            IModIncompatibilityService incompatibilityService,
            IDialogService dialogService,
            IPathService pathService,
            IModService modService)
        {
            _dataService = dataService;
            _commandService = commandService;
            _ioService = ioService;
            _modListManager = modListManager;
            _incompatibilityService = incompatibilityService;
            _dialogService = dialogService;
            _pathService = pathService;
            _modService = modService;
            _pathService.RefreshPaths();
            RefreshPathValidity();
            InitializeCommands(); // Calls partial initialization methods
        }

        // Combined initializer calling partial initializers
        private void InitializeCommands()
        {
            InitializeListManagementCommands();
            InitializeModActionsCommands();
            InitializeToolsAnalysisCommands();
            InitializeInstallationCommands();
            InitializePlaceholderCommands();
            CustomizeModCommand = CreateCommand<ModItem>(
                execute: mod => ExecuteCustomizeMod(mod),
                canExecute: mod => CanExecutizeMod(mod));

        }

        private bool CanExecutizeMod(ModItem mod)
        {
            return !IsParentLoading && mod != null && mod.ModType != ModType.Core && mod.ModType != ModType.Expansion;
        }

        private async Task ExecuteCustomizeMod(ModItem mod)
        {
            if (mod == null) return;

            try
            {
                Debug.WriteLine($"Attempting to customize mod: {mod.PackageId}");
                var customInfo = _modService.GetCustomModInfo(mod.PackageId);
                var viewModel = new CustomizeModDialogViewModel(mod, customInfo, _modService);

                Debug.WriteLine("Showing customize dialog...");
                var result = _dialogService.ShowCustomizeModDialog(viewModel);
                Debug.WriteLine($"Dialog result: {result}");

                if (result == ModCustomizationResult.Save)
                {
                    Debug.WriteLine("Saving custom mod info...");
                    RequestDataRefresh?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ExecuteCustomizeMod: {ex}");
            }
        }

        private void RefreshPathValidity()
        {
            var gamePath = _pathService.GetGamePath();
            var modsPath = _pathService.GetModsPath();
            var configPath = _pathService.GetConfigPath();

            HasValidPaths = !string.IsNullOrEmpty(gamePath) &&
                           !string.IsNullOrEmpty(modsPath) &&
                           !string.IsNullOrEmpty(configPath);
        }


    }
}
