using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Core.Commands;
using RimSharp.Features.ModManager.Dialogs.DuplicateMods;
using RimSharp.Features.ModManager.Dialogs.Incompatibilities;
using RimSharp.Features.ModManager.Services.Commands;
using RimSharp.MyApp.AppFiles;
using RimSharp.MyApp.Dialogs;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Collections;

namespace RimSharp.Features.ModManager.ViewModels
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
        public ICommand DeleteModCommand { get; private set; }
        public ICommand ResolveDependenciesCommand { get; private set; }
        public ICommand CheckIncompatibilitiesCommand { get; private set; }
        public ICommand CheckDuplicatesCommand { get; private set; }

        // New Commands for Multiple Items
        public ICommand OpenModFoldersCommand { get; private set; }
        public ICommand OpenUrlsCommand { get; private set; }
        public ICommand OpenWorkshopPagesCommand { get; private set; }
        public ICommand OpenOtherUrlsCommand { get; private set; }
        public ICommand DeleteModsCommand { get; private set; }

        public ModsViewModel(
            IModDataService dataService,
            IModFilterService filterService,
            IModCommandService commandService,
            IModListIOService ioService,
            IModListManager modListManager,
            IModIncompatibilityService incompatibilityService,
            IDialogService dialogService,
            IModService modService)
        {
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
            DeactivateModCommand = new RelayCommand<ModItem>(mod => { _modListManager.DeactivateMod(mod); HasUnsavedChanges = true; }, mod => mod != null && !mod.IsCore && !_isLoading);
            DeleteModCommand = new RelayCommand<ModItem>(async mod => await ExecuteDeleteModAsync(mod), CanExecuteDeleteMod);
            OpenUrlCommand = new RelayCommand(OpenUrl, _ => SelectedMod != null);
            ImportListCommand = new RelayCommand(async _ => await ExecuteImport(), _ => !_isLoading);
            ExportListCommand = new RelayCommand(async _ => await ExecuteExport(), _ => !_isLoading && _modListManager.VirtualActiveMods.Any());
            CheckIncompatibilitiesCommand = new RelayCommand(async _ => await ExecuteCheckIncompatibilities(), _ => !_isLoading && _modListManager.VirtualActiveMods.Any());
            CheckDuplicatesCommand = new RelayCommand(_ => CheckForDuplicates());
            StripModsCommand = new RelayCommand(StripMods, _ => !_isLoading);
            CreatePackCommand = new RelayCommand(CreatePack, _ => !_isLoading);
            FixIntegrityCommand = new RelayCommand(FixIntegrity, _ => !_isLoading);
            RunGameCommand = new RelayCommand(RunGame, _ => !_isLoading);
            FilterInactiveCommand = new RelayCommand(ExecuteFilterInactive, _ => !_isLoading);
            FilterActiveCommand = new RelayCommand(ExecuteFilterActive, _ => !_isLoading);
            ResolveDependenciesCommand = new RelayCommand(async _ => await ExecuteResolveDependencies(), _ => !_isLoading);

            // Initialize New Commands
            OpenModFoldersCommand = new RelayCommand<IList>(OpenModFolders, selectedItems => selectedItems != null && selectedItems.Count > 0);
            OpenUrlsCommand = new RelayCommand<IList>(OpenUrls, selectedItems => selectedItems != null && selectedItems.Count > 0);
            OpenWorkshopPagesCommand = new RelayCommand<IList>(OpenWorkshopPages, selectedItems => selectedItems != null && selectedItems.Count > 0);
            OpenOtherUrlsCommand = new RelayCommand<IList>(OpenOtherUrls, selectedItems => selectedItems != null && selectedItems.Count > 0);
            DeleteModsCommand = new RelayCommand<IList>(async selectedItems => await ExecuteDeleteModsAsync(selectedItems), selectedItems => selectedItems != null && selectedItems.Count > 0 && !_isLoading);
        }

        public async Task RefreshDataAsync()
        {
            Debug.WriteLine("[RefreshDataAsync] Starting refresh...");
            await LoadDataAsync();
            Debug.WriteLine("[RefreshDataAsync] Refresh complete.");
        }

        private async Task LoadDataAsync()
        {
            IsLoading = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                var allMods = await _dataService.LoadAllModsAsync();
                var activeIdsFromConfig = _dataService.LoadActiveModIdsFromConfig();
                _modListManager.Initialize(allMods, activeIdsFromConfig);
                SelectedMod = _modListManager.VirtualActiveMods.FirstOrDefault(m => m.Mod.IsCore).Mod
                             ?? _modListManager.VirtualActiveMods.FirstOrDefault().Mod
                             ?? _modListManager.AllInactiveMods.FirstOrDefault();
                HasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading mods: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Loading Error", $"Error loading mods: {ex.Message}"));
                HasUnsavedChanges = false;
            }
            finally
            {
                IsLoading = false;
                CommandManager.InvalidateRequerySuggested();
                (DeleteModCommand as RelayCommand<ModItem>)?.RaiseCanExecuteChanged();
            }
        }

        private bool CanExecuteDeleteMod(ModItem mod)
        {
            return mod != null
                && !mod.IsCore
                && !mod.IsExpansion
                && !string.IsNullOrEmpty(mod.Path)
                && Directory.Exists(mod.Path)
                && !_isLoading;
        }

        private async Task ExecuteDeleteModAsync(ModItem mod)
        {
            if (!CanExecuteDeleteMod(mod))
            {
                Debug.WriteLine($"[ExecuteDeleteModAsync] Precondition failed for mod '{mod?.Name}'. Path: '{mod?.Path}' Exists: {Directory.Exists(mod?.Path ?? "")}");
                _dialogService.ShowWarning("Deletion Blocked", $"Cannot delete mod '{mod?.Name}'. It might be Core/DLC, already deleted, or the application is busy.");
                return;
            }

            var result = _dialogService.ShowConfirmation(
                "Confirm Deletion",
                $"Are you sure you want to permanently delete the mod '{mod.Name}'?\n\nThis action cannot be undone.\n\nPath: {mod.Path}",
                showCancel: true);

            if (result != MessageDialogResult.OK)
            {
                Debug.WriteLine($"[ExecuteDeleteModAsync] Deletion cancelled by user for mod '{mod.Name}'.");
                return;
            }

            IsLoading = true;
            CommandManager.InvalidateRequerySuggested();

            bool deletionSuccess = false;
            try
            {
                Debug.WriteLine($"[ExecuteDeleteModAsync] Attempting to delete directory: {mod.Path}");
                await Task.Run(() => Directory.Delete(mod.Path, recursive: true));
                deletionSuccess = true;
                Debug.WriteLine($"[ExecuteDeleteModAsync] Successfully deleted directory: {mod.Path}");
                _dialogService.ShowInformation("Deletion Successful", $"Mod '{mod.Name}' was deleted.");
            }
            catch (UnauthorizedAccessException authEx)
            {
                Debug.WriteLine($"[ExecuteDeleteModAsync] Authorization error deleting '{mod.Path}': {authEx.Message}");
                _dialogService.ShowError("Deletion Error", $"Permission denied when trying to delete mod '{mod.Name}'.\nCheck file permissions or if the folder is in use.\n\nError: {authEx.Message}");
            }
            catch (IOException ioEx)
            {
                Debug.WriteLine($"[ExecuteDeleteModAsync] IO error deleting '{mod.Path}': {ioEx.Message}");
                _dialogService.ShowError("Deletion Error", $"Could not delete mod '{mod.Name}'. The folder might be open or in use by another process.\n\nError: {ioEx.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExecuteDeleteModAsync] General error deleting '{mod.Path}': {ex}");
                _dialogService.ShowError("Deletion Error", $"An unexpected error occurred while deleting mod '{mod.Name}'.\n\nError: {ex.Message}");
            }
            finally
            {
                if (deletionSuccess)
                {
                    Debug.WriteLine("[ExecuteDeleteModAsync] Deletion successful, initiating mod list refresh.");
                    await RefreshDataAsync();
                }
                else
                {
                    IsLoading = false;
                    CommandManager.InvalidateRequerySuggested();
                    Debug.WriteLine("[ExecuteDeleteModAsync] Deletion failed, resetting loading state without full refresh.");
                }
            }
        }

        private void OpenModFolders(IList selectedItems)
        {
            var mods = selectedItems.Cast<ModItem>().ToList();
            var opened = new List<string>();
            var notOpened = new List<string>();

            foreach (var mod in mods)
            {
                if (!string.IsNullOrWhiteSpace(mod.Path) && Directory.Exists(mod.Path))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(mod.Path) { UseShellExecute = true });
                        opened.Add(mod.Name);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Could not open folder for {mod.Name}: {ex}");
                        notOpened.Add($"{mod.Name}: {ex.Message}");
                    }
                }
                else
                {
                    notOpened.Add($"{mod.Name}: Folder does not exist");
                }
            }

            if (notOpened.Count > 0)
            {
                var message = $"Opened folders for {opened.Count} mods.\n\nCould not open folders for the following mods:\n" + string.Join("\n", notOpened);
                _dialogService.ShowWarning("Open Folders", message);
            }
        }

        private void OpenUrls(IList selectedItems)
        {
            var mods = selectedItems.Cast<ModItem>().ToList();
            var opened = new List<string>();
            var notOpened = new List<string>();

            foreach (var mod in mods)
            {
                if (!string.IsNullOrWhiteSpace(mod.Url))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(mod.Url) { UseShellExecute = true });
                        opened.Add(mod.Name);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Could not open URL for {mod.Name}: {ex}");
                        notOpened.Add($"{mod.Name}: {ex.Message}");
                    }
                }
                else
                {
                    notOpened.Add($"{mod.Name}: No URL available");
                }
            }

            if (notOpened.Count > 0)
            {
                var message = $"Opened URLs for {opened.Count} mods.\n\nCould not open URLs for the following mods:\n" + string.Join("\n", notOpened);
                _dialogService.ShowWarning("Open URLs", message);
            }
        }

        private void OpenWorkshopPages(IList selectedItems)
        {
            var mods = selectedItems.Cast<ModItem>().ToList();
            var opened = new List<string>();
            var notOpened = new List<string>();

            foreach (var mod in mods)
            {
                if (!string.IsNullOrWhiteSpace(mod.SteamUrl))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(mod.SteamUrl) { UseShellExecute = true });
                        opened.Add(mod.Name);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Could not open workshop page for {mod.Name}: {ex}");
                        notOpened.Add($"{mod.Name}: {ex.Message}");
                    }
                }
                else
                {
                    notOpened.Add($"{mod.Name}: No workshop URL");
                }
            }

            if (notOpened.Count > 0)
            {
                var message = $"Opened workshop pages for {opened.Count} mods.\n\nCould not open workshop pages for the following mods:\n" + string.Join("\n", notOpened);
                _dialogService.ShowWarning("Open Workshop Pages", message);
            }
        }

        private void OpenOtherUrls(IList selectedItems)
        {
            var mods = selectedItems.Cast<ModItem>().ToList();
            var opened = new List<string>();
            var notOpened = new List<string>();

            foreach (var mod in mods)
            {
                if (!string.IsNullOrWhiteSpace(mod.ExternalUrl))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(mod.ExternalUrl) { UseShellExecute = true });
                        opened.Add(mod.Name);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Could not open external URL for {mod.Name}: {ex}");
                        notOpened.Add($"{mod.Name}: {ex.Message}");
                    }
                }
                else
                {
                    notOpened.Add($"{mod.Name}: No external URL");
                }
            }

            if (notOpened.Count > 0)
            {
                var message = $"Opened external URLs for {opened.Count} mods.\n\nCould not open external URLs for the following mods:\n" + string.Join("\n", notOpened);
                _dialogService.ShowWarning("Open External URLs", message);
            }
        }

        private async Task ExecuteDeleteModsAsync(IList selectedItems)
        {
            var mods = selectedItems.Cast<ModItem>().ToList();
            var deletableMods = mods.Where(m => !m.IsCore && !m.IsExpansion && !string.IsNullOrEmpty(m.Path) && Directory.Exists(m.Path)).ToList();
            var nonDeletableMods = mods.Except(deletableMods).ToList();

            if (deletableMods.Count == 0)
            {
                _dialogService.ShowWarning("Deletion Blocked", "None of the selected mods can be deleted. They might be core or expansion mods, or their folders do not exist.");
                return;
            }

            var confirmationMessage = new StringBuilder();
            confirmationMessage.AppendLine("Are you sure you want to permanently delete the following mods?");
            confirmationMessage.AppendLine();
            foreach (var mod in deletableMods)
            {
                confirmationMessage.AppendLine($"- {mod.Name}");
            }
            if (nonDeletableMods.Count > 0)
            {
                confirmationMessage.AppendLine();
                confirmationMessage.AppendLine("The following mods cannot be deleted because they are core or expansion mods, or their folders do not exist:");
                foreach (var mod in nonDeletableMods)
                {
                    confirmationMessage.AppendLine($"- {mod.Name}");
                }
            }

            var result = _dialogService.ShowConfirmation("Confirm Deletion", confirmationMessage.ToString(), showCancel: true);
            if (result != MessageDialogResult.OK)
            {
                return;
            }

            IsLoading = true;
            CommandManager.InvalidateRequerySuggested();

            var deletionResults = new List<string>();
            foreach (var mod in deletableMods)
            {
                try
                {
                    await Task.Run(() => Directory.Delete(mod.Path, recursive: true));
                    deletionResults.Add($"Successfully deleted {mod.Name}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to delete {mod.Name}: {ex}");
                    deletionResults.Add($"Failed to delete {mod.Name}: {ex.Message}");
                }
            }

            await RefreshDataAsync();

            var summaryMessage = string.Join("\n", deletionResults);
            _dialogService.ShowInformation("Deletion Summary", summaryMessage);
        }

        private void CheckForDuplicates()
        {
            var allMods = _modListManager.GetAllMods().ToList();
            var duplicateGroups = allMods
                .GroupBy(m => m.PackageId?.ToLowerInvariant() ?? string.Empty)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicateGroups.Any())
            {
                var dialog = new DuplicateModDialogViewModel(
                    duplicateGroups,
                    pathsToDelete => DeleteDuplicateMods(pathsToDelete),
                    () => { /* Cancel callback */ });

                var view = new DuplicateModDialogView(dialog)
                {
                    Owner = Application.Current.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                view.ShowDialog();
            }
            else
            {
                _dialogService.ShowInformation("No duplicate mods found", "No duplicates");
            }
        }

        private async void DeleteDuplicateMods(List<string> pathsToDelete)
        {
            if (pathsToDelete == null)
            {
                Debug.WriteLine("[DeleteDuplicateMods] ERROR: Received a null list.");
                _dialogService.ShowWarning("Deletion Warning", "Cannot perform deletion: received a null list of paths.");
                return;
            }
            Debug.WriteLine($"[DeleteDuplicateMods] Received list with {pathsToDelete.Count} items.");

            var successfullyDeletedMods = new List<ModItem>();
            var errorMessages = new List<string>();
            var allModsLookup = _modListManager.GetAllMods()
                                 .Where(m => !string.IsNullOrEmpty(m.Path))
                                 .ToDictionary(m => m.Path, m => m, StringComparer.OrdinalIgnoreCase);

            foreach (var path in pathsToDelete)
            {
                if (path == null)
                {
                    Debug.WriteLine("[DeleteDuplicateMods] !!! CRITICAL ERROR: Encountered a NULL path in the list during iteration !!!");
                    errorMessages.Add("Internal Error: Encountered a null path reference during deletion process.");
                    continue;
                }

                Debug.WriteLine($"[DeleteDuplicateMods] Processing path: '{path}'");
                allModsLookup.TryGetValue(path, out ModItem modInfo);
                string modIdentifier = modInfo != null ? $"{modInfo.Name} ({modInfo.PackageId})" : $"'{path}'";

                try
                {
                    if (Directory.Exists(path))
                    {
                        Debug.WriteLine($"[DeleteDuplicateMods] Directory exists. Attempting to delete {modIdentifier}...");
                        await Task.Run(() => Directory.Delete(path, true));
                        Debug.WriteLine($"[DeleteDuplicateMods] Successfully deleted {modIdentifier}.");
                        if (modInfo != null)
                        {
                            successfullyDeletedMods.Add(modInfo);
                        }
                        else
                        {
                            Debug.WriteLine($"[DeleteDuplicateMods] WARNING: Deleted path '{path}' but couldn't find associated ModItem info.");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[DeleteDuplicateMods] Directory does not exist or is not accessible, skipping: {modIdentifier}");
                    }
                }
                catch (IOException ioEx)
                {
                    Debug.WriteLine($"[DeleteDuplicateMods] IO Error deleting {modIdentifier}: {ioEx.Message}");
                    errorMessages.Add($"I/O Error deleting {modIdentifier}: {ioEx.Message}");
                }
                catch (UnauthorizedAccessException authEx)
                {
                    Debug.WriteLine($"[DeleteDuplicateMods] Access Error deleting {modIdentifier}: {authEx.Message}");
                    errorMessages.Add($"Access Error deleting {modIdentifier}: {authEx.Message}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DeleteDuplicateMods] General Error deleting {modIdentifier}: {ex}");
                    errorMessages.Add($"Error deleting {modIdentifier}: {ex.Message}");
                }
            }

            Debug.WriteLine($"[DeleteDuplicateMods] Finished deletion attempts. Successes: {successfullyDeletedMods.Count}, Errors: {errorMessages.Count}");

            var reportTitle = errorMessages.Any() ? "Deletion Complete with Errors" : "Deletion Complete";
            var reportMessage = new StringBuilder();
            var dialogType = MessageDialogType.Information;

            if (successfullyDeletedMods.Any())
            {
                reportMessage.AppendLine($"Successfully deleted {successfullyDeletedMods.Count} duplicate mod(s):");
                foreach (var mod in successfullyDeletedMods)
                {
                    reportMessage.AppendLine($"  - {mod.Name ?? "Unknown Name"}");
                    reportMessage.AppendLine($"    ID: {mod.PackageId ?? "N/A"}, Supported Versions: {GetSupportedVersionsString(mod)}, SteamID: {mod.SteamId ?? "N/A"}");
                }
                reportMessage.AppendLine();
            }
            else if (!errorMessages.Any())
            {
                reportMessage.AppendLine("No duplicate mods were deleted.");
                reportMessage.AppendLine();
            }

            if (errorMessages.Any())
            {
                dialogType = MessageDialogType.Warning;
                reportMessage.AppendLine("Errors encountered during deletion:");
                foreach (var error in errorMessages)
                {
                    reportMessage.AppendLine($"  - {error}");
                }
                reportMessage.AppendLine();
            }

            RunOnUIThread(() =>
            {
                _dialogService.ShowMessageWithCopy(reportTitle, reportMessage.ToString().Trim(), dialogType);
                _ = RefreshDataAsync();
                Debug.WriteLine("[DeleteDuplicateMods] Mod list refresh initiated after dialog.");
            });
        }

        private string GetSupportedVersionsString(ModItem mod)
        {
            if (mod.SupportedVersions == null || !mod.SupportedVersions.Any())
                return "N/A";
            return string.Join(", ", mod.SupportedVersions);
        }

        private async Task ExecuteResolveDependencies()
        {
            IsLoading = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                var result = await Task.Run(() => _modListManager.ResolveDependencies());
                var (addedMods, missingDependencies) = result;

                var message = new StringBuilder();

                if (addedMods.Count > 0)
                {
                    HasUnsavedChanges = true;
                    message.AppendLine("The following dependencies were automatically added:");
                    message.AppendLine();
                    foreach (var mod in addedMods)
                    {
                        message.AppendLine($"- {mod.Name} ({mod.PackageId})");
                    }
                    message.AppendLine();
                }

                if (missingDependencies.Count > 0)
                {
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
                }

                if (message.Length == 0)
                {
                    RunOnUIThread(() =>
                    {
                        _dialogService.ShowInformation("Dependencies Check",
                            "No missing dependencies found and no new dependencies were added.");
                        CommandManager.InvalidateRequerySuggested();
                    });
                }
                else if (missingDependencies.Count == 0)
                {
                    RunOnUIThread(() =>
                    {
                        _dialogService.ShowInformation("Dependencies Added",
                            message.ToString().TrimEnd());
                        CommandManager.InvalidateRequerySuggested();
                    });
                }
                else
                {
                    RunOnUIThread(() =>
                    {
                        _dialogService.ShowMessageWithCopy("Dependencies Status",
                            message.ToString().TrimEnd(),
                            MessageDialogType.Warning);
                        CommandManager.InvalidateRequerySuggested();
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error resolving dependencies: {ex}");
                RunOnUIThread(() =>
                {
                    _dialogService.ShowError("Resolution Error",
                        $"Error resolving dependencies: {ex.Message}");
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
            CommandManager.InvalidateRequerySuggested();
            try
            {
                var activeMods = _modListManager.VirtualActiveMods.Select(entry => entry.Mod).ToList();
                var incompatibilities = await Task.Run(() => _incompatibilityService.FindIncompatibilities(activeMods));

                if (incompatibilities.Count == 0)
                {
                    RunOnUIThread(() =>
                        _dialogService.ShowInformation("Compatibility Check", "No incompatibilities found between active mods.")
                    );
                    return;
                }

                var groups = await Task.Run(() => _incompatibilityService.GroupIncompatibilities(incompatibilities));

                if (groups.Count == 0)
                {
                    RunOnUIThread(() =>
                        _dialogService.ShowInformation("Compatibility Check", "No incompatibility groups to resolve.")
                    );
                    return;
                }

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
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void ShowIncompatibilityDialog(List<IncompatibilityGroup> groups)
        {
            var dialogViewModel = new ModIncompatibilityDialogViewModel(
                groups,
                ApplyIncompatibilityResolutions,
                () => { /* Cancel handler if needed */ }
            );

            var dialog = new ModIncompatibilityDialogView(dialogViewModel)
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            dialog.ShowDialog();
        }

        private void ApplyIncompatibilityResolutions(List<ModItem> modsToRemove)
        {
            if (modsToRemove == null || modsToRemove.Count == 0)
                return;

            try
            {
                foreach (var mod in modsToRemove)
                {
                    _modListManager.DeactivateMod(mod);
                }

                HasUnsavedChanges = true;
                _dialogService.ShowInformation("Incompatibilities Resolved", $"Successfully resolved incompatibilities by deactivating {modsToRemove.Count} mods.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying incompatibility resolutions: {ex}");
                _dialogService.ShowError("Resolution Error", $"Error applying incompatibility resolutions: {ex.Message}");
            }
        }

        private void OnModListManagerChanged(object sender, EventArgs e)
        {
            _filterService.UpdateCollections(
                _modListManager.VirtualActiveMods,
                _modListManager.AllInactiveMods
            );

            OnPropertyChanged(nameof(TotalActiveMods));
            OnPropertyChanged(nameof(TotalInactiveMods));

            HasUnsavedChanges = true;

            RunOnUIThread(() =>
            {
                CommandManager.InvalidateRequerySuggested();
                (DeleteModCommand as RelayCommand<ModItem>)?.RaiseCanExecuteChanged();
            });

            Debug.WriteLine("ModsViewModel handled ModListManager ListChanged event.");
        }

        private void SelectMod(object parameter)
        {
            if (parameter is ModItem mod) SelectedMod = mod;
        }

        private async Task ExecuteDropMod(DropModArgs args)
        {
            await _commandService.HandleDropCommand(args);
        }

        private async Task ExecuteClearActiveList()
        {
            await _commandService.ClearActiveModsAsync();
        }

        private async Task ExecuteSortActiveList()
        {
            IsLoading = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                await _commandService.SortActiveModsAsync();
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
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void ExecuteSaveMods()
        {
            IsLoading = true;
            try
            {
                var activeIdsToSave = _modListManager.VirtualActiveMods
                                        .Select(entry => entry.Mod.PackageId)
                                        .Where(id => !string.IsNullOrEmpty(id))
                                        .ToList();

                _dataService.SaveActiveModIdsToConfig(activeIdsToSave);
                HasUnsavedChanges = false;
                Debug.WriteLine("Mod list saved successfully.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving mods config: {ex}");
                RunOnUIThread(() =>
                    _dialogService.ShowError("Save Error", $"Failed to save mod list: {ex.Message}")
                );
            }
            finally
            {
                IsLoading = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task ExecuteImport()
        {
            await _ioService.ImportModListAsync();
        }

        private async Task ExecuteExport()
        {
            var activeModsToExport = _modListManager.VirtualActiveMods.Select(entry => entry.Mod).ToList();
            if (!activeModsToExport.Any())
            {
                _dialogService.ShowInformation("Export List", "There are no active mods to export.");
                return;
            }
            await _ioService.ExportModListAsync(activeModsToExport);
        }

        private void OpenUrl(object parameter)
        {
            string target = null;
            if (parameter is string urlParam)
            {
                target = urlParam;
            }
            else if (SelectedMod != null)
            {
                target = SelectedMod.Url;
                if (string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(SelectedMod.Path))
                {
                    target = SelectedMod.Path;
                }
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                Debug.WriteLine("OpenUrl: No target specified or derived.");
                _dialogService.ShowInformation("Information", "No URL or local path available for the selected mod.");
                return;
            }

            try
            {
                if (Directory.Exists(target))
                {
                    string fullPath = Path.GetFullPath(target);
                    Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
                }
                else
                {
                    var uriString = target;
                    if (!uriString.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !uriString.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        if (uriString.Contains('.') && !uriString.Contains(" ") && !uriString.Contains("\\"))
                        {
                            uriString = "http://" + uriString;
                        }
                        else
                        {
                            throw new InvalidOperationException($"Target '{target}' is not a recognized directory or URL format.");
                        }
                    }

                    Process.Start(new ProcessStartInfo(uriString) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not open path/URL '{target}': {ex}");
                _dialogService.ShowError("Error", $"Could not open path/URL: {ex.Message}");
            }
        }

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
                _modListManager.ListChanged -= OnModListManagerChanged;
            }
        }

        ~ModsViewModel()
        {
            Dispose(false);
        }
    }
}