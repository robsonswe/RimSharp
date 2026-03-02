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
using RimSharp.AppDir.Dialogs;

namespace RimSharp.Features.GitModManager.ViewModels
{
    // Wrapper class for ModItem to add UI-specific properties
    public class GitModItemWrapper : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _updateStatus = string.Empty;

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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class GitModsViewModel : ViewModelBase, IGitModsViewModel, IDisposable
    {
        private readonly IModService _modService;
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService;
        private readonly IGitService _gitService;
        private List<GitModItemWrapper> _gitMods = new();
        private string _statusMessage = string.Empty;
        private bool _isBusy;
        private List<GitModItemWrapper>? _oldGitMods;
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

        public bool SelectAll
        {
            get => GitMods?.All(m => m.IsSelected) ?? false;
            set
            {
                if (GitMods != null)
                {
                    foreach (var mod in GitMods)
                    {
                        mod.IsSelected = value;
                    }
                    OnPropertyChanged();
                }
            }
        }
        public ICommand CheckUpdatesCommand { get; }
        public ICommand PullUpdatesCommand { get; }
        public ICommand OpenGitHubRepoCommand { get; }
        public ICommand CheckIndividualUpdateCommand { get; }
        public ICommand PullIndividualUpdateCommand { get; }
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
            OpenGitHubRepoCommand = CreateAsyncCommand<string>(OpenGitHubRepo, CanOpenGitHubRepo);

            CheckIndividualUpdateCommand = CreateAsyncCommand<GitModItemWrapper>(ExecuteCheckIndividualUpdateAsync, wrapper => !IsBusy && wrapper != null, nameof(IsBusy));
            PullIndividualUpdateCommand = CreateAsyncCommand<GitModItemWrapper>(ExecutePullIndividualUpdateAsync, wrapper => !IsBusy && wrapper != null && wrapper.IsSelected, nameof(IsBusy));

            _modListManager.ListChanged += HandleModListChanged;
            _ = Task.Run(LoadGitMods);
        }
        private bool CanExecuteCommands() => !IsBusy;
        private bool CanPullUpdates() => !IsBusy && GitMods != null && GitMods.Any(m => m.IsSelected);
        private bool CanOpenGitHubRepo(string gitRepo)
        {
            return !string.IsNullOrWhiteSpace(gitRepo) &&
                   (gitRepo.Contains("/") || Uri.IsWellFormedUriString(gitRepo, UriKind.Absolute));
        }

        private async Task ExecuteCheckIndividualUpdateAsync(GitModItemWrapper wrapper)
        {
            if (wrapper == null) return;

            IsBusy = true;
            StatusMessage = $"Checking {wrapper.ModItem.Name}...";
            try
            {
                var mod = wrapper.ModItem;
                if (!_gitService.IsRepository(mod.Path))
                {
                    wrapper.UpdateStatus = "Not a git repository";
                    wrapper.IsSelected = false;
                    return;
                }

                await _gitService.FetchAsync(mod.Path, "origin", CancellationToken.None);
                var div = await _gitService.GetDivergenceAsync(mod.Path, "origin", CancellationToken.None);

                if (!div.IsValid)
                {
                    wrapper.UpdateStatus = div.ErrorMessage ?? "Error";
                    wrapper.IsSelected = false;
                }
                else if (div.BehindBy > 0)
                {
                    wrapper.UpdateStatus = $"{div.BehindBy} update(s)";
                    wrapper.IsSelected = true;
                }
                else if (div.AheadBy > 0)
                {
                    wrapper.UpdateStatus = $"Up to date ({div.AheadBy} local commits)";
                    wrapper.IsSelected = false;
                }
                else
                {
                    wrapper.UpdateStatus = "Up to date";
                    wrapper.IsSelected = false;
                }
                StatusMessage = "Check complete.";
            }
            catch (Exception ex)
            {
                wrapper.UpdateStatus = "Error checking";
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                ((IDelegateCommand)PullUpdatesCommand).RaiseCanExecuteChanged();
            }
        }

        private async Task ExecutePullIndividualUpdateAsync(GitModItemWrapper wrapper)
        {
            if (wrapper == null) return;

            IsBusy = true;
            StatusMessage = $"Pulling {wrapper.ModItem.Name}...";
            try
            {
                var mod = wrapper.ModItem;
                var result = await _gitService.PullAsync(mod.Path, "origin", CancellationToken.None);

                wrapper.UpdateStatus = result.Status switch
                {
                    GitPullStatus.UpToDate => "Already up to date",
                    GitPullStatus.FastForward => "Updated successfully",
                    GitPullStatus.NonFastForward => "Updated (Non-FF)",
                    GitPullStatus.Conflict => "Error: Conflicts",
                    _ => result.Message ?? "Error pulling"
                };

                wrapper.IsSelected = false;
                StatusMessage = "Pull complete.";
            }
            catch (Exception ex)
            {
                wrapper.UpdateStatus = "Pull error";
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                ((IDelegateCommand)PullUpdatesCommand).RaiseCanExecuteChanged();
            }
        }

        private async Task ExecuteCheckUpdatesAsync(CancellationToken ct)
        {
            Debug.WriteLine("[GitModsVM] ExecuteCheckUpdatesAsync called");
            if (GitMods == null || GitMods.Count == 0)
            {
                Debug.WriteLine("[GitModsVM] No git mods available");
                StatusMessage = "No Git mods loaded yet. Please wait for the mod list to load.";
                await _dialogService.ShowInformation("Check Updates", "No Git mods are loaded yet. Please wait a moment for the mod list to load and try again.");
                return;
            }

            Debug.WriteLine($"[GitModsVM] Starting update check for {GitMods.Count} mods");
            IsBusy = true;
            StatusMessage = "Checking for updates...";
            ProgressDialogViewModel? progressViewModel = null;
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
                    Debug.WriteLine("[GitModsVM] Progress dialog shown");
                });

                await Task.Delay(50, CancellationToken.None);

                int totalMods = GitMods.Count;
                int processedCount = 0;

                Debug.WriteLine($"[GitModsVM] Processing {totalMods} mods");

                foreach (var m in GitMods) m.IsSelected = false;

                foreach (var modWrapper in GitMods)
                {
                    linkedCts.Token.ThrowIfCancellationRequested();
                    processedCount++;
                    var mod = modWrapper.ModItem;

                    Debug.WriteLine($"[GitModsVM] Checking mod: {mod.Name}");

                    progressViewModel?.UpdateProgress(
                        (int)(((double)processedCount / totalMods) * 100),
                        $"Checking {mod.Name} ({processedCount}/{totalMods})..."
                    );

                    if (!_gitService.IsRepository(mod.Path))
                    {
                        Debug.WriteLine($"[GitModsVM] {mod.Name} is not a git repository");
                        modWrapper.UpdateStatus = "Not a git repository";
                        continue;
                    }

                    try
                    {
                        Debug.WriteLine($"[GitModsVM] Fetching {mod.Name}");
                        await _gitService.FetchAsync(mod.Path, "origin", linkedCts.Token);

                        Debug.WriteLine($"[GitModsVM] Getting divergence for {mod.Name}");
                        var div = await _gitService.GetDivergenceAsync(mod.Path, "origin", linkedCts.Token);

                        if (!div.IsValid)
                        {
                            Debug.WriteLine($"[GitModsVM] {mod.Name} divergence invalid: {div.ErrorMessage}");
                            modWrapper.UpdateStatus = div.ErrorMessage ?? "Error";
                        }
                        else if (div.BehindBy > 0)
                        {
                            Debug.WriteLine($"[GitModsVM] {mod.Name} is behind by {div.BehindBy}");
                            modWrapper.UpdateStatus = $"{div.BehindBy} update(s)";
                            modWrapper.IsSelected = true;
                        }
                        else if (div.AheadBy > 0)
                        {
                            Debug.WriteLine($"[GitModsVM] {mod.Name} is ahead by {div.AheadBy}");
                            modWrapper.UpdateStatus = $"Up to date ({div.AheadBy} local commits)";
                        }
                        else
                        {
                            Debug.WriteLine($"[GitModsVM] {mod.Name} is up to date");
                            modWrapper.UpdateStatus = "Up to date";
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[GitModsVM] Error checking {mod.Name}: {ex.Message}");
                        modWrapper.UpdateStatus = "Error checking";
                    }
                }

                Debug.WriteLine("[GitModsVM] Update check complete");
                StatusMessage = "Update check complete.";
                progressViewModel?.CompleteOperation(StatusMessage);
                RunOnUIThread(() => ((AsyncRelayCommand)PullUpdatesCommand).RaiseCanExecuteChanged());
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[GitModsVM] Update check cancelled");
                StatusMessage = "Update check cancelled.";
                progressViewModel?.ForceClose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GitModsVM] Error in ExecuteCheckUpdatesAsync: {ex}");
                StatusMessage = $"Error checking updates: {ex.Message}";
                progressViewModel?.ForceClose();
            }
            finally
            {
                Debug.WriteLine("[GitModsVM] Setting IsBusy = false");
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
                await _dialogService.ShowInformation("Pull Updates", "No mods selected for update.");
                return;
            }

            IsBusy = true;
            StatusMessage = $"Pulling updates for {selectedMods.Count} mod(s)...";
            ProgressDialogViewModel? progressViewModel = null;
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

        private async Task OpenGitHubRepo(string gitRepo) // Typed parameter
        {
            // CanExecute checked by framework
            if (string.IsNullOrWhiteSpace(gitRepo)) return;

            try
            {
                string url;

                if (gitRepo.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    // Case 1: Already a full URL (e.g., "https://github.com/user/repo")
                    url = gitRepo;
                }
                else if (gitRepo.Contains("github.com", StringComparison.OrdinalIgnoreCase))
                {

                    url = $"https://{gitRepo}";
                }
                else
                {

                    url = $"https://github.com/{gitRepo}";
                }

                if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                {
                    Debug.WriteLine($"[WARN] Attempted to open invalid URL: {url}");
                    StatusMessage = $"Invalid repository URL format: {gitRepo}";
                    await _dialogService.ShowWarning("Invalid URL", $"The repository URL '{url}' is not valid.");
                    return;
                }

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
                await _dialogService.ShowError("Open Link Error", $"Could not open the link: {ex.Message}\n\nPlease ensure you have a default web browser configured.");
            }
            catch (Exception ex) // Catch other potential exceptions
            {
                Debug.WriteLine($"[ERROR] Failed to open GitHub repo {gitRepo}: {ex}");
                StatusMessage = $"Failed to open GitHub repo: {ex.Message}";
                await _dialogService.ShowError("Open Link Error", $"Could not open the link: {ex.Message}");
            }
        }

        private void HandleModListChanged(object? sender, EventArgs e)
        {
            Debug.WriteLine("[DEBUG] GitModsViewModel: Received ListChanged event from ModListManager. Reloading Git mods.");

            RunOnUIThread(LoadGitMods);
        }

        private void LoadGitMods()
        {
            if (_disposed) return;

            Debug.WriteLine("[GitModsVM] LoadGitMods() called");
            try
            {
                Debug.WriteLine("[GitModsVM] Getting all mods from manager");
                var allMods = _modListManager.GetAllMods();
                Debug.WriteLine($"[GitModsVM] GetAllMods() returned {allMods?.Count() ?? 0} total mods");

                if (allMods == null)
                {
                    Debug.WriteLine("[GitModsVM] allMods is null, setting empty list");
                    GitMods = new List<GitModItemWrapper>();
                    return;
                }

                Debug.WriteLine("[GitModsVM] Filtering for Git mods");
                var filteredMods = allMods
                    .Where(m => m != null && m.ModType == ModType.Git)
                    .Select(m => new GitModItemWrapper(m))
                    .OrderBy(m => m.ModItem.Name)
                    .ToList();
                Debug.WriteLine($"[GitModsVM] Filtered to {filteredMods.Count} Git mods");

                GitMods = filteredMods;
                Debug.WriteLine($"[GitModsVM] GitMods property set, Count = {GitMods.Count}");

                RunOnUIThread(() =>
                {
                    ((AsyncRelayCommand)CheckUpdatesCommand).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)PullUpdatesCommand).RaiseCanExecuteChanged();
                    Debug.WriteLine("[GitModsVM] Commands raised CanExecuteChanged");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GitModsVM] ERROR in LoadGitMods: {ex}");
                StatusMessage = "Error loading Git mods list.";
                GitMods = new List<GitModItemWrapper>();
            }
        }
        private void GitModItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GitModItemWrapper.IsSelected))
            {
                RunOnUIThread(() => 
                {
                    ((IDelegateCommand)PullUpdatesCommand).RaiseCanExecuteChanged();
                    ((IDelegateCommand<GitModItemWrapper>)PullIndividualUpdateCommand).RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(SelectAll));
                });
                Debug.WriteLine("[DEBUG] Item selection changed, PullUpdatesCommand.CanExecute updated");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                Debug.WriteLine($"[GitModsViewModel] Disposing derived resources...");

                if (_modListManager != null)
                {
                    Debug.WriteLine("[GitModsViewModel] Unsubscribing from ListChanged.");
                    _modListManager.ListChanged -= HandleModListChanged;
                }

                if (_oldGitMods != null) // Use _oldGitMods as it holds the last assigned list
                {
                    Debug.WriteLine($"[GitModsViewModel] Unsubscribing from {_oldGitMods.Count} GitModItemWrapper instances.");
                    foreach (var mod in _oldGitMods)
                    {
                        mod.PropertyChanged -= GitModItem_PropertyChanged;
                    }
                    _oldGitMods = null;
                }
                _gitMods = new List<GitModItemWrapper>();

            }

// IMPORTANT: Call the base class implementation LAST

            Debug.WriteLine($"[GitModsViewModel] Calling base.Dispose({disposing}).");
            base.Dispose(disposing);
             Debug.WriteLine($"[GitModsViewModel] Finished Dispose({disposing}). _disposed = {_disposed}"); // Base sets the flag
        }

        ~GitModsViewModel()
        {
            Dispose(false);
        }
    }
}


