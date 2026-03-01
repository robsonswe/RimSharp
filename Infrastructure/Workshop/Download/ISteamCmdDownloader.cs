using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Infrastructure.Workshop;
using RimSharp.Infrastructure.Workshop.Download.Models;

public interface ISteamCmdDownloader
{
    Task<SteamCmdDownloadResult> DownloadModsAsync(
        IEnumerable<DownloadItem> itemsToDownload,
        bool validate,
        IProgress<(int current, int total, string message)>? progress = null,
        CancellationToken cancellationToken = default);
}
