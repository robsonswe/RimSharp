#nullable enable
using System.Threading.Tasks;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IAppUpdaterService
    {
        Task<(bool IsUpdateAvailable, string? LatestVersion, string? ReleaseUrl)> CheckForAppUpdateAsync();
    }
}
