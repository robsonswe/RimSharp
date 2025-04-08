using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
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
    private readonly ObservableCollection<DownloadItem> _items;
    private readonly Dispatcher _dispatcher;

    public ObservableCollection<DownloadItem> Items => _items;
    
    public event EventHandler<string> StatusChanged;
    
    public DownloadQueueService()
    {
        _items = new ObservableCollection<DownloadItem>();
        _dispatcher = Dispatcher.CurrentDispatcher;
    }
    
    public bool AddToQueue(ModInfoDto modInfo)
    {
        if (modInfo == null || string.IsNullOrEmpty(modInfo.SteamId))
            return false;
            
        if (IsInQueue(modInfo.SteamId))
        {
            NotifyStatusChanged("This mod is already in the download queue");
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
        
        if (_dispatcher.CheckAccess())
        {
            _items.Add(downloadItem);
            NotifyStatusChanged($"Added mod {modInfo.Name} to download queue");
        }
        else
        {
            _dispatcher.Invoke(() => 
            {
                _items.Add(downloadItem);
                NotifyStatusChanged($"Added mod {modInfo.Name} to download queue");
            });
        }
        
        return true;
    }
    
    public void RemoveFromQueue(DownloadItem item)
    {
        if (item == null || !_items.Contains(item))
            return;

        if (_dispatcher.CheckAccess())
        {
            _items.Remove(item);
            NotifyStatusChanged($"Removed {item.Name} from download queue");
        }
        else
        {
            _dispatcher.Invoke(() => 
            {
                _items.Remove(item);
                NotifyStatusChanged($"Removed {item.Name} from download queue");
            });
        }
    }
    
    public bool IsInQueue(string steamId)
    {
        return !string.IsNullOrEmpty(steamId) && 
               _items.Any(item => item.SteamId == steamId);
    }

    private void NotifyStatusChanged(string message)
    {
        if (_dispatcher.CheckAccess())
        {
            StatusChanged?.Invoke(this, message);
        }
        else
        {
            _dispatcher.Invoke(() => StatusChanged?.Invoke(this, message));
        }
    }
}

}
