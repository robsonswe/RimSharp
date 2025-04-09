namespace RimSharp.Features.WorkshopDownloader.Models
{
    public enum SteamCmdProgressType
    {
        GeneralInfo, // Generic status message
        StartingDownload, // Starting download of a specific mod
        DownloadSuccess, // Successfully downloaded a specific mod
        DownloadFailure, // Failed to download a specific mod
        OverallProgress, // Update overall percentage
        Warning,
        Error
    }

    public class SteamCmdProgressUpdate
    {
        public SteamCmdProgressType Type { get; set; }
        public string Message { get; set; }
        public string SteamId { get; set; } // ID of the mod being processed
        public string ModName { get; set; } // Name of the mod being processed
        public int CurrentItem { get; set; } // e.g., 3rd mod out of 10
        public int TotalItems { get; set; } // e.g., 10 mods total

        public SteamCmdProgressUpdate(SteamCmdProgressType type, string message, int current = 0, int total = 0, string steamId = null, string modName = null)
        {
            Type = type;
            Message = message;
            CurrentItem = current;
            TotalItems = total;
            SteamId = steamId;
            ModName = modName;
        }
    }
}