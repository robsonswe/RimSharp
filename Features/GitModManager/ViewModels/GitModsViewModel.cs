using RimSharp.Core.Commands;
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Features.ModManager.Services.Management;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Diagnostics;
using LibGit2Sharp;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading;
using RimSharp.Core.Commands.Base;
using RimSharp.AppDir.Dialogs; // Added for ProgressDialogViewModel

namespace RimSharp.Features.GitModManager.ViewModels
{
    // Wrapper class for ModItem to add UI-specific properties
    public class GitModItemWrapper : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _updateStatus;

        public ModItem ModItem { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string UpdateStatus
        {
            get => _updateStatus;
            set
            {
                if (_updateStatus != value)
                {
                    _updateStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public GitModItemWrapper(ModItem modItem)
        {
            ModItem = modItem ?? throw new ArgumentNullException(nameof(modItem));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class GitModsViewModel : ViewModelBase, IDisposable
    {
        // --- Services ---
        private readonly IModService _modService;
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService;
        private readonly IGitService _gitService;

        // --- Private Fields ---
        private List<GitModItemWrapper> _gitMods;
        private string _statusMessage;
        private bool _isBusy;
        private List<GitModItemWrapper> _oldGitMods;

        // --- Properties ---
        public List<GitModItemWrapper> GitMods
        {
            get => _gitMods;
            set
            {
                if (SetProperty(ref _gitMods, value))
                {
                    OnPropertyChanged(nameof(GitMods.Count));

                    if (_oldGitMods != null)
                    {
                        foreach (var mod in _oldGitMods)
                        {
                            mod.PropertyChanged -= GitModItem_PropertyChanged;
                        }
                    }

                    if (_gitMods != null)
                    {
                        foreach (var mod in _gitMods)
                        {
                            mod.PropertyChanged += GitModItem_PropertyChanged;
                        }
                    }

                    _oldGitMods = _gitMods;
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        // --- Commands ---
        public ICommand CheckUpdatesCommand { get; }
        public ICommand PullUpdatesCommand { get; }
        public ICommand OpenGitHubRepoCommand { get; }

        // --- Constructor ---
        public GitModsViewModel(
            IModService modService,
            IModListManager modListManager,
            IDialogService dialogService,
            IGitService gitService)
        {
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _modListManager = modListManager ?? throw new ArgumentNullException(nameof(modListManager));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));

            CheckUpdatesCommand = CreateCancellableAsyncCommand(ExecuteCheckUpdatesAsync, CanExecuteCommands, nameof(IsBusy));
            PullUpdatesCommand = CreateCancellableAsyncCommand(ExecutePullUpdatesAsync, CanPullUpdates, nameof(IsBusy));
            OpenGitHubRepoCommand = CreateCommand<string>(OpenGitHubRepo, CanOpenGitHubRepo);

            _modListManager.ListChanged += HandleModListChanged;
            LoadGitMods();
        }

        // --- CanExecute Predicates ---
        private bool CanExecuteCommands() => !IsBusy;
        private bool CanPullUpdates() => !IsBusy && GitMods != null && GitMods.Any(m => m.IsSelected);
        private bool CanOpenGitHubRepo(string gitRepo)
        {
            return !string.IsNullOrWhiteSpace(gitRepo) &&
                   (gitRepo.Contains("/") || Uri.IsWellFormedUriString(gitRepo, UriKind.Absolute));
        }

        // --- Execution Methods ---

        private async Task ExecuteCheckUpdatesAsync(CancellationToken ct)
        {
            IsBusy = true;
            StatusMessage = "Checking for updates...";
            ProgressDialogViewModel progressViewModel = null;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                RunOnUIThread(() =>
                {
                    progressViewModel = _dialogService.ShowProgressDialog(
                        "Checking Mod Updates",
                        "Initializing update check...",
                        canCancel: true,
                        isIndeterminate: true,
                        cts: linkedCts,
                        closeable: false
                    );
                });

                await Task.Delay(50, CancellationToken.None);

                int totalMods = GitMods?.Count ?? 0;
                int processedCount = 0;

                if (GitMods != null && totalMods > 0)
                {
                    foreach (var modWrapper in GitMods)
                    {
                        linkedCts.Token.ThrowIfCancellationRequested();
                        processedCount++;
                        var mod = modWrapper.ModItem;

                        progressViewModel?.UpdateProgress(
                            (int)(((double)processedCount / totalMods) * 100),
                            $"Checking {mod.Name} ({processedCount}/{totalMods})..."
                        );

                        if (!_gitService.IsRepository(mod.Path))
                        {
                            modWrapper.UpdateStatus = "Not a git repository";
                            modWrapper.IsSelected = false;
                            continue;
                        }

                        try
                        {
                            await _gitService.FetchAsync(mod.Path, "origin", linkedCts.Token);
                            var div = await _gitService.GetDivergenceAsync(mod.Path, "origin", linkedCts.Token);

                            if (!div.IsValid)
                            {
                                modWrapper.UpdateStatus = div.ErrorMessage ?? "Error";
                                modWrapper.IsSelected = false;
                            }
                            else if (div.BehindBy > 0)
                            {
                                modWrapper.UpdateStatus = $"{div.BehindBy} update(s)";
                                modWrapper.IsSelected = true;
                            }
                            else if (div.AheadBy > 0)
                            {
                                modWrapper.UpdateStatus = $"Up to date ({div.AheadBy} local commits)";
                                modWrapper.IsSelected = false;
                            }
                            else
                            {
                                modWrapper.UpdateStatus = "Up to date";
                                modWrapper.IsSelected = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            modWrapper.UpdateStatus = "Error checking";
                            modWrapper.IsSelected = false;
                            Debug.WriteLine($"[ERROR] CheckUpdates failed for mod '{mod.Name}': {ex.Message}");
                        }
                    }
                }
                else
                {
                    progressViewModel?.UpdateProgress(100, "No Git mods found to check.");
                    await Task.Delay(500, CancellationToken.None);
                }

                StatusMessage = "Update check complete.";
                progressViewModel?.CompleteOperation(StatusMessage);
                RunOnUIThread(() => ((AsyncRelayCommand)PullUpdatesCommand).RaiseCanExecuteChanged());
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Update check cancelled.";
                progressViewModel?.ForceClose();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error checking updates: {ex.Message}";
                progressViewModel?.ForceClose();
            }
            finally
            {
                IsBusy = false;
                progressViewModel?.Dispose();
            }
        }

        private async Task ExecutePullUpdatesAsync(CancellationToken ct)
        {
            var selectedMods = GitMods?.Where(m => m.IsSelected).ToList();
            if (selectedMods == null || !selectedMods.Any())
            {
                StatusMessage = "No mods selected for update.";
                _dialogService.ShowInformation("Pull Updates", "No mods selected for update.");
                return;
            }

            IsBusy = true;
            StatusMessage = $"Pulling updates for {selectedMods.Count} mod(s)...";
            ProgressDialogViewModel progressViewModel = null;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            int successCount = 0;
            int failCount = 0;
            int totalToUpdate = selectedMods.Count;
            int processedCount = 0;

            try
            {
                RunOnUIThread(() =>
                {
                    progressViewModel = _dialogService.ShowProgressDialog(
                         "Pulling Mod Updates",
                         $"Preparing to pull {totalToUpdate} mod(s)...",
                         canCancel: true,
                         isIndeterminate: false,
                         cts: linkedCts,
                         closeable: false
                     );
                });
                await Task.Delay(50, CancellationToken.None);

                foreach (var modWrapper in selectedMods)
                {
                    linkedCts.Token.ThrowIfCancellationRequested();
                    processedCount++;
                    var mod = modWrapper.ModItem;

                    progressViewModel?.UpdateProgress(
                         (int)(((double)processedCount / totalToUpdate) * 100),
                         $"Pulling {mod.Name} ({processedCount}/{totalToUpdate})..."
                    );

                    try
                    {
                        var result = await _gitService.PullAsync(mod.Path, "origin", linkedCts.Token);
                        
                        modWrapper.UpdateStatus = result.Status switch
                        {
                            GitPullStatus.UpToDate => "Already up to date",
                            GitPullStatus.FastForward => "Updated successfully",
                            GitPullStatus.NonFastForward => "Updated (Non-FF)",
                            GitPullStatus.Conflict => "Error: Conflicts",
                            _ => result.Message ?? "Error pulling"
                        };

                        if (result.Status == GitPullStatus.FastForward || result.Status == GitPullStatus.NonFastForward)
                        {
                            successCount++;
                        }
                        else if (result.Status != GitPullStatus.UpToDate)
                        {
                            failCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        modWrapper.UpdateStatus = "Pull error";
                        failCount++;
                        Debug.WriteLine($"[ERROR] PullUpdates failed for mod '{mod.Name}': {ex}");
                    }
                    finally
                    {
                        RunOnUIThread(() => modWrapper.IsSelected = false);
                    }
                }

                if (failCount > 0)
                    StatusMessage = $"Update pull finished. {successCount} succeeded, {failCount} failed.";
                else if (successCount > 0)
                    StatusMessage = $"Update pull finished. {successCount} updated successfully.";
                else
                    StatusMessage = "Update pull finished. No mods required updating.";

                progressViewModel?.CompleteOperation(StatusMessage);
                RunOnUIThread(() => ((AsyncRelayCommand)PullUpdatesCommand).RaiseCanExecuteChanged());
            }
            catch (OperationCanceledException)
            {
                int remaining = totalToUpdate - processedCount;
                StatusMessage = $"Update pull cancelled by user. {successCount} succeeded, {failCount} failed, {remaining} skipped.";
                progressViewModel?.ForceClose();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error pulling updates: {ex.Message}";
                progressViewModel?.ForceClose();
            }
            finally
            {
                IsBusy = false;
                RunOnUIThread(() => ((AsyncRelayCommand)PullUpdatesCommand).RaiseCanExecuteChanged());
                progressViewModel?.Dispose();
            }
        }

        // --- Other Methods (remain largely the same) ---

        private void OpenGitHubRepo(string gitRepo) // Typed parameter
        {
            // CanExecute checked by framework
            if (string.IsNullOrWhiteSpace(gitRepo)) return;

            try
            {
                string url;
                // Handle different formats for the git repo string to construct a valid URL.
                if (gitRepo.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    // Case 1: Already a full URL (e.g., "https://github.com/user/repo")
                    url = gitRepo;
                }
                else if (gitRepo.Contains("github.com", StringComparison.OrdinalIgnoreCase))
                {
                    // Case 2: Contains domain but no protocol (e.g., "github.com/user/repo")
                    url = $"https://{gitRepo}";
                }
                else
                {
                    // Case 3: Assumed to be just the user/repo part (e.g., "user/repo")
                    url = $"https://github.com/{gitRepo}";
                }

                // Validate the constructed URL (optional but good practice)
                if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                {
                    Debug.WriteLine($"[WARN] Attempted to open invalid URL: {url}");
                    StatusMessage = $"Invalid repository URL format: {gitRepo}";
                    _dialogService.ShowWarning("Invalid URL", $"The repository URL '{url}' is not valid.");
                    return;
                }

                // Use ProcessStartInfo for better control and potential error handling
                ProcessStartInfo psi = new ProcessStartInfo(url)
                {
                    UseShellExecute = true // IMPORTANT: Must be true to open URL in default browser
                };
                Process.Start(psi);
                Debug.WriteLine($"[INFO] Opened GitHub repo: {url}");
            }
            catch (Win32Exception ex) // Catch specific exceptions like browser not found
            {
                Debug.WriteLine($"[ERROR] Failed to open GitHub repo {gitRepo}. Win32Exception: {ex.Message} (ErrorCode: {ex.ErrorCode})");
                StatusMessage = $"Could not open link. Is a default web browser configured?";
                _dialogService.ShowError("Open Link Error", $"Could not open the link: {ex.Message}\n\nPlease ensure you have a default web browser configured.");
            }
            catch (Exception ex) // Catch other potential exceptions
            {
                Debug.WriteLine($"[ERROR] Failed to open GitHub repo {gitRepo}: {ex}");
                StatusMessage = $"Failed to open GitHub repo: {ex.Message}";
                _dialogService.ShowError("Open Link Error", $"Could not open the link: {ex.Message}");
            }
        }

        private void HandleModListChanged(object sender, EventArgs e)
        {
            Debug.WriteLine("[DEBUG] GitModsViewModel: Received ListChanged event from ModListManager. Reloading Git mods.");
            // Run LoadGitMods on UI thread as it modifies the GitMods collection which is bound to the UI
            RunOnUIThread(LoadGitMods);
        }

        private void LoadGitMods()
        {
            if (_disposed) return;

            Debug.WriteLine("[DEBUG] GitModsViewModel: LoadGitMods() called.");
            try
            {
                var allMods = _modListManager.GetAllMods(); // Assuming this is safe to call from any thread
                Debug.WriteLine($"[DEBUG] GitModsViewModel: _modListManager.GetAllMods() returned {allMods?.Count() ?? 0} total mods.");

                if (allMods == null)
                {
                    GitMods = new List<GitModItemWrapper>(); // Assign empty list, setter handles notification
                    return;
                }

                var filteredMods = allMods
                    .Where(m => m != null && m.ModType == ModType.Git) // Filter for non-null Git mods
                    .Select(m => new GitModItemWrapper(m)) // Wrap them
                    .OrderBy(m => m.ModItem.Name) // Order alphabetically
                    .ToList();
                Debug.WriteLine($"[DEBUG] GitModsViewModel: Filtered down to {filteredMods.Count} Git mods.");

                // Important: Assign the new list to the property to trigger UI update
                GitMods = filteredMods;

                // Refresh command states as list content might affect CanExecute
                RunOnUIThread(() =>
                {
                    ((AsyncRelayCommand)CheckUpdatesCommand).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)PullUpdatesCommand).RaiseCanExecuteChanged();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] GitModsViewModel: Failed to load git mods: {ex}");
                StatusMessage = "Error loading Git mods list.";
                GitMods = new List<GitModItemWrapper>(); // Set to empty on error
            }
        }

        // Event handler for GitModItemWrapper property changes (like IsSelected)
        private void GitModItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GitModItemWrapper.IsSelected))
            {
                // Update commands that depend on selection state (PullUpdatesCommand)
                RunOnUIThread(() => ((AsyncRelayCommand)PullUpdatesCommand).RaiseCanExecuteChanged());
                Debug.WriteLine("[DEBUG] Item selection changed, PullUpdatesCommand.CanExecute updated");
            }
        }

        // --- Dispose Method ---

        protected override void Dispose(bool disposing)
        {
            // Check the base class flag BEFORE doing anything
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                // --- Derived Class Specific Cleanup ---
                Debug.WriteLine($"[GitModsViewModel] Disposing derived resources...");
                // Unsubscribe from managed events
                if (_modListManager != null)
                {
                    Debug.WriteLine("[GitModsViewModel] Unsubscribing from ListChanged.");
                    _modListManager.ListChanged -= HandleModListChanged;
                }

                // Unsubscribe from all mod item property changes
                if (_oldGitMods != null) // Use _oldGitMods as it holds the last assigned list
                {
                    Debug.WriteLine($"[GitModsViewModel] Unsubscribing from {_oldGitMods.Count} GitModItemWrapper instances.");
                    foreach (var mod in _oldGitMods)
                    {
                        mod.PropertyChanged -= GitModItem_PropertyChanged;
                    }
                    _oldGitMods = null; // Clear reference
                }
                _gitMods = null; // Clear reference

                // Note: Do not dispose injected services (_modService, _modListManager, _dialogService) here
                // --- End Derived Class Specific Cleanup ---
            }
            // Dispose unmanaged resources here if any (specific to GitModsViewModel)

            // IMPORTANT: Call the base class implementation LAST
            // This will dispose owned commands and set the _disposed flag
            Debug.WriteLine($"[GitModsViewModel] Calling base.Dispose({disposing}).");
            base.Dispose(disposing);
             Debug.WriteLine($"[GitModsViewModel] Finished Dispose({disposing}). _disposed = {_disposed}"); // Base sets the flag
        }

        // Finalizer (optional, good practice if using unmanaged resources directly)
        ~GitModsViewModel()
        {
            Dispose(false);
        }
    }
}