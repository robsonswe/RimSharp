using RimSharp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RimSharp.Services
{
    public class ModService : IModService
    {
        private readonly IPathService _pathService;
        private List<ModItem> _loadedMods;

        public ModService(IPathService pathService)
        {
            _pathService = pathService;
            _loadedMods = new List<ModItem>();
        }

        public IEnumerable<ModItem> GetLoadedMods()
        {
            return _loadedMods;
        }

        public void LoadMods()
        {
            // Prototype implementation - just create a dummy core mod
            _loadedMods.Add(new ModItem
            {
                Name = "Core",
                PackageId = "ludeon.rimworld",
                Path = System.IO.Path.Combine(_pathService.GetModsPath(), "Core"),
                IsCore = true,
                IsDirty = false,
                Authors = "Ludeon Studios",
                Description = "Core game content"
            });
        }

        public async Task LoadModsAsync()
        {
            await Task.Run(() =>
            {
                // Prototype implementation - just create a dummy core mod
                _loadedMods.Add(new ModItem
                {
                    Name = "Core",
                    PackageId = "ludeon.rimworld",
                    Path = System.IO.Path.Combine(_pathService.GetModsPath(), "Core"),
                    IsCore = true,
                    IsDirty = false,
                    Authors = "Ludeon Studios",
                    Description = "Core game content"
                });
            });
        }
    }
}