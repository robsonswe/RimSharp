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
        private readonly object _collectionLock = new object();

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

            if (IsInQueue(modInfo.SteamId))
            {
                NotifyStatusChanged($"Mod '{modInfo.Name}' is already in the download queue");
                return false;
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

            bool removed = false;
            if (ThreadHelper.IsUiThread)
            {
                removed = _items.Remove(item);
                if (removed)
                {
                    NotifyStatusChanged($"Removed {item.Name} from download queue");
                }
                else
                {
                    Debug.WriteLine($"[DownloadQueueService] Failed to remove item {item.SteamId} - not found in collection.");
                }
                return removed;
            }
            else
            {

                ThreadHelper.BeginInvokeOnUiThread(() =>
                {
                    if (_items.Remove(item))
                    {
                        NotifyStatusChanged($"Removed {item.Name} from download queue");
                    }
                });
                return true; 
            }
        }

        public bool IsInQueue(string steamId)
        {
            if (string.IsNullOrEmpty(steamId)) return false;

            bool exists = false;
            ThreadHelper.EnsureUiThread(() =>
            {
                exists = _items.Any(item => item.SteamId == steamId);
            });
            return exists;
        }

        private void NotifyStatusChanged(string message)
        {
            ThreadHelper.EnsureUiThread(() => StatusChanged?.Invoke(this, message));
        }
    }
}


