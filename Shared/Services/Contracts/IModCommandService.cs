using System.Threading.Tasks;
using RimSharp.Features.ModManager.Services.Commands;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModCommandService
    {
        Task HandleDropCommand(DropModArgs args);
        Task ClearActiveModsAsync();
        Task SortActiveModsAsync();
    }
}
