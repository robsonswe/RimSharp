using System.Threading.Tasks;

namespace RimSharp.ViewModels.Modules.Mods.Commands
{
    public interface IModCommandService
    {
        Task HandleDropCommand(DropModArgs args);
        Task ClearActiveModsAsync();
        Task SortActiveModsAsync();
    }
}
