using System.Collections.Generic;
using System.Threading.Tasks;
using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModListIOService
    {
        Task ImportModListAsync();
        Task ExportModListAsync(IEnumerable<ModItem> activeMods);
    }
}
