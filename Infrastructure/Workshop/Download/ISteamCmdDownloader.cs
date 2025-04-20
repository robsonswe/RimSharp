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
        // bool showWindow = false, // Parameter less relevant now, always true behavior
        CancellationToken cancellationToken = default);
}
