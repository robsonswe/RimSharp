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
using RimSharp.Features.WorkshopDownloader.Services;

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

        private readonly IModReplacementService _replacementService;
        private readonly IDownloadQueueService _downloadQueueService;
        private readonly ISteamApiClient _steamApiClient;

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
                Debug.WriteLine($"[ModActionsViewModel] IsParentLoading SETTER: Current = {_isParentLoading}, New = {value}");
                // Use base SetProperty, command observation handles updates
                if (SetProperty(ref _isParentLoading, value))
                {
                    Debug.WriteLine($"[ModActionsViewModel] IsParentLoading Changed to {value}.");
                }
            }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set
            {
                // Use base SetProperty, command observation handles updates
                // REMOVE THE EVENT INVOCATION FROM HERE
                // if (SetProperty(ref _hasUnsavedChanges, value))
                // {
                //     // Request update in parent VM if needed (handled by event)
                //     // HasUnsavedChangesRequest?.Invoke(this, value); // <<< REMOVE THIS LINE
                // }
                // Setter still needs to raise PropertyChanged for command observation:
                SetProperty(ref _hasUnsavedChanges, value);
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
        public ICommand CheckReplacementsCommand { get; private set; }

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
            IModService modService,
            IModReplacementService replacementService,
            IDownloadQueueService downloadQueueService,
            ISteamApiClient steamApiClient)
        {
            _dataService = dataService;
            _commandService = commandService;
            _ioService = ioService;
            _modListManager = modListManager;
            _incompatibilityService = incompatibilityService;
            _dialogService = dialogService;
            _pathService = pathService;
            _modService = modService;
            _replacementService = replacementService;
            _pathService.RefreshPaths();
            _downloadQueueService = downloadQueueService;
            _steamApiClient = steamApiClient;
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
            CustomizeModCommand = CreateAsyncCommand<ModItem>(
                execute: ExecuteCustomizeMod,
                canExecute: CanExecutizeMod,
                observedProperties: new[] { nameof(IsParentLoading), nameof(SelectedMod) });


        }

        private bool CanExecutizeMod(ModItem mod)
        {
            return !IsParentLoading && mod != null && mod.ModType != ModType.Core && mod.ModType != ModType.Expansion;
        }

        private async Task ExecuteCustomizeMod(ModItem mod)
        {
            if (mod == null) return;

            ModCustomInfo customInfo = null;
            CustomizeModDialogViewModel viewModel = null;
            ModCustomizationResult result = ModCustomizationResult.Cancel; // Default result

            // Indicate loading potentially (optional, if it's quick, maybe not needed)
            // IsLoadingRequest?.Invoke(this, true); // Consider if needed

            try
            {
                Debug.WriteLine($"Attempting to customize mod: {mod.PackageId}");

                // 1. Load data in the background (Keep this)
                customInfo = await Task.Run(() => _modService.GetCustomModInfo(mod.PackageId));
                // Execution resumes here, potentially on UI thread or background thread after await

                // 2. Ensure subsequent UI operations run on the UI thread.
                // Use the RunOnUIThread helper from ViewModelBase.
                RunOnUIThread(() =>
                {
                    // Create ViewModel (safe on UI thread)
                    viewModel = new CustomizeModDialogViewModel(mod, customInfo, _modService);

                    Debug.WriteLine("Showing customize dialog on UI thread...");

                    // Show Dialog (Must be on UI thread)
                    result = _dialogService.ShowCustomizeModDialog(viewModel); // ShowDialog blocks here

                    Debug.WriteLine($"Dialog result: {result}");

                    if (result == ModCustomizationResult.Save)
                    {
                        Debug.WriteLine("Requesting data refresh after save...");
                        // Trigger refresh event (also safer on UI thread)
                        RequestDataRefresh?.Invoke(this, EventArgs.Empty);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ExecuteCustomizeMod: {ex}");
                // Show error message on UI thread
                RunOnUIThread(() => _dialogService.ShowError("Customization Error", $"Failed to customize mod '{mod?.Name ?? "Unknown"}': {ex.Message}"));
            }
            finally
            {
                // IsLoadingRequest?.Invoke(this, false); // Turn off loading indicator if used
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
