using RimSharp.Core.Commands;
using RimSharp.MyApp.AppFiles;
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
using RimSharp.MyApp.Dialogs; // Added for ProgressDialogViewModel

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
        private readonly IDialogService _dialogService; // Added for progress dialogs

        // --- Private Fields ---
        private List<GitModItemWrapper> _gitMods;
        private string _statusMessage;
        private bool _isBusy;
        private List<GitModItemWrapper> _oldGitMods; // Keep track of old items for proper event handling

        // --- Properties ---
        public List<GitModItemWrapper> GitMods
        {
            get => _gitMods;
            set
            {
                if (SetProperty(ref _gitMods, value))
                {
                    Debug.WriteLine($"[DEBUG] GitModsViewModel: GitMods property SET. New Count: {(_gitMods?.Count ?? 0)}. PropertyChanged raised for GitMods.");
                    OnPropertyChanged(nameof(GitMods.Count)); // Notify count change if needed

                    // Unsubscribe from old items
                    if (_oldGitMods != null)
                    {
                        foreach (var mod in _oldGitMods)
                        {
                            mod.PropertyChanged -= GitModItem_PropertyChanged;
                        }
                    }

                    // Subscribe to new items
                    if (_gitMods != null)
                    {
                        foreach (var mod in _gitMods)
                        {
                            mod.PropertyChanged += GitModItem_PropertyChanged;
                        }
                    }

                    _oldGitMods = _gitMods; // Update the old list reference
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
            private set => SetProperty(ref _isBusy, value); // Command CanExecute updated via observation
        }

        // --- Commands ---
        public ICommand CheckUpdatesCommand { get; }
        public ICommand PullUpdatesCommand { get; }
        public ICommand OpenGitHubRepoCommand { get; }

        // --- Constructor ---
        public GitModsViewModel(
            IModService modService,
            IModListManager modListManager,
            IDialogService dialogService) // Inject IDialogService
        {
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _modListManager = modListManager ?? throw new ArgumentNullException(nameof(modListManager));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService)); // Store injected service

            Debug.WriteLine("[DEBUG] GitModsViewModel: Constructor entered.");

            CheckUpdatesCommand = CreateCancellableAsyncCommand(ExecuteCheckUpdatesAsync, CanExecuteCommands, nameof(IsBusy));
            PullUpdatesCommand = CreateCancellableAsyncCommand(ExecutePullUpdatesAsync, CanPullUpdates, nameof(IsBusy));
            OpenGitHubRepoCommand = CreateCommand<string>(OpenGitHubRepo, CanOpenGitHubRepo); // Sync, typed

            _modListManager.ListChanged += HandleModListChanged;
            LoadGitMods(); // Load initial mods
            Debug.WriteLine($"[DEBUG] GitModsViewModel: Constructor finished. Initial GitMods Count: {(_gitMods?.Count ?? 0)}");
        }

        // --- CanExecute Predicates ---
        private bool CanExecuteCommands() => !IsBusy;
        private bool CanPullUpdates() => !IsBusy && GitMods != null && GitMods.Any(m => m.IsSelected);
        private bool CanOpenGitHubRepo(string gitRepo) // Typed parameter
        {
            return !string.IsNullOrWhiteSpace(gitRepo) &&
                   (gitRepo.Contains("/") || Uri.IsWellFormedUriString(gitRepo, UriKind.Absolute));
        }

        // --- Execution Methods (Async Wrappers with Progress Dialog) ---

        private async Task ExecuteCheckUpdatesAsync(CancellationToken ct)
        {
            IsBusy = true;
            StatusMessage = "Checking for updates...";
            ProgressDialogViewModel progressViewModel = null;
            // Create a linked CTS so the dialog's cancel button cancels the command's token
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                // Show Progress Dialog on UI Thread before starting background work
                RunOnUIThread(() =>
                {
                    progressViewModel = _dialogService.ShowProgressDialog(
                        "Checking Mod Updates",
                        "Initializing update check...",
                        canCancel: true,
                        isIndeterminate: true, // Start as indeterminate
                        cts: linkedCts, // Pass the LINKED CTS
                        closeable: false // Don't allow user to close via 'X' until done/cancelled
                    );
                });

                // Wait a tiny moment to ensure the dialog appears before heavy work starts
                await Task.Delay(50, CancellationToken.None); // Use CancellationToken.None here

                int totalMods = GitMods?.Count ?? 0;
                int processedCount = 0;

                // Run synchronous LibGit2Sharp code on background thread
                await Task.Run(async () => // Make inner lambda async to allow delays
                {
                    if (GitMods == null || totalMods == 0)
                    {
                        // Update dialog if no mods found
                        progressViewModel?.UpdateProgress(100, "No Git mods found to check.");
                        await Task.Delay(500, linkedCts.Token); // Short delay to see the message
                        return;
                    }

                    foreach (var modWrapper in GitMods)
                    {
                        // Use the linked token for cancellation checks within the background task
                        linkedCts.Token.ThrowIfCancellationRequested();
                        processedCount++;
                        var mod = modWrapper.ModItem;

                        // Update Progress Dialog (ensure UI thread)
                        progressViewModel?.UpdateProgress(
                            (int)(((double)processedCount / totalMods) * 100), // Calculate percentage
                            $"Checking {mod.Name} ({processedCount}/{totalMods})..."
                        );
                        // Optionally add a small delay if updates are too fast
                        // await Task.Delay(10, linkedCts.Token);

                        try
                        {
                            using var repo = new Repository(mod.Path);
                            var remote = repo.Network.Remotes["origin"];
                            if (remote == null)
                            {
                                modWrapper.UpdateStatus = "Remote 'origin' not found";
                                modWrapper.IsSelected = false;
                                Debug.WriteLine($"[WARN] Mod '{mod.Name}' has no remote 'origin'.");
                                continue;
                            }

                            var fetchOptions = new FetchOptions(); // Add credentials if needed
                            string logMessage = $"Fetching {remote.Name} for {mod.Name}...";
                            Debug.WriteLine(logMessage);
                            // Fetch can take time, check cancellation before/after if possible (not directly supported by LibGit2Sharp Fetch)
                            Commands.Fetch(repo, remote.Name, remote.FetchRefSpecs.Select(x => x.Specification), fetchOptions, logMessage);
                            linkedCts.Token.ThrowIfCancellationRequested(); // Check after fetch

                            var branch = repo.Head;
                            if (branch?.TrackedBranch == null)
                            {
                                modWrapper.UpdateStatus = "No tracking branch";
                                modWrapper.IsSelected = false;
                                Debug.WriteLine($"[WARN] Mod '{mod.Name}' ({branch?.FriendlyName}) has no tracking branch.");
                                continue;
                            }

                            var tracking = branch.TrackedBranch;
                            // Calculation can be quick, but check token anyway
                            linkedCts.Token.ThrowIfCancellationRequested();
                            var divergence = repo.ObjectDatabase.CalculateHistoryDivergence(branch.Tip, tracking.Tip);

                            if (divergence.BehindBy.GetValueOrDefault() > 0)
                            {
                                modWrapper.UpdateStatus = $"{divergence.BehindBy} update(s)";
                                modWrapper.IsSelected = true;
                                Debug.WriteLine($"[INFO] Mod '{mod.Name}' has {divergence.BehindBy} update(s).");
                            }
                            else if (divergence.AheadBy.GetValueOrDefault() > 0)
                            {
                                modWrapper.UpdateStatus = $"Up to date ({divergence.AheadBy} local commits)";
                                modWrapper.IsSelected = false;
                                Debug.WriteLine($"[INFO] Mod '{mod.Name}' is up to date ({divergence.AheadBy} local commits).");
                            }
                            else
                            {
                                modWrapper.UpdateStatus = "Up to date";
                                modWrapper.IsSelected = false;
                                Debug.WriteLine($"[INFO] Mod '{mod.Name}' is up to date.");
                            }
                        }
                        catch (RepositoryNotFoundException)
                        {
                            modWrapper.UpdateStatus = "Error: Repository not found";
                            modWrapper.IsSelected = false;
                            Debug.WriteLine($"[ERROR] CheckUpdates repo not found for mod '{mod.Name}' at path {mod.Path}");
                        }
                        catch (LibGit2SharpException gitEx)
                        {
                            modWrapper.UpdateStatus = $"Error: {gitEx.Message}";
                            modWrapper.IsSelected = false;
                            Debug.WriteLine($"[ERROR] CheckUpdates Git error for mod '{mod.Name}': {gitEx.Message}");
                        }
                        catch (OperationCanceledException) // Catch cancellation within the loop
                        {
                            Debug.WriteLine($"[ExecuteCheckUpdatesAsync] Cancellation requested while processing {mod?.Name}.");
                            throw; // Re-throw to be caught by the outer handler
                        }
                        catch (Exception ex)
                        {
                            modWrapper.UpdateStatus = "Error checking";
                            modWrapper.IsSelected = false;
                            Debug.WriteLine($"[ERROR] CheckUpdates failed for mod '{mod.Name}': {ex.Message}");
                        }
                    }
                }, linkedCts.Token); // Pass the linked token to Task.Run

                // If completed without cancellation:
                StatusMessage = "Update check complete.";
                progressViewModel?.CompleteOperation(StatusMessage); // Close dialog successfully
                // Manually trigger CanExecuteChanged for PullUpdatesCommand as its CanExecute depends on IsSelected state changes during check
                RunOnUIThread(() => ((AsyncRelayCommand)PullUpdatesCommand).RaiseCanExecuteChanged());
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Update check cancelled.";
                Debug.WriteLine("[ExecuteCheckUpdatesAsync] Operation cancelled by user.");
                // Dialog should close itself via its CancelCommand/CTS link, but ForceClose ensures it if something went wrong.
                progressViewModel?.ForceClose();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error checking updates: {ex.Message}";
                Debug.WriteLine($"[ERROR] Overall error during CheckUpdates: {ex.Message}");
                // Show error in dialog before closing (optional)
                if (progressViewModel != null)
                {
                    progressViewModel.Message = $"Error: {ex.Message.Split('\n')[0]}"; // Update message directly
                    // Note: CanCancel setter is private in ProgressDialogViewModel,
                    // so we cannot set it directly here. ForceClose will handle closure state.
                }
                await Task.Delay(2000); // Give user time to see error in dialog
                progressViewModel?.ForceClose(); // Close dialog on failure
            }
            finally
            {
                IsBusy = false;
                // Ensure the view model is disposed EVEN IF ShowProgressDialog failed or dialog was closed
                progressViewModel?.Dispose();
            }
        }

        private async Task ExecutePullUpdatesAsync(CancellationToken ct)
        {
            var selectedMods = GitMods?.Where(m => m.IsSelected).ToList();
            if (selectedMods == null || !selectedMods.Any())
            {
                StatusMessage = "No mods selected for update.";
                // Optionally show a quick info dialog:
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
                // Show Progress Dialog on UI Thread
                RunOnUIThread(() =>
                {
                    progressViewModel = _dialogService.ShowProgressDialog(
                         "Pulling Mod Updates",
                         $"Preparing to pull {totalToUpdate} mod(s)...",
                         canCancel: true,
                         isIndeterminate: false, // We can show progress per mod
                         cts: linkedCts,
                         closeable: false
                     );
                });
                await Task.Delay(50, CancellationToken.None); // Allow dialog to render

                // Run synchronous LibGit2Sharp code on background thread
                await Task.Run(() =>
                {
                    foreach (var modWrapper in selectedMods)
                    {
                        linkedCts.Token.ThrowIfCancellationRequested();
                        processedCount++;
                        var mod = modWrapper.ModItem;

                        // Update Progress Dialog
                        progressViewModel?.UpdateProgress(
                             (int)(((double)processedCount / totalToUpdate) * 100),
                             $"Pulling {mod.Name} ({processedCount}/{totalToUpdate})..."
                        );
                        // Optionally add a small delay
                        // await Task.Delay(10, linkedCts.Token);

                        try
                        {
                            using var repo = new Repository(mod.Path);
                            // Consider making signature configurable in settings
                            var signature = new Signature("RimSharp Mod Manager", "rimsharp@example.com", DateTimeOffset.Now);
                            var pullOptions = new PullOptions
                            {
                                FetchOptions = new FetchOptions(), // Add credentials if needed
                                MergeOptions = new MergeOptions
                                {
                                    FastForwardStrategy = FastForwardStrategy.FastForwardOnly, // Safest default
                                    // Consider adding conflict handling strategy if needed (e.g., FailOnConflict)
                                    // FileConflictStrategy = CheckoutFileConflictStrategy.Fail
                                }
                            };

                            Debug.WriteLine($"Pulling updates for mod '{mod.Name}'...");
                            // Pull can take time
                            MergeResult result = Commands.Pull(repo, signature, pullOptions);
                            linkedCts.Token.ThrowIfCancellationRequested(); // Check after pull

                            if (result.Status == MergeStatus.UpToDate)
                            {
                                modWrapper.UpdateStatus = "Already up to date";
                                Debug.WriteLine($"[INFO] Mod '{mod.Name}' was already up to date.");
                                // Don't increment successCount here, as nothing changed.
                            }
                            else if (result.Status == MergeStatus.FastForward || result.Status == MergeStatus.NonFastForward) // Consider NonFastForward success? Usually yes.
                            {
                                modWrapper.UpdateStatus = "Updated successfully";
                                successCount++;
                                Debug.WriteLine($"[INFO] Pulled updates for mod '{mod.Name}'. Status: {result.Status}");
                            }
                            else // Conflicts or other errors
                            {
                                modWrapper.UpdateStatus = $"Pull failed ({result.Status})";
                                failCount++;
                                Debug.WriteLine($"[ERROR] Pull failed for mod '{mod.Name}'. Status: {result.Status}. Commit: {result.Commit?.Id.Sha ?? "N/A"}");
                                // Optionally add more details if result.Commit exists
                            }
                        }
                        catch (RepositoryNotFoundException)
                        {
                            modWrapper.UpdateStatus = "Error: Repository not found";
                            failCount++;
                            Debug.WriteLine($"[ERROR] PullUpdates repo not found for mod '{mod.Name}' at path {mod.Path}");
                        }
                        catch (LibGit2SharpException gitEx) // Catch specific Git errors first
                        {
                            // Provide more specific feedback if possible
                            if (gitEx is NonFastForwardException)
                            {
                                modWrapper.UpdateStatus = "Pull failed: Non-fast-forward";
                                Debug.WriteLine($"[ERROR] Pull failed for '{mod.Name}': Non-fast-forward merge required. {gitEx.Message}");
                            }
                            else if (gitEx is CheckoutConflictException conflictEx)
                            {
                                modWrapper.UpdateStatus = "Pull failed: Conflicts";
                                Debug.WriteLine($"[ERROR] Pull failed for '{mod.Name}': Conflicts detected. {conflictEx.Message}");
                            }
                            else
                            {
                                modWrapper.UpdateStatus = $"Pull error: {gitEx.Message.Split('\n')[0]}"; // First line often most useful
                                Debug.WriteLine($"[ERROR] PullUpdates Git error for mod '{mod.Name}': {gitEx}"); // Log full exception
                            }
                            failCount++;
                        }
                        catch (OperationCanceledException) // Catch cancellation within the loop
                        {
                            Debug.WriteLine($"[ExecutePullUpdatesAsync] Cancellation requested while pulling {mod?.Name}.");
                            modWrapper.UpdateStatus = "Pull cancelled";
                            failCount++; // Count cancelled item as failure
                            throw; // Re-throw to stop processing further items
                        }
                        catch (Exception ex) // Catch any other unexpected errors
                        {
                            modWrapper.UpdateStatus = "Pull error (unexpected)";
                            failCount++;
                            Debug.WriteLine($"[ERROR] PullUpdates unexpected error for mod '{mod.Name}': {ex}");
                        }
                        finally
                        {
                            // Reset selection state immediately after attempt (on UI thread)
                            // This ensures the checkbox unchecks even if an error occurred
                            RunOnUIThread(() => modWrapper.IsSelected = false);
                        }
                    }
                }, linkedCts.Token); // Pass linked token to Task.Run

                // Update final status message after loop completes naturally
                if (failCount > 0)
                    StatusMessage = $"Update pull finished. {successCount} succeeded, {failCount} failed.";
                else if (successCount > 0)
                    StatusMessage = $"Update pull finished. {successCount} updated successfully.";
                else
                    StatusMessage = "Update pull finished. No mods required updating."; // If successCount == 0 and failCount == 0

                progressViewModel?.CompleteOperation(StatusMessage); // Close dialog successfully
                // CanExecute state depends on IsSelected, which was modified in the loop, trigger update.
                RunOnUIThread(() => ((AsyncRelayCommand)PullUpdatesCommand).RaiseCanExecuteChanged());

            }
            catch (OperationCanceledException)
            {
                // Update status for partial completion
                int remaining = totalToUpdate - processedCount; // Items not even attempted
                StatusMessage = $"Update pull cancelled by user. {successCount} succeeded, {failCount} failed, {remaining} skipped.";
                Debug.WriteLine("[ExecutePullUpdatesAsync] Operation cancelled by user.");
                progressViewModel?.ForceClose(); // Ensure dialog closes
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error pulling updates: {ex.Message}";
                Debug.WriteLine($"[ERROR] Overall error during PullUpdates: {ex.Message}");
                // Show error message in dialog before closing
                if (progressViewModel != null)
                {
                    progressViewModel.Message = $"Error: {ex.Message.Split('\n')[0]}"; // Update message directly
                                                                                       // Note: CanCancel setter is private.
                }
                await Task.Delay(2000); // Allow user to see error
                progressViewModel?.ForceClose();
            }
            finally
            {
                IsBusy = false;
                // This 'finally' block runs *after* the catch blocks.
                // The IsSelected state should already be false due to the inner finally.
                // We still need to trigger CanExecuteChanged in case cancellation happened *before* the loop started.
                RunOnUIThread(() => ((AsyncRelayCommand)PullUpdatesCommand).RaiseCanExecuteChanged());
                progressViewModel?.Dispose(); // Dispose the dialog VM
            }
        }

        // --- Other Methods (remain largely the same) ---

        private void OpenGitHubRepo(string gitRepo) // Typed parameter
        {
            // CanExecute checked by framework
            if (string.IsNullOrWhiteSpace(gitRepo)) return;

            try
            {
                // Basic URL construction, assumes github.com if not a full URL
                string url = gitRepo.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? gitRepo
                    : $"https://github.com/{gitRepo}";

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