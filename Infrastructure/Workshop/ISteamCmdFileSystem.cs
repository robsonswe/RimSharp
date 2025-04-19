#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

public interface ISteamCmdFileSystem
{
    Task<bool> ClearDepotCacheAsync();
}
