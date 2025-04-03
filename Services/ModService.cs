using RimSharp.Models;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RimSharp.Services
{

    public class ModService : IModService
    {
        private readonly IPathService _pathService;
        private readonly List<ModItem> _allMods = new();
        private string _currentMajorVersion = string.Empty;

        public ModService(IPathService pathService)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        }

        public IEnumerable<ModItem> GetLoadedMods() => _allMods;

        public void LoadMods()
        {
            _allMods.Clear();
            _currentMajorVersion = _pathService.GetMajorGameVersion();

            // Check if all required paths are set
            var gamePath = _pathService.GetGamePath();
            var configPath = _pathService.GetConfigPath();
            var modsPath = _pathService.GetModsPath();

            if (string.IsNullOrEmpty(gamePath) || 
                string.IsNullOrEmpty(configPath) || 
                string.IsNullOrEmpty(modsPath))
            {
                return; // Return empty list if any path is not set
            }

            // Load core mods from game data folder
            LoadCoreMods(gamePath);

            // Load workshop mods from mods folder
            LoadWorkshopMods(modsPath);

            // Set active status based on ModsConfig.xml
            SetActiveMods(configPath);
        }

        public async Task LoadModsAsync()
        {
            var gamePath = _pathService.GetGamePath();
            var configPath = _pathService.GetConfigPath();
            var modsPath = _pathService.GetModsPath();
            _currentMajorVersion = _pathService.GetMajorGameVersion();

            if (string.IsNullOrEmpty(gamePath) || 
                string.IsNullOrEmpty(configPath) || 
                string.IsNullOrEmpty(modsPath))
            {
                _allMods.Clear();
                return;
            }

            var mods = new ConcurrentBag<ModItem>();

            // Process core and workshop mods in parallel
            await Task.WhenAll(
                Task.Run(() => LoadCoreModsAsync(gamePath, mods)),
                Task.Run(() => LoadWorkshopModsAsync(modsPath, mods))
            );

            // Update the main collection
            _allMods.Clear();
            _allMods.AddRange(mods);

            // Set active status based on ModsConfig.xml
            await Task.Run(() => SetActiveMods(configPath));
        }

        private void LoadCoreMods(string gamePath)
        {
            var coreModsPath = Path.Combine(gamePath, "Data");
            if (!Directory.Exists(coreModsPath)) return;

            foreach (var dir in Directory.GetDirectories(coreModsPath))
            {
                var aboutPath = Path.Combine(dir, "About", "About.xml");
                if (!File.Exists(aboutPath)) continue;

                var folderName = Path.GetFileName(dir);
                var mod = ParseAboutXml(aboutPath, folderName);
                if (mod == null) continue;

                mod.Path = dir;

                // Set core/expansion flags
                bool isCoreMod = mod.PackageId == "Ludeon.RimWorld";
                mod.IsCore = isCoreMod;
                mod.IsExpansion = !isCoreMod;  // All other official mods in Data folder are expansions

                // Append "[DLC]" to name if it's an expansion
                if (mod.IsExpansion && !string.IsNullOrEmpty(mod.Name))
                {
                    mod.Name = $"{mod.Name} [DLC]";
                }

                _allMods.Add(mod);
            }
        }

        private async Task LoadCoreModsAsync(string gamePath, ConcurrentBag<ModItem> mods)
        {
            var coreModsPath = Path.Combine(gamePath, "Data");
            if (!Directory.Exists(coreModsPath)) return;

            // Get all directories first to avoid file system bottlenecks
            var directories = Directory.GetDirectories(coreModsPath);
            
            await Task.WhenAll(directories.Select(async dir => 
            {
                var aboutPath = Path.Combine(dir, "About", "About.xml");
                if (!File.Exists(aboutPath)) return;

                var folderName = Path.GetFileName(dir);
                
                // Parse on a separate thread to avoid XML parsing bottlenecks
                var mod = await Task.Run(() => ParseAboutXml(aboutPath, folderName));
                if (mod == null) return;

                mod.Path = dir;

                // Set core/expansion flags
                bool isCoreMod = mod.PackageId == "Ludeon.RimWorld";
                mod.IsCore = isCoreMod;
                mod.IsExpansion = !isCoreMod;

                if (mod.IsExpansion && !string.IsNullOrEmpty(mod.Name))
                {
                    mod.Name = $"{mod.Name} [DLC]";
                }

                mods.Add(mod);
            }));
        }

        private void LoadWorkshopMods(string modsPath)
        {
            if (!Directory.Exists(modsPath)) return;

            foreach (var dir in Directory.GetDirectories(modsPath))
            {
                var aboutPath = Path.Combine(dir, "About", "About.xml");
                if (!File.Exists(aboutPath)) continue;

                var folderName = Path.GetFileName(dir);
                var mod = ParseAboutXml(aboutPath, folderName);
                if (mod == null) continue;

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

        private async Task LoadWorkshopModsAsync(string modsPath, ConcurrentBag<ModItem> mods)
        {
            if (!Directory.Exists(modsPath)) return;

            // Get all directories first to avoid file system bottlenecks
            var directories = Directory.GetDirectories(modsPath);
            
            await Task.WhenAll(directories.Select(async dir => 
            {
                var aboutPath = Path.Combine(dir, "About", "About.xml");
                if (!File.Exists(aboutPath)) return;

                var folderName = Path.GetFileName(dir);
                
                // Parse on a separate thread to avoid XML parsing bottlenecks
                var mod = await Task.Run(() => ParseAboutXml(aboutPath, folderName));
                if (mod == null) return;

                mod.Path = dir;

                // Check if folder name is a Steam ID
                if (long.TryParse(folderName, out _))
                {
                    mod.SteamId = folderName;
                    mod.SteamUrl = $"https://steamcommunity.com/workshop/filedetails/?id={folderName}";
                }

                mods.Add(mod);
            }));
        }

        private ModItem ParseAboutXml(string aboutPath, string folderName = null)
        {
            try
            {
                var doc = XDocument.Load(aboutPath);
                var aboutFolder = Path.GetDirectoryName(aboutPath);
                var previewImagePath = Path.Combine(aboutFolder, "Preview.png");

                var root = doc.Element("ModMetaData");
                if (root == null) return null;

                var name = root.Element("name")?.Value;
                if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(folderName))
                {
                    name = folderName;
                }

                var supportedVersions = root.Element("supportedVersions")?.Elements("li")
                    .Select(x => x.Value)
                    .ToList() ?? new List<string>();

                var mod = new ModItem
                {
                    Name = name,
                    PackageId = root.Element("packageId")?.Value,
                    Authors = root.Element("author")?.Value ??
                             string.Join(", ", root.Element("authors")?.Elements("li").Select(x => x.Value) ?? Array.Empty<string>()),
                    Description = root.Element("description")?.Value,
                    ModVersion = root.Element("modVersion")?.Value,
                    ModIconPath = root.Element("modIconPath")?.Value,
                    Url = root.Element("url")?.Value,
                    SupportedVersions = supportedVersions,
                    PreviewImagePath = File.Exists(previewImagePath) ? previewImagePath : null,
                    IsOutdatedRW = !IsVersionSupported(_currentMajorVersion, supportedVersions) && supportedVersions.Any(),
                };

                // Parse dependencies
                mod.ModDependencies = root.Element("modDependencies")?.Elements("li")
                    .Select(x => new ModDependency
                    {
                        PackageId = x.Element("packageId")?.Value,
                        DisplayName = x.Element("displayName")?.Value,
                        SteamWorkshopUrl = x.Element("steamWorkshopUrl")?.Value
                    }).ToList() ?? new List<ModDependency>();

                // Parse load order rules using a more efficient approach
                ParseLoadOrderRules(root, mod);

                return mod;
            }
            catch
            {
                return null; // Return null for invalid mods
            }
        }

        private static void ParseLoadOrderRules(XElement root, ModItem mod)
        {
            // Load all load order rules at once to minimize XML traversal
            mod.LoadBefore = root.Element("loadBefore")?.Elements("li").Select(x => x.Value).ToList() ?? new List<string>();
            mod.LoadAfter = root.Element("loadAfter")?.Elements("li").Select(x => x.Value).ToList() ?? new List<string>();
            mod.ForceLoadBefore = root.Element("forceLoadBefore")?.Elements("li").Select(x => x.Value).ToList() ?? new List<string>();
            mod.ForceLoadAfter = root.Element("forceLoadAfter")?.Elements("li").Select(x => x.Value).ToList() ?? new List<string>();
            mod.IncompatibleWith = root.Element("incompatibleWith")?.Elements("li").Select(x => x.Value).ToList() ?? new List<string>();
        }

        private static bool IsVersionSupported(string currentVersion, List<string> supportedVersions)
        {
            if (string.IsNullOrEmpty(currentVersion)) return true;
            if (supportedVersions == null || !supportedVersions.Any()) return true;

            return supportedVersions.Any(v => 
                string.Equals(v.Trim(), currentVersion.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private void SetActiveMods(string configFolderPath)
        {
            var configPath = Path.Combine(configFolderPath, "ModsConfig.xml");
            if (!File.Exists(configPath)) return;

            try
            {
                var doc = XDocument.Load(configPath);
                
                // Create a lookup dictionary for faster mod searches
                var packageIdLookup = _allMods.ToDictionary(
                    m => m.PackageId?.ToLowerInvariant() ?? string.Empty, 
                    m => m,
                    StringComparer.OrdinalIgnoreCase
                );
                
                // Get active mod IDs and set IsActive efficiently
                var activeMods = doc.Root?.Element("activeMods")?.Elements("li")
                    .Select(x => x.Value.ToLowerInvariant())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (activeMods != null)
                {
                    foreach (var id in activeMods)
                    {
                        if (packageIdLookup.TryGetValue(id, out var mod))
                        {
                            mod.IsActive = true;
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors parsing ModsConfig.xml
            }
        }
    }
}