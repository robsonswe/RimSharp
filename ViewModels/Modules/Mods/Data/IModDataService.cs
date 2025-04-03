using System.Collections.Generic;
using System.Threading.Tasks;
using RimSharp.Models;

namespace RimSharp.ViewModels.Modules.Mods.Data
{
    public interface IModDataService
    {
        Task<List<ModItem>> LoadAllModsAsync();
        List<string> LoadActiveModIdsFromConfig();
        void SaveActiveModIdsToConfig(IEnumerable<string> activeModIds);
    }
}