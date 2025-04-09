using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface ISteamCmdDownloader
{
    Task<bool> DownloadModsAsync(IEnumerable<string> workshopIds, bool validate, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
