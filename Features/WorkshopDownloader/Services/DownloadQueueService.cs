using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Core.Extensions;
using System.Diagnostics;

namespace RimSharp.Features.WorkshopDownloader.Services
{
    public interface IDownloadQueueService
    {
        ObservableCollection<DownloadItem> Items { get; }
        bool AddToQueue(ModInfoDto modInfo);
        bool RemoveFromQueue(DownloadItem item);
        bool IsInQueue(string steamId);

        event EventHandler<string> StatusChanged;
    }

    public class DownloadQueueService : IDownloadQueueService
    {
        private readonly ObservableCollection<DownloadItem> _items;
        private readonly System.Collections.Generic.HashSet<string> _enqueuedSteamIds = new();
        private readonly object _collectionLock = new();

        public ObservableCollection<DownloadItem> Items => _items;

        public event EventHandler<string>? StatusChanged;

        public DownloadQueueService()
        {
            _items = new ObservableCollection<DownloadItem>();
        }

        public bool AddToQueue(ModInfoDto modInfo)
        {
            if (modInfo == null || string.IsNullOrEmpty(modInfo.SteamId))
                return false;

            lock (_collectionLock)
            {
                if (_enqueuedSteamIds.Contains(modInfo.SteamId))
                {
                    NotifyStatusChanged($"Mod '{modInfo.Name}' is already in the download queue");
                    return false;
                }
                
                _enqueuedSteamIds.Add(modInfo.SteamId);
            }

            var downloadItem = new DownloadItem
            {
                Name = modInfo.Name,
                Url = modInfo.Url,
                SteamId = modInfo.SteamId,
                PublishDate = modInfo.PublishDate,
                StandardDate = modInfo.StandardDate,
                FileSize = modInfo.FileSize,
                LatestVersions = modInfo.LatestVersions
            };

            ThreadHelper.EnsureUiThread(() =>
            {
                _items.Add(downloadItem);
                NotifyStatusChanged($"Added mod {modInfo.Name} to download queue");
            });

            return true;
        }

        public bool RemoveFromQueue(DownloadItem item)
        {
            if (item == null)
                return false;

            lock (_collectionLock)
            {
                if (!string.IsNullOrEmpty(item.SteamId))
                {
                    _enqueuedSteamIds.Remove(item.SteamId);
                }
            }

            if (ThreadHelper.IsUiThread)
            {
                bool removed = _items.Remove(item);
                if (removed)
                {
                    NotifyStatusChanged($"Removed {item.Name} from download queue");
                }
                return removed;
            }
            else
            {
                ThreadHelper.BeginInvokeOnUiThread(() =>
                {
                    _items.Remove(item);
                    NotifyStatusChanged($"Removed {item.Name} from download queue");
                });
                return true; 
            }
        }

        public bool IsInQueue(string steamId)
        {
            if (string.IsNullOrEmpty(steamId)) return false;

            lock (_collectionLock)
            {
                return _enqueuedSteamIds.Contains(steamId);
            }
        }

        private void NotifyStatusChanged(string message)
        {
            ThreadHelper.EnsureUiThread(() => StatusChanged?.Invoke(this, message));
        }
    }
}


