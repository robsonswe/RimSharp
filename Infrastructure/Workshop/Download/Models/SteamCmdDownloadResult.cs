using System.Collections.Generic;
using RimSharp.Features.WorkshopDownloader.Models;

namespace RimSharp.Infrastructure.Workshop.Download.Models
{
    public class SteamCmdDownloadResult
    {
        public List<DownloadItem> SucceededItems { get; } = new List<DownloadItem>();
        public List<DownloadItem> FailedItems { get; } = new List<DownloadItem>();
        public bool OverallSuccess { get; set; } // Based on ExitCode or specific logic
        public List<string> LogMessages { get; } = new List<string>(); // Store log for debugging if needed
        public int ExitCode { get; set; } = -1; // Store the process exit code
    }
}
