using RimSharp.Core.Commands;
using RimSharp.MyApp.AppFiles;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Features.ModManager.Services.Mangement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Diagnostics;
using LibGit2Sharp;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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
        private readonly IModService _modService;
        private readonly IModListManager _modListManager;
        private List<GitModItemWrapper> _gitMods;
        private string _statusMessage;
        private bool _isDisposed = false;

        public List<GitModItemWrapper> GitMods
        {
            get => _gitMods;
            set
            {
                if (SetProperty(ref _gitMods, value))
                {
                    Debug.WriteLine($"[DEBUG] GitModsViewModel: GitMods property SET. New Count: {(_gitMods?.Count ?? 0)}. PropertyChanged raised for GitMods.");
                    OnPropertyChanged(nameof(GitMods.Count));
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand CheckUpdatesCommand { get; }
        public ICommand PullUpdatesCommand { get; }
        public ICommand OpenGitHubRepoCommand { get; }

        public GitModsViewModel(IModService modService, IModListManager modListManager)
        {
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _modListManager = modListManager ?? throw new ArgumentNullException(nameof(modListManager));
            Debug.WriteLine("[DEBUG] GitModsViewModel: Constructor entered.");

            CheckUpdatesCommand = new RelayCommand(CheckUpdates);
            PullUpdatesCommand = new RelayCommand(PullUpdates, CanPullUpdates);
            OpenGitHubRepoCommand = new RelayCommand<string>(OpenGitHubRepo, CanOpenGitHubRepo);

            _modListManager.ListChanged += HandleModListChanged;
            LoadGitMods();
            Debug.WriteLine($"[DEBUG] GitModsViewModel: Constructor finished. Initial GitMods Count: {(_gitMods?.Count ?? 0)}");
        }

        private void OpenGitHubRepo(string gitRepo)
        {
            if (!CanOpenGitHubRepo(gitRepo)) return;

            try
            {
                // Handle cases where gitRepo might already be a full URL
                string url = gitRepo.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? gitRepo
                    : $"https://github.com/{gitRepo}";

                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                Debug.WriteLine($"[INFO] Opened GitHub repo: {url}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Failed to open GitHub repo {gitRepo}: {ex.Message}");
                StatusMessage = $"Failed to open GitHub repo: {ex.Message}";
            }
        }


        private bool CanOpenGitHubRepo(string gitRepo)
        {
            // More thorough validation
            return !string.IsNullOrWhiteSpace(gitRepo) &&
                   (gitRepo.Contains("/") ||  // basic check for owner/repo format
                    Uri.IsWellFormedUriString(gitRepo, UriKind.Absolute)); // or full URL
        }
        private void HandleModListChanged(object sender, EventArgs e)
        {
            Debug.WriteLine("[DEBUG] GitModsViewModel: Received ListChanged event from ModListManager. Reloading Git mods.");
            LoadGitMods();
        }

        private void LoadGitMods()
        {
            if (_isDisposed) return;

            Debug.WriteLine("[DEBUG] GitModsViewModel: LoadGitMods() called.");
            var allMods = _modListManager.GetAllMods();
            Debug.WriteLine($"[DEBUG] GitModsViewModel: _modListManager.GetAllMods() returned {allMods?.Count() ?? 0} total mods.");

            if (allMods == null || !allMods.Any())
            {
                Debug.WriteLine("[DEBUG] GitModsViewModel: allMods is NULL or empty. Setting GitMods to empty list.");
                GitMods = new List<GitModItemWrapper>();
                return;
            }

            var filteredMods = allMods
                .Where(m => m != null && m.ModType == ModType.Git)
                .Select(m => new GitModItemWrapper(m))
                .ToList();
            Debug.WriteLine($"[DEBUG] GitModsViewModel: Filtered down to {filteredMods.Count} Git mods.");
            GitMods = filteredMods;
        }

        private void CheckUpdates(object parameter)
        {
            StatusMessage = "Checking for updates...";

            foreach (var modWrapper in GitMods)
            {
                var mod = modWrapper.ModItem;
                try
                {
                    using var repo = new Repository(mod.Path);
                    var remote = repo.Network.Remotes["origin"];
                    var fetchOptions = new FetchOptions();
                    Commands.Fetch(repo, remote.Name, remote.FetchRefSpecs.Select(x => x.Specification), fetchOptions, null);

                    var branch = repo.Head;
                    var tracking = branch.TrackedBranch;

                    if (tracking == null)
                    {
                        modWrapper.UpdateStatus = "No tracking branch";
                        modWrapper.IsSelected = false;
                        Debug.WriteLine($"[WARN] Mod '{mod.Name}' has no tracking branch.");
                        continue;
                    }

                    var divergence = repo.ObjectDatabase.CalculateHistoryDivergence(branch.Tip, tracking.Tip);

                    if (divergence.BehindBy > 0)
                    {
                        modWrapper.UpdateStatus = $"{divergence.BehindBy} new updates";
                        modWrapper.IsSelected = true;
                        Debug.WriteLine($"[INFO] Mod '{mod.Name}' has {divergence.BehindBy} new updates.");
                    }
                    else
                    {
                        modWrapper.UpdateStatus = "Up to date";
                        modWrapper.IsSelected = false;
                        Debug.WriteLine($"[INFO] Mod '{mod.Name}' is up to date.");
                    }
                }
                catch (Exception ex)
                {
                    modWrapper.UpdateStatus = "Error checking updates";
                    modWrapper.IsSelected = false;
                    Debug.WriteLine($"[ERROR] CheckUpdates failed for mod '{mod.Name}': {ex.Message}");
                }
            }

            StatusMessage = "Update check complete.";
        }

        private bool CanPullUpdates(object parameter)
        {
            return GitMods != null && GitMods.Any(m => m.IsSelected);
        }

        private void PullUpdates(object parameter)
        {
            StatusMessage = "Pulling updates...";

            var selectedMods = GitMods.Where(m => m.IsSelected).ToList();
            foreach (var modWrapper in selectedMods)
            {
                var mod = modWrapper.ModItem;
                try
                {
                    using var repo = new Repository(mod.Path);
                    var signature = new Signature("ModManager", "modmanager@localhost", DateTimeOffset.Now);
                    var pullOptions = new PullOptions
                    {
                        FetchOptions = new FetchOptions(),
                        MergeOptions = new MergeOptions
                        {
                            FastForwardStrategy = FastForwardStrategy.FastForwardOnly
                        }
                    };
                    Commands.Pull(repo, signature, pullOptions);
                    Debug.WriteLine($"[INFO] Pulled updates for mod '{mod.Name}'.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ERROR] PullUpdates failed for mod '{mod.Name}': {ex.Message}");
                }
            }

            StatusMessage = "Updates pulled successfully.";
        }

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