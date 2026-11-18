#nullable enable
using RimSharp.Infrastructure.Mods.ModInfo;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModInfoSourceManager : IExternalSourceManager
    {
        ModInfoSourceRegistry Registry { get; }
    }
}
