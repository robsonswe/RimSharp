#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Infrastructure.Workshop.Core
{
    public interface ISteamCmdInstaller
    {
        Task<bool> CheckSetupAsync();
        Task<bool> SetupAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    }
}