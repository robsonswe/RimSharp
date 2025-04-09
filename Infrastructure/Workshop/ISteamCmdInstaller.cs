#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

public interface ISteamCmdInstaller
{
    Task<bool> CheckSetupAsync();
    Task<bool> SetupAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
