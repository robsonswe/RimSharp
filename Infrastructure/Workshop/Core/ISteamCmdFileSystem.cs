#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Infrastructure.Workshop.Core
{
    public interface ISteamCmdFileSystem
    {
        Task<bool> ClearDepotCacheAsync();
    }
}