#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModDataService
    {
        Task<List<ModItem>> LoadAllModsAsync(System.IProgress<(int current, int total, string message)>? progress = null);
        List<string> LoadActiveModIdsFromConfig();
        void SaveActiveModIdsToConfig(IEnumerable<string> activeModIds);
    }
}