using System;
using System.Windows.Input;
using RimSharp.Core.Commands;
using RimSharp.Features.WorkshopDownloader.Models;

namespace RimSharp.Features.WorkshopDownloader.Components.DownloadQueue
{
    public class QueueCommands
    {
        public ICommand AddModCommand { get; }
        public ICommand RemoveItemCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand SetupSteamCmdCommand { get; }
        public ICommand CheckUpdatesCommand { get; }

        public QueueCommands(
            Action addMod,
            Func<bool> canAddMod,
            Action<DownloadItem> removeItem,
            Action download,
            Func<bool> canDownload,
            Action setupSteamCmd,
            Action checkUpdates)
        {
            AddModCommand = new RelayCommand(
                _ => addMod(),
                _ => canAddMod());
                
            RemoveItemCommand = new RelayCommand(
                param => removeItem(param as DownloadItem),
                param => param is DownloadItem);
                
            DownloadCommand = new RelayCommand(
                _ => download(),
                _ => canDownload());
                
            SetupSteamCmdCommand = new RelayCommand(
                _ => setupSteamCmd());
                
            CheckUpdatesCommand = new RelayCommand(
                _ => checkUpdates());
        }
    }
}