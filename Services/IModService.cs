// IModService.cs
using RimSharp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RimSharp.Services
{
    public interface IModService
    {
        IEnumerable<ModItem> GetLoadedMods();
        void LoadMods();
        Task LoadModsAsync();
    }
}