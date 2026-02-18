#nullable enable
using System;
using System.Reflection;
using System.Threading.Tasks;
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

        private string _updateStatusIcon = "🔄";
        public string UpdateStatusIcon { get => _updateStatusIcon; set => SetProperty(ref _updateStatusIcon, value); }

        private bool _isNewVersionAvailable;
        public bool IsNewVersionAvailable { get => _isNewVersionAvailable; set => SetProperty(ref _isNewVersionAvailable, value); }

        private string? _releaseUrl;
        public string? ReleaseUrl { get => _releaseUrl; set => SetProperty(ref _releaseUrl, value); }

        public AboutDialogViewModel(IAppUpdaterService appUpdaterService) : base("About RimSharp")
        {
            _appUpdaterService = appUpdaterService;
            _ = CheckForUpdatesAsync();
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                var (isAvailable, latestVersion, url) = await _appUpdaterService.CheckForAppUpdateAsync();
                
                if (isAvailable)
                {
                    UpdateStatus = $"New version available: {latestVersion}";
                    UpdateStatusIcon = "⚠️";
                    IsNewVersionAvailable = true;
                    ReleaseUrl = url;
                }
                else if (latestVersion != null)
                {
                    UpdateStatus = "RimSharp is up to date";
                    UpdateStatusIcon = "✅";
                    IsNewVersionAvailable = false;
                }
                else
                {
                    UpdateStatus = "Could not check for updates";
                    UpdateStatusIcon = "❌";
                    IsNewVersionAvailable = false;
                }
            }
            catch
            {
                UpdateStatus = "Error checking for updates";
                UpdateStatusIcon = "❌";
                IsNewVersionAvailable = false;
            }
        }

        public void Close()
        {
            CloseDialog(true);
        }
    }
}
