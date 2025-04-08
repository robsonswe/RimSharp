using System;
using System.Collections.ObjectModel;
using System.Linq;
using RimSharp.Features.WorkshopDownloader.Models;

namespace RimSharp.Features.WorkshopDownloader.Services
{
    public interface IDownloadQueueService
    {
        ObservableCollection<DownloadItem> Items { get; }
        bool AddToQueue(ModInfoDto modInfo);
        void RemoveFromQueue(DownloadItem item);
        bool IsInQueue(string steamId);
        
        event EventHandler<string> StatusChanged;
    }
    
    public class DownloadQueueService : IDownloadQueueService
    {
        public ObservableCollection<DownloadItem> Items { get; }
        
        public event EventHandler<string> StatusChanged;
        
        public DownloadQueueService()
        {
            Items = new ObservableCollection<DownloadItem>();
        }
        
        public bool AddToQueue(ModInfoDto modInfo)
        {
            if (modInfo == null || string.IsNullOrEmpty(modInfo.SteamId))
                return false;
                
            if (IsInQueue(modInfo.SteamId))
            {
                StatusChanged?.Invoke(this, "This mod is already in the download queue");
                return false;
            }
            
            var downloadItem = new DownloadItem
            {
                Name = modInfo.Name,
                Url = modInfo.Url,
                SteamId = modInfo.SteamId,
                PublishDate = modInfo.PublishDate,
                StandardDate = modInfo.StandardDate
            };
            
            Items.Add(downloadItem);
            StatusChanged?.Invoke(this, $"Added mod {modInfo.Name} to download queue");
            return true;
        }
        
        public void RemoveFromQueue(DownloadItem item)
        {
            if (item != null && Items.Contains(item))
            {
                Items.Remove(item);
                StatusChanged?.Invoke(this, $"Removed {item.Name} from download queue");
            }
        }
        
        public bool IsInQueue(string steamId)
        {
            return !string.IsNullOrEmpty(steamId) && 
                   Items.Any(item => item.SteamId == steamId);
        }
    }
}
