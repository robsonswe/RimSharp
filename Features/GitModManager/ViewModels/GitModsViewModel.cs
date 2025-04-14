using RimSharp.Core.Commands;
using RimSharp.MyApp.AppFiles;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Features.ModManager.Services.Mangement; // Correct namespace? Check dependencies.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Diagnostics;
using LibGit2Sharp;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks; // Added for async command wrappers
using System.Threading;
using RimSharp.Core.Commands.Base; // Added for CancellationToken

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
        // Services remain the same
        private readonly IModService _modService;
        private readonly IModListManager _modListManager;
        private List<GitModItemWrapper> _gitMods;
        private string _statusMessage;
        private bool _isDisposed = false;
        private bool _isBusy; // Add busy flag for async operations

        public List<GitModItemWrapper> GitMods
        {
            get => _gitMods;
            set
            {
                 // Use base SetProperty
                if (SetProperty(ref _gitMods, value))
                {
                    Debug.WriteLine($"[DEBUG] GitModsViewModel: GitMods property SET. New Count: {(_gitMods?.Count ?? 0)}. PropertyChanged raised for GitMods.");
                    OnPropertyChanged(nameof(GitMods.Count));
                    
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
                    
                    _oldGitMods = _gitMods;
                }
            }
        }
        
        // Keep track of old items for proper event handling
        private List<GitModItemWrapper> _oldGitMods;

        // Event handler for GitModItemWrapper property changes
        private void GitModItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GitModItemWrapper.IsSelected))
            {
                // Update commands that depend on selection state
                RunOnUIThread(() => ((AsyncRelayCommand)PullUpdatesCommand).RaiseCanExecuteChanged());
                Debug.WriteLine("[DEBUG] Item selection changed, PullUpdatesCommand.CanExecute updated");
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value); // Use base SetProperty
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                // Use base SetProperty, observation handles command updates
                SetProperty(ref _isBusy, value);
            }
        }

        public ICommand CheckUpdatesCommand { get; }
        public ICommand PullUpdatesCommand { get; }
        public ICommand OpenGitHubRepoCommand { get; }

        public GitModsViewModel(IModService modService, IModListManager modListManager)
        {
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _modListManager = modListManager ?? throw new ArgumentNullException(nameof(modListManager));
            Debug.WriteLine("[DEBUG] GitModsViewModel: Constructor entered.");

            // Use base class helpers and observe IsBusy
            // Wrap synchronous logic in Task.Run for async commands
            CheckUpdatesCommand = CreateCancellableAsyncCommand(ExecuteCheckUpdatesAsync, CanExecuteCommands, nameof(IsBusy));
            PullUpdatesCommand = CreateCancellableAsyncCommand(ExecutePullUpdatesAsync, CanPullUpdates, nameof(IsBusy)); 
            OpenGitHubRepoCommand = CreateCommand<string>(OpenGitHubRepo, CanOpenGitHubRepo); // Sync, typed

            _modListManager.ListChanged += HandleModListChanged;
            LoadGitMods();
            Debug.WriteLine($"[DEBUG] GitModsViewModel: Constructor finished. Initial GitMods Count: {(_gitMods?.Count ?? 0)}");
        }

        // --- CanExecute Predicates ---
        private bool CanExecuteCommands() => !IsBusy;
        private bool CanPullUpdates() => !IsBusy && GitMods != null && GitMods.Any(m => m.IsSelected);

        // --- Execution Methods (Async Wrappers) ---

        private async Task ExecuteCheckUpdatesAsync(CancellationToken ct)
        {
             // CanExecute checked by framework
            IsBusy = true;
            StatusMessage = "Checking for updates...";
            try
            {
                // Run synchronous LibGit2Sharp code on background thread
                await Task.Run(() =>
                {
                    foreach (var modWrapper in GitMods)
                    {
                        ct.ThrowIfCancellationRequested(); // Check cancellation within the loop
                        var mod = modWrapper.ModItem;
                        try
                        {
                             // Use using statement for Repository
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
                             // Perform fetch
                             string logMessage = $"Fetching {remote.Name} for {mod.Name}...";
                             Debug.WriteLine(logMessage);
                             Commands.Fetch(repo, remote.Name, remote.FetchRefSpecs.Select(x => x.Specification), fetchOptions, logMessage);

                             var branch = repo.Head;
                             if (branch?.TrackedBranch == null)
                             {
                                 modWrapper.UpdateStatus = "No tracking branch";
                                 modWrapper.IsSelected = false;
                                 Debug.WriteLine($"[WARN] Mod '{mod.Name}' ({branch?.FriendlyName}) has no tracking branch.");
                                 continue;
                             }

                             var tracking = branch.TrackedBranch;
                             var divergence = repo.ObjectDatabase.CalculateHistoryDivergence(branch.Tip, tracking.Tip);

                             if (divergence.BehindBy.GetValueOrDefault() > 0) // Use GetValueOrDefault
                             {
                                 modWrapper.UpdateStatus = $"{divergence.BehindBy} update(s)";
                                 modWrapper.IsSelected = true;
                                 Debug.WriteLine($"[INFO] Mod '{mod.Name}' has {divergence.BehindBy} update(s).");
                             }
                             else if (divergence.AheadBy.GetValueOrDefault() > 0) // Handle local commits
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
                        catch (LibGit2SharpException gitEx) // Catch specific Git exceptions
                        {
                             modWrapper.UpdateStatus = $"Error: {gitEx.Message}";
                             modWrapper.IsSelected = false;
                             Debug.WriteLine($"[ERROR] CheckUpdates Git error for mod '{mod.Name}': {gitEx.Message}");
                        }
                        catch (Exception ex) // Catch general exceptions
                        {
                             modWrapper.UpdateStatus = "Error checking";
                             modWrapper.IsSelected = false;
                             Debug.WriteLine($"[ERROR] CheckUpdates failed for mod '{mod.Name}': {ex.Message}");
                        }
                    }
                }, ct); // Pass token to Task.Run

                 ct.ThrowIfCancellationRequested(); // Check after Task.Run completes

                 StatusMessage = "Update check complete.";
                 // Manually trigger CanExecuteChanged for PullUpdatesCommand as its CanExecute depends on IsSelected state
                 RunOnUIThread(() => ((AsyncRelayCommand)PullUpdatesCommand).RaiseCanExecuteChanged());
            }
            catch (OperationCanceledException)
            {
                 StatusMessage = "Update check cancelled.";
                 Debug.WriteLine("[ExecuteCheckUpdatesAsync] Operation cancelled.");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error checking updates: {ex.Message}";
                Debug.WriteLine($"[ERROR] Overall error during CheckUpdates: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }


        private async Task ExecutePullUpdatesAsync(CancellationToken ct)
        {
             // CanExecute checked by framework
            var selectedMods = GitMods?.Where(m => m.IsSelected).ToList();
            if (selectedMods == null || !selectedMods.Any())
            {
                 StatusMessage = "No mods selected for update.";
                 return;
            }

            IsBusy = true;
            StatusMessage = $"Pulling updates for {selectedMods.Count} mod(s)...";
            int successCount = 0;
            int failCount = 0;

            try
            {
                // Run synchronous LibGit2Sharp code on background thread
                await Task.Run(() =>
                {
                    foreach (var modWrapper in selectedMods)
                    {
                        ct.ThrowIfCancellationRequested(); // Check cancellation
                        var mod = modWrapper.ModItem;
                        try
                        {
                            using var repo = new Repository(mod.Path);
                            // Default signature - consider making configurable
                            var signature = new Signature("RimSharp Mod Manager", "rimsharp@example.com", DateTimeOffset.Now);
                            var pullOptions = new PullOptions
                            {
                                FetchOptions = new FetchOptions(), // Add credentials if needed
                                MergeOptions = new MergeOptions
                                {
                                     FastForwardStrategy = FastForwardStrategy.FastForwardOnly, // Default strategy
                                     // Consider adding conflict handling strategy if needed
                                }
                            };

                             Debug.WriteLine($"Pulling updates for mod '{mod.Name}'...");
                             MergeResult result = Commands.Pull(repo, signature, pullOptions);

                             if (result.Status == MergeStatus.UpToDate)
                             {
                                 modWrapper.UpdateStatus = "Already up to date";
                                 Debug.WriteLine($"[INFO] Mod '{mod.Name}' was already up to date.");
                             }
                             else if (result.Status == MergeStatus.FastForward || result.Status == MergeStatus.NonFastForward) // Consider NonFastForward success?
                             {
                                 modWrapper.UpdateStatus = "Updated successfully";
                                 successCount++;
                                 Debug.WriteLine($"[INFO] Pulled updates for mod '{mod.Name}'. Status: {result.Status}");
                             }
                             else // Conflicts or other errors
                             {
                                 modWrapper.UpdateStatus = $"Pull failed ({result.Status})";
                                 failCount++;
                                 Debug.WriteLine($"[ERROR] Pull failed for mod '{mod.Name}'. Status: {result.Status}");
                             }
                        }
                        catch (RepositoryNotFoundException)
                        {
                            modWrapper.UpdateStatus = "Error: Repository not found";
                            failCount++;
                            Debug.WriteLine($"[ERROR] PullUpdates repo not found for mod '{mod.Name}' at path {mod.Path}");
                        }
                        catch (LibGit2SharpException gitEx)
                        {
                            modWrapper.UpdateStatus = $"Pull error: {gitEx.Message}";
                            failCount++;
                            Debug.WriteLine($"[ERROR] PullUpdates Git error for mod '{mod.Name}': {gitEx.Message}");
                        }
                        catch (Exception ex)
                        {
                             modWrapper.UpdateStatus = "Pull error";
                             failCount++;
                             Debug.WriteLine($"[ERROR] PullUpdates failed for mod '{mod.Name}': {ex.Message}");
                        }
                        finally
                        {
                             // Reset selection state after attempt
                             modWrapper.IsSelected = false;
                        }
                    }
                }, ct); // Pass token to Task.Run

                 ct.ThrowIfCancellationRequested(); // Check after Task.Run

                 if (failCount > 0)
                    StatusMessage = $"Update pull finished. {successCount} succeeded, {failCount} failed.";
                 else
                    StatusMessage = $"Update pull finished. {successCount} succeeded.";

                 // Manually trigger CanExecuteChanged for PullUpdatesCommand as IsSelected changed
                 RunOnUIThread(() => ((AsyncRelayCommand)PullUpdatesCommand).RaiseCanExecuteChanged());

            }
            catch (OperationCanceledException)
            {
                 StatusMessage = "Update pull cancelled.";
                 Debug.WriteLine("[ExecutePullUpdatesAsync] Operation cancelled.");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error pulling updates: {ex.Message}";
                Debug.WriteLine($"[ERROR] Overall error during PullUpdates: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        // --- Other Methods (remain largely the same) ---

        private void OpenGitHubRepo(string gitRepo) // Typed parameter
        {
             // CanExecute checked by framework
            if (string.IsNullOrWhiteSpace(gitRepo)) return;

            try
            {
                string url = gitRepo.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? gitRepo
                    : $"https://github.com/{gitRepo}"; // Basic construction

                // Use ProcessStartInfo for better control and error handling potential
                ProcessStartInfo psi = new ProcessStartInfo(url)
                {
                    UseShellExecute = true // Important for opening URLs in default browser
                };
                Process.Start(psi);
                Debug.WriteLine($"[INFO] Opened GitHub repo: {url}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Failed to open GitHub repo {gitRepo}: {ex.Message}");
                StatusMessage = $"Failed to open GitHub repo: {ex.Message}";
                // Consider showing a dialog here as well
                // _dialogService.ShowError("Open Link Error", $"Could not open the link: {ex.Message}");
            }
        }

        private bool CanOpenGitHubRepo(string gitRepo) // Typed parameter
        {
            return !string.IsNullOrWhiteSpace(gitRepo) &&
                   (gitRepo.Contains("/") || Uri.IsWellFormedUriString(gitRepo, UriKind.Absolute));
        }

        private void HandleModListChanged(object sender, EventArgs e)
        {
            Debug.WriteLine("[DEBUG] GitModsViewModel: Received ListChanged event from ModListManager. Reloading Git mods.");
            // Consider running LoadGitMods on UI thread if it modifies GitMods collection directly
            RunOnUIThread(LoadGitMods);
        }

        private void LoadGitMods()
        {
            if (_isDisposed) return;

            Debug.WriteLine("[DEBUG] GitModsViewModel: LoadGitMods() called.");
            var allMods = _modListManager.GetAllMods(); // Assuming this is safe to call from any thread
            Debug.WriteLine($"[DEBUG] GitModsViewModel: _modListManager.GetAllMods() returned {allMods?.Count() ?? 0} total mods.");

            if (allMods == null)
            {
                GitMods = new List<GitModItemWrapper>();
                return;
            }

            var filteredMods = allMods
                .Where(m => m != null && m.ModType == ModType.Git)
                .Select(m => new GitModItemWrapper(m))
                .ToList();
            Debug.WriteLine($"[DEBUG] GitModsViewModel: Filtered down to {filteredMods.Count} Git mods.");
            GitMods = filteredMods; // Property setter handles notification

            // Refresh command states as list content might affect CanExecute
             RunOnUIThread(() => {
                 ((AsyncRelayCommand)CheckUpdatesCommand).RaiseCanExecuteChanged();
                 ((AsyncRelayCommand)PullUpdatesCommand).RaiseCanExecuteChanged();
             });
        }

        // Dispose method updated to clean up event handlers

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    if (_modListManager != null)
                    {
                        Debug.WriteLine("[DEBUG] GitModsViewModel: Disposing and unsubscribing from ListChanged.");
                        _modListManager.ListChanged -= HandleModListChanged;
                    }
                    
                    // Unsubscribe from all mod item property changes
                    if (_gitMods != null)
                    {
                        foreach (var mod in _gitMods)
                        {
                            mod.PropertyChanged -= GitModItem_PropertyChanged;
                        }
                    }
                }
                _isDisposed = true;
            }
        }

        ~GitModsViewModel()
        {
            Dispose(false);
        }
    }
}