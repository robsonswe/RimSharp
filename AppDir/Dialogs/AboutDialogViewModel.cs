#nullable enable
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.AppDir.Dialogs
{
    public class AboutDialogViewModel : DialogViewModelBase<bool>
    {
        private readonly IAppUpdaterService _appUpdaterService;

        public string AppName => "RimSharp";
        public string AppVersion => $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"}";
        public string Description => "A modern RimWorld mod manager designed for speed and reliability.";
        public string Developer => "robsonswe";
        public string License => "MIT License";
        public string GitHubUrl => "https://github.com/robsonswe/RimSharp";

        private string _updateStatus = "Checking for updates...";
        public string UpdateStatus { get => _updateStatus; set => SetProperty(ref _updateStatus, value); }

        private string _updateStatusIcon = "fa-arrows-rotate";
        public string UpdateStatusIcon { get => _updateStatusIcon; set => SetProperty(ref _updateStatusIcon, value); }

        private string _statusColor = "RimworldBrownBrush";
        public string StatusColor { get => _statusColor; set => SetProperty(ref _statusColor, value); }

        private bool _isNewVersionAvailable;
        public bool IsNewVersionAvailable { get => _isNewVersionAvailable; set => SetProperty(ref _isNewVersionAvailable, value); }

        private string? _releaseUrl;
        public string? ReleaseUrl { get => _releaseUrl; set => SetProperty(ref _releaseUrl, value); }

        public ICommand UpdateCommand { get; }
        public ICommand OpenUrlCommand { get; }

        public AboutDialogViewModel(IAppUpdaterService appUpdaterService) : base("About RimSharp")
        {
            _appUpdaterService = appUpdaterService;
            UpdateCommand = CreateCommand(ExecuteUpdate, () => IsNewVersionAvailable, nameof(IsNewVersionAvailable));
            OpenUrlCommand = CreateCommand<string>(ExecuteOpenUrl);
            _ = CheckForUpdatesAsync();
        }

        private void ExecuteUpdate()
        {
            if (!string.IsNullOrEmpty(ReleaseUrl))
            {
                ExecuteOpenUrl(ReleaseUrl);
            }
        }

        private void ExecuteOpenUrl(string? url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening URL '{url}': {ex.Message}");
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                var (isAvailable, latestVersion, url) = await _appUpdaterService.CheckForAppUpdateAsync();
                
                if (isAvailable)
                {
                    UpdateStatus = $"New version available: {latestVersion}";
                    UpdateStatusIcon = "fa-triangle-exclamation";
                    StatusColor = "RimworldRedBrush";
                    IsNewVersionAvailable = true;
                    ReleaseUrl = url;
                }
                else if (latestVersion != null)
                {
                    UpdateStatus = "RimSharp is up to date";
                    UpdateStatusIcon = "fa-circle-check";
                    StatusColor = "RimworldDarkGreenBrush";
                    IsNewVersionAvailable = false;
                }
                else
                {
                    UpdateStatus = "Could not check for updates";
                    UpdateStatusIcon = "fa-circle-xmark";
                    StatusColor = "RimworldErrorRedBrush";
                    IsNewVersionAvailable = false;
                }
            }
            catch
            {
                UpdateStatus = "Error checking for updates";
                UpdateStatusIcon = "fa-circle-xmark";
                StatusColor = "RimworldErrorRedBrush";
                IsNewVersionAvailable = false;
            }
        }

        public void Close()
        {
            CloseDialog(true);
        }
    }
}
