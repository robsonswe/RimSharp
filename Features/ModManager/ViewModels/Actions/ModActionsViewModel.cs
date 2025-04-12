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

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    // Mark the class as partial
    public partial class ModActionsViewModel : ViewModelBase
    {
        // Dependencies (Remain here)
        private readonly IModDataService _dataService;
        private readonly IModCommandService _commandService;
        private readonly IModListIOService _ioService;
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService;
        private readonly IModIncompatibilityService _incompatibilityService;
        private readonly IPathService _pathService;

        // State properties (Remain here)
        private bool _isParentLoading;
        private bool _hasUnsavedChanges;
        private ModItem _selectedMod; // For single-item actions
        private IList _selectedItems; // For multi-item actions

        public bool IsParentLoading
        {
            get => _isParentLoading;
            set
            {
                if (SetProperty(ref _isParentLoading, value)) RaiseCanExecuteChangedForAllCommands();
            }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set
            {
                if (SetProperty(ref _hasUnsavedChanges, value)) RaiseCanExecuteChangedForAllCommands();
            }
        }

        public ModItem SelectedMod
        {
            get => _selectedMod;
            set
            {
                if (SetProperty(ref _selectedMod, value)) RaiseCanExecuteChangedForAllCommands();
            }
        }

        public IList SelectedItems // Bound from ListBox typically
        {
            get => _selectedItems;
            set
            {
                if (SetProperty(ref _selectedItems, value)) RaiseCanExecuteChangedForAllCommands();
            }
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
            IPathService pathService)
        {
            _dataService = dataService;
            _commandService = commandService;
            _ioService = ioService;
            _modListManager = modListManager;
            _incompatibilityService = incompatibilityService;
            _dialogService = dialogService;
            _pathService = pathService;
            InitializeCommands(); // Calls partial initialization methods
        }

        // Combined initializer calling partial initializers
        private void InitializeCommands()
        {
            InitializeListManagementCommands();
            InitializeModActionsCommands();
            InitializeToolsAnalysisCommands();
            InitializeInstallationCommands();
            InitializePlaceholderCommands(); // Add initialization for placeholders
        }

        // Central CanExecuteChanged raiser (Remains here)
        private void RaiseCanExecuteChangedForAllCommands()
        {
            // Use the specific command type for correct RaiseCanExecuteChanged invocation
            (ClearActiveListCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (SortActiveListCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ImportListCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (ExportListCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (ResolveDependenciesCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (CheckIncompatibilitiesCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (CheckDuplicatesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteModCommand as AsyncRelayCommand<ModItem>)?.RaiseCanExecuteChanged();
            (DeleteModsCommand as AsyncRelayCommand<IList>)?.RaiseCanExecuteChanged();
            (OpenModFoldersCommand as RelayCommand<IList>)?.RaiseCanExecuteChanged();
            (OpenUrlsCommand as RelayCommand<IList>)?.RaiseCanExecuteChanged();
            (OpenWorkshopPagesCommand as RelayCommand<IList>)?.RaiseCanExecuteChanged();
            (OpenOtherUrlsCommand as RelayCommand<IList>)?.RaiseCanExecuteChanged();
            (StripModsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FixIntegrityCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RunGameCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (InstallFromZipCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (InstallFromGithubCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        // --- Generic CanExecute Predicates (Can stay here or move if specific) ---
        private bool CanExecuteSimpleCommands() => !IsParentLoading;
        private bool CanExecuteSaveMods() => HasUnsavedChanges && !IsParentLoading;
        private bool CanExecuteExport() => !IsParentLoading && _modListManager.VirtualActiveMods.Any();
        private bool CanExecuteCheckIncompatibilities() => !IsParentLoading && _modListManager.VirtualActiveMods.Any();
        private bool CanExecuteMultiSelectActions(IList selectedItems)
        {
            selectedItems = selectedItems ?? SelectedItems;
            return selectedItems != null && selectedItems.Count > 0 && !IsParentLoading;
        }
    }
}
