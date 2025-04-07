using System.Collections.Generic;
using System.Threading.Tasks;
using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModDataService
    {
        Task<List<ModItem>> LoadAllModsAsync();
        List<string> LoadActiveModIdsFromConfig();
        void SaveActiveModIdsToConfig(IEnumerable<string> activeModIds);
    }
}