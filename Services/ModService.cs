// Update ModService.cs
using RimSharp.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RimSharp.Services
{
    public class ModService : IModService
    {
        private readonly IPathService _pathService;
        private List<ModItem> _allMods = new List<ModItem>();

        public ModService(IPathService pathService)
        {
            _pathService = pathService;
        }

        public IEnumerable<ModItem> GetLoadedMods() => _allMods;

                public void LoadMods()
        {
            _allMods.Clear();

            // Check if all required paths are set
            if (string.IsNullOrEmpty(_pathService.GetGamePath()) || 
                string.IsNullOrEmpty(_pathService.GetConfigPath()) || 
                string.IsNullOrEmpty(_pathService.GetModsPath()))
            {
                return; // Return empty list if any path is not set
            }

            // Load core mods from game data folder
            LoadCoreMods();

            // Load workshop mods from mods folder
            LoadWorkshopMods();

            // Set active status based on ModsConfig.xml
            SetActiveMods();
        }


        public async Task LoadModsAsync()
        {
            await Task.Run(() => LoadMods());
        }

        private void LoadCoreMods()
        {
            var coreModsPath = Path.Combine(_pathService.GetGamePath(), "Data");
            if (!Directory.Exists(coreModsPath)) return;

            foreach (var dir in Directory.GetDirectories(coreModsPath))
            {
                var aboutPath = Path.Combine(dir, "About", "About.xml");
                if (File.Exists(aboutPath))
                {
                    var folderName = Path.GetFileName(dir);
                    var mod = ParseAboutXml(aboutPath, folderName);
                    mod.IsCore = true;
                    mod.Path = dir;
                    _allMods.Add(mod);
                }
            }
        }

        private void LoadWorkshopMods()
        {
            var modsPath = _pathService.GetModsPath();
            if (!Directory.Exists(modsPath)) return;

            foreach (var dir in Directory.GetDirectories(modsPath))
            {
                var aboutPath = Path.Combine(dir, "About", "About.xml");
                if (File.Exists(aboutPath))
                {
                    var folderName = Path.GetFileName(dir);
                    var mod = ParseAboutXml(aboutPath, folderName);
                    mod.Path = dir;

                    // Check if folder name is a Steam ID
                    if (long.TryParse(folderName, out _))
                    {
                        mod.SteamId = folderName;
                        mod.SteamUrl = $"https://steamcommunity.com/workshop/filedetails/?id={folderName}";
                    }

                    _allMods.Add(mod);
                }
            }
        }


        private ModItem ParseAboutXml(string aboutPath, string folderName = null)
        {
            var doc = XDocument.Load(aboutPath);
            var aboutFolder = Path.GetDirectoryName(aboutPath);
            var previewImagePath = Path.Combine(aboutFolder, "Preview.png");

            var root = doc.Element("ModMetaData");
            var name = root.Element("name")?.Value;
            if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(folderName))
            {
                name = folderName;
            }
            var mod = new ModItem
            {
                Name = name,
                PackageId = root.Element("packageId")?.Value,
                Authors = root.Element("author")?.Value ??
                         string.Join(", ", root.Element("authors")?.Elements("li").Select(x => x.Value) ?? Enumerable.Empty<string>()),
                Description = root.Element("description")?.Value,
                ModVersion = root.Element("modVersion")?.Value,
                ModIconPath = root.Element("modIconPath")?.Value,
                Url = root.Element("url")?.Value,
                SupportedVersions = root.Element("supportedVersions")?.Elements("li").Select(x => x.Value).ToList() ?? new List<string>(),
                PreviewImagePath = File.Exists(previewImagePath) ? previewImagePath : null
            };

            // Parse dependencies
            mod.ModDependencies = root.Element("modDependencies")?.Elements("li")
                .Select(x => new ModDependency
                {
                    PackageId = x.Element("packageId")?.Value,
                    DisplayName = x.Element("displayName")?.Value,
                    SteamWorkshopUrl = x.Element("steamWorkshopUrl")?.Value
                }).ToList() ?? new List<ModDependency>();

            // Parse load order rules
            mod.LoadBefore = root.Element("loadBefore")?.Elements("li").Select(x => x.Value).ToList() ?? new List<string>();
            mod.LoadAfter = root.Element("loadAfter")?.Elements("li").Select(x => x.Value).ToList() ?? new List<string>();
            mod.ForceLoadBefore = root.Element("forceLoadBefore")?.Elements("li").Select(x => x.Value).ToList() ?? new List<string>();
            mod.ForceLoadAfter = root.Element("forceLoadAfter")?.Elements("li").Select(x => x.Value).ToList() ?? new List<string>();
            mod.IncompatibleWith = root.Element("incompatibleWith")?.Elements("li").Select(x => x.Value).ToList() ?? new List<string>();

            return mod;
        }

        private void SetActiveMods()
        {
            var configPath = Path.Combine(_pathService.GetConfigPath(), "ModsConfig.xml");
            if (!File.Exists(configPath)) return;

            var doc = XDocument.Load(configPath);
            var activeMods = doc.Root.Element("activeMods")?.Elements("li").Select(x => x.Value).ToList() ?? new List<string>();

            foreach (var mod in _allMods)
            {
                mod.IsActive = activeMods.Contains(mod.PackageId);
            }
        }
    }
}