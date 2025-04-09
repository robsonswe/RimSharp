using System;
using System.Threading;
using System.Threading.Tasks;

public interface ISteamCmdFileSystem
{
    Task<bool> CreateWorkshopLinkAsync(string sourceModsPath, string targetLinkPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<bool> ClearDepotCacheAsync();
}
