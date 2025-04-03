using RimSharp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RimSharp.ViewModels.Modules.Mods.IO
{
    public interface IModListIOService
    {
        Task ImportModListAsync();
        Task ExportModListAsync(IEnumerable<ModItem> activeMods);
    }
}
