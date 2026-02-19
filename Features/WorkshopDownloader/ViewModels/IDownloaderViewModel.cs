using System;
using System.ComponentModel;

namespace RimSharp.Features.WorkshopDownloader.ViewModels
{
    public interface IDownloaderViewModel : INotifyPropertyChanged
    {
        bool IsOperationInProgress { get; }
        event EventHandler DownloadCompletedAndRefreshNeeded;
    }
}
