#nullable enable
using System;
using RimSharp.AppDir.AppFiles;

namespace RimSharp.Features.WorkshopDownloader.Components.StatusBar
{
    public class StatusBarViewModel : ViewModelBase
    {
        private string _statusMessage = string.Empty;
        private bool _isProgressBarVisible = false;
        private bool _isIndeterminate = true;
        private int _progress = 0;

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public bool IsProgressBarVisible
        {
            get => _isProgressBarVisible;
            set => SetProperty(ref _isProgressBarVisible, value);
        }

        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set => SetProperty(ref _isIndeterminate, value);
        }

        public int Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, Math.Clamp(value, 0, 100));
        }

        public StatusBarViewModel()
        {
            // Initialize with default empty status
        }

        public void SetStatus(string message)
        {
            RunOnUIThread(() => StatusMessage = message);
        }

        public void ShowProgressBar(bool isIndeterminate = true)
        {
            RunOnUIThread(() => 
            {
                IsIndeterminate = isIndeterminate;
                IsProgressBarVisible = true;
                if (isIndeterminate)
                {
                    Progress = 0;
                }
            });
        }

        public void UpdateProgress(int progressPercentage, string? statusMessage = null)
        {
            RunOnUIThread(() => 
            {
                IsIndeterminate = false;
                Progress = progressPercentage;
                if (statusMessage != null)
                {
                    StatusMessage = statusMessage;
                }
            });
        }

        public void HideProgressBar()
        {
            RunOnUIThread(() => 
            {
                IsProgressBarVisible = false;
                Progress = 0;
            });
        }

        public void Reset()
        {
            RunOnUIThread(() => 
            {
                StatusMessage = string.Empty;
                IsProgressBarVisible = false;
                IsIndeterminate = true;
                Progress = 0;
            });
        }
    }
}