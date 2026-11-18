#nullable enable
using RimSharp.Infrastructure.Mods.Replacements;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModReplacementSourceManager : IExternalSourceManager
    {
        ModReplacementSourceRegistry Registry { get; }
    }
}
