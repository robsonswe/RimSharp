#nullable enable
using RimSharp.Infrastructure.Mods.Versions;

namespace RimSharp.Shared.Services.Contracts
{
    public interface INoVersionWarningSourceManager : IExternalSourceManager
    {
        NoVersionWarningSourceRegistry Registry { get; }
    }
}
