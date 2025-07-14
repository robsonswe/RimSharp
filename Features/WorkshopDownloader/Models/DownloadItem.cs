#nullable enable
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Models;
using System.Collections.Generic;

namespace RimSharp.Features.WorkshopDownloader.Models
{
    public class DownloadItem : ViewModelBase
    {
        private string? _name;
        private string? _url;
        private string? _steamId;
        private string? _publishDate; // Date from Workshop
        private string? _standardDate; // Parsed Workshop Date
        private long _fileSize;
        private List<string>? _latestVersions;
        private List<VersionSupport>? _installedVersions;

        private bool _isInstalled;
        private string? _localDateStamp; // Date from local timestamp file
        private bool _isActive;
        private bool _isLocallyOutdatedRW; // Based on ModItem.IsOutdatedRW
        private bool _isFavorite;

        public string? Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string? Url
        {
            get => _url;
            set => SetProperty(ref _url, value);
        }

        public string? SteamId
        {
            get => _steamId;
            set => SetProperty(ref _steamId, value);
        }

        public string? PublishDate // Workshop Publish Date
        {
            get => _publishDate;
            set => SetProperty(ref _publishDate, value);
        }

        public string? StandardDate // Workshop Publish Date (Standard Format)
        {
            get => _standardDate;
            set => SetProperty(ref _standardDate, value);
        }
      public long FileSize // Added: Size in bytes
        {
            get => _fileSize;
            set => SetProperty(ref _fileSize, value);
        }

        public List<string>? LatestVersions
        {
            get => _latestVersions;
            set => SetProperty(ref _latestVersions, value);
        }

        public List<VersionSupport>? InstalledVersions
        {
            get => _installedVersions;
            set => SetProperty(ref _installedVersions, value);
        }
        public bool IsInstalled
        {
            get => _isInstalled;
            set => SetProperty(ref _isInstalled, value);
        }

        public string? LocalDateStamp // Local Timestamp Date
        {
            get => _localDateStamp;
            set => SetProperty(ref _localDateStamp, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        public bool IsLocallyOutdatedRW
        {
            get => _isLocallyOutdatedRW;
            set => SetProperty(ref _isLocallyOutdatedRW, value);
        }

        public bool IsFavorite
        {
            get => _isFavorite;
            set => SetProperty(ref _isFavorite, value);
        }

        // --- Helper to reset local info ---
        public void ClearLocalInfo()
        {
            IsInstalled = false;
            LocalDateStamp = null;
            IsActive = false;
            IsLocallyOutdatedRW = false;
            IsFavorite = false;
            InstalledVersions = null;
        }
    }
}