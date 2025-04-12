using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Models;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RimSharp.Shared.Services.Implementations
{

    public class ModService : IModService
    {
        private readonly IPathService _pathService;
        private readonly List<ModItem> _allMods = new();
        private string _currentMajorVersion = string.Empty;

        private readonly IModRulesService _rulesService;


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

            // Apply rules to all mods
            _rulesService.ApplyRulesToMods(_allMods);

            // Now check for outdated status after rules have been applied
            foreach (var mod in _allMods)
            {
                mod.IsOutdatedRW = !IsVersionSupported(_currentMajorVersion, mod.SupportedVersions) && mod.SupportedVersions.Any();
            }

            // Set active status based on ModsConfig.xml
            SetActiveMods(configPath);
        }


        public ModService(IPathService pathService, IModRulesService rulesService)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _rulesService = rulesService ?? throw new ArgumentNullException(nameof(rulesService));
        }
        #region Timestamp File Creation

        /// <summary>
        /// Creates the DateStamp and timestamp.txt files for a successfully downloaded mod.
        /// </summary>
        public async Task CreateTimestampFilesAsync(string steamId, string publishDate, string standardDate)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !long.TryParse(steamId, out _))
            {
                Debug.WriteLine($"[CreateTimestampFilesAsync] Invalid SteamId provided: '{steamId}'");
                return; // Invalid input
            }

            // Normalize dates to prevent issues, use fallback if null/empty
            string finalPublishDate = string.IsNullOrWhiteSpace(publishDate) ? DateTime.UtcNow.ToString("d MMM yyyy @ h:mmtt", CultureInfo.InvariantCulture) : publishDate.Trim();
            string finalStandardDate = string.IsNullOrWhiteSpace(standardDate) ? DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) : standardDate.Trim();


            try
            {
                var modsPath = _pathService.GetModsPath();
                if (string.IsNullOrEmpty(modsPath) || !Directory.Exists(modsPath))
                {
                    Debug.WriteLine($"[CreateTimestampFilesAsync] Mods path is not set or does not exist: '{modsPath}'");
                    return; // Mods path needed
                }

                var modDir = Path.Combine(modsPath, steamId);
                var aboutDir = Path.Combine(modDir, "About");
                var dateStampPath = Path.Combine(modDir, "DateStamp");
                var timestampPath = Path.Combine(aboutDir, "timestamp.txt");

                // Ensure the target directory exists
                Directory.CreateDirectory(aboutDir); // Creates 'About' if needed (modDir should exist from download)

                // Write the files asynchronously
                await File.WriteAllTextAsync(dateStampPath, finalPublishDate);
                await File.WriteAllTextAsync(timestampPath, finalStandardDate);

                Debug.WriteLine($"[CreateTimestampFilesAsync] Successfully created timestamp files for mod {steamId} in {aboutDir}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"[CreateTimestampFilesAsync] Permission error writing timestamp files for mod {steamId}: {ex.Message}");
                // Potentially inform the user via a different mechanism if critical
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"[CreateTimestampFilesAsync] IO error writing timestamp files for mod {steamId}: {ex.Message}");
            }
            catch (Exception ex) // Catch unexpected errors
            {
                Debug.WriteLine($"[CreateTimestampFilesAsync] Unexpected error writing timestamp files for mod {steamId}: {ex.Message}");
            }
        }

        #endregion

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

            // Apply rules to all mods at once
            _rulesService.ApplyRulesToMods(_allMods);

            // Now check for outdated status after rules have been applied
            foreach (var mod in _allMods)
            {
                mod.IsOutdatedRW = !IsVersionSupported(_currentMajorVersion, mod.SupportedVersions) && mod.SupportedVersions.Any();
            }

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
                mod.ModType = isCoreMod ? ModType.Core : ModType.Expansion;
                // Append "[DLC]" to name if it's an expansion
                if (mod.ModType == ModType.Expansion && !string.IsNullOrEmpty(mod.Name)) // Check ModType
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
                mod.ModType = isCoreMod ? ModType.Core : ModType.Expansion;
                if (mod.ModType == ModType.Expansion && !string.IsNullOrEmpty(mod.Name))
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

                if (long.TryParse(folderName, out _))
                {
                    mod.SteamId = folderName;
                    mod.SteamUrl = $"https://steamcommunity.com/workshop/filedetails/?id={folderName}";
                    mod.ModType = ModType.WorkshopL;
                }
                else
                {
                    var gitDir = Path.Combine(dir, ".git");
                    if (Directory.Exists(gitDir))
                    {
                        mod.ModType = ModType.Git;
                        // Parse Git repository URL
                        var gitConfigPath = Path.Combine(gitDir, "config");
                        if (File.Exists(gitConfigPath))
                        {
                            try
                            {
                                var lines = File.ReadAllLines(gitConfigPath);
                                bool inOriginSection = false;
                                foreach (var line in lines)
                                {
                                    var trimmed = line.Trim();
                                    if (trimmed.StartsWith("[remote \"origin\"]"))
                                    {
                                        inOriginSection = true;
                                    }
                                    else if (trimmed.StartsWith("[") && inOriginSection)
                                    {
                                        inOriginSection = false; // Exit origin section
                                    }
                                    else if (inOriginSection && trimmed.StartsWith("url = "))
                                    {
                                        mod.GitRepo = trimmed.Substring("url = ".Length).Trim();
                                        break;
                                    }
                                }
                                // Standardize the URL to match desired format
                                if (!string.IsNullOrEmpty(mod.GitRepo))
                                {
                                    if (mod.GitRepo.StartsWith("git@"))
                                    {
                                        // Convert SSH URL: git@github.com:jptrrs/OpenTheWindows.git
                                        var parts = mod.GitRepo.Split(':');
                                        if (parts.Length == 2)
                                        {
                                            mod.GitRepo = parts[1].Replace(".git", "");
                                        }
                                    }
                                    else if (mod.GitRepo.StartsWith("https://"))
                                    {
                                        // Handle HTTPS URL: https://github.com/jptrrs/OpenTheWindows.git
                                        mod.GitRepo = mod.GitRepo.Replace("https://", "").Replace(".git", "");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Failed to read Git config for mod {mod.PackageId}: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        mod.ModType = ModType.Zipped;
                    }
                }

                _allMods.Add(mod);
            }
        }

        private async Task LoadWorkshopModsAsync(string modsPath, ConcurrentBag<ModItem> mods)
        {
            if (!Directory.Exists(modsPath)) return;

            var directories = Directory.GetDirectories(modsPath);

            await Task.WhenAll(directories.Select(async dir =>
            {
                var aboutPath = Path.Combine(dir, "About", "About.xml");
                if (!File.Exists(aboutPath)) return;

                var folderName = Path.GetFileName(dir);
                var mod = await Task.Run(() => ParseAboutXml(aboutPath, folderName));
                if (mod == null) return;

                mod.Path = dir;

                if (long.TryParse(folderName, out _))
                {
                    mod.SteamId = folderName;
                    mod.SteamUrl = $"https://steamcommunity.com/workshop/filedetails/?id={folderName}";
                    mod.ModType = ModType.WorkshopL;
                }
                else
                {
                    var gitDir = Path.Combine(dir, ".git");
                    if (Directory.Exists(gitDir))
                    {
                        mod.ModType = ModType.Git;
                        var gitConfigPath = Path.Combine(gitDir, "config");
                        if (File.Exists(gitConfigPath))
                        {
                            try
                            {
                                var lines = File.ReadAllLines(gitConfigPath);
                                bool inOriginSection = false;
                                foreach (var line in lines)
                                {
                                    var trimmed = line.Trim();
                                    if (trimmed.StartsWith("[remote \"origin\"]"))
                                    {
                                        inOriginSection = true;
                                    }
                                    else if (trimmed.StartsWith("[") && inOriginSection)
                                    {
                                        inOriginSection = false;
                                    }
                                    else if (inOriginSection && trimmed.StartsWith("url = "))
                                    {
                                        mod.GitRepo = trimmed.Substring("url = ".Length).Trim();
                                        break;
                                    }
                                }
                                if (!string.IsNullOrEmpty(mod.GitRepo))
                                {
                                    if (mod.GitRepo.StartsWith("git@"))
                                    {
                                        var parts = mod.GitRepo.Split(':');
                                        if (parts.Length == 2)
                                        {
                                            mod.GitRepo = parts[1].Replace(".git", "");
                                        }
                                    }
                                    else if (mod.GitRepo.StartsWith("https://"))
                                    {
                                        mod.GitRepo = mod.GitRepo.Replace("https://", "").Replace(".git", "");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Failed to read Git config for mod {mod.PackageId}: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        mod.ModType = ModType.Zipped;
                    }
                }

                mods.Add(mod);
            }));
        }

        private ModItem ParseAboutXml(string aboutPath, string folderName = null)
        {
            try
            {
                var doc = XDocument.Load(aboutPath);
                var aboutFolder = new DirectoryInfo(aboutPath).FullName;
                var aboutFolderPath = Path.GetDirectoryName(aboutFolder);
                var modRootFolder = Directory.GetParent(aboutFolderPath)?.FullName;
                var previewImagePath = Path.Combine(aboutFolderPath, "Preview.png");

                var root = doc.Element("ModMetaData");
                if (root == null) return null;

                var name = root.Element("name")?.Value;
                if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(folderName))
                {
                    name = folderName;
                }

                var supportedVersions = root.Element("supportedVersions")?.Elements("li")
                    .Select(x => new VersionSupport(x.Value, false))
                    .ToList() ?? new List<VersionSupport>();

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
                };

                mod.ModDependencies = root.Element("modDependencies")?.Elements("li")
                    .Select(x => new ModDependency
                    {
                        PackageId = x.Element("packageId")?.Value,
                        DisplayName = x.Element("displayName")?.Value,
                        SteamWorkshopUrl = x.Element("steamWorkshopUrl")?.Value
                    }).ToList() ?? new List<ModDependency>();

                if (!string.IsNullOrEmpty(modRootFolder))
                {
                    var dateStampPath = Path.Combine(modRootFolder, "DateStamp");
                    if (File.Exists(dateStampPath))
                    {
                        mod.DateStamp = File.ReadAllText(dateStampPath).Trim();
                    }
                }

                var timestampPath = Path.Combine(aboutFolder, "timestamp.txt");
                if (File.Exists(timestampPath))
                {
                    try
                    {
                        var raw = File.ReadAllText(timestampPath).Trim();
                        mod.UpdateDate = DateTime.ParseExact(raw, "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)
                                    .ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to parse timestamp.txt for mod {mod.PackageId}: {ex.Message}");
                    }
                }
                else if (!string.IsNullOrEmpty(mod.DateStamp))
                {
                    try
                    {
                        var cleaned = mod.DateStamp.Replace("@", "").Trim();
                        cleaned = Regex.Replace(cleaned, @"\s+", " ");
                        cleaned = Regex.Replace(cleaned, @"\b(am|pm)\b", m => m.Value.ToUpperInvariant());

                        string[] formats = {
                    "dd MMM, yyyy h:mmtt",
                    "dd MMM, yyyy hh:mmtt",
                    "d MMM, yyyy h:mmtt",
                };

                        if (DateTime.TryParseExact(cleaned, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                        {
                            mod.UpdateDate = parsedDate.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            Debug.WriteLine($"Unrecognized DateStamp format for mod {mod.PackageId}: '{mod.DateStamp}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to parse DateStamp for mod {mod.PackageId}: {ex.Message}");
                    }
                }

                ParseLoadOrderRules(root, mod);

                return mod;
            }
            catch
            {
                return null;
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

        private static bool IsVersionSupported(string currentVersion, List<VersionSupport> supportedVersions)
        {
            if (string.IsNullOrEmpty(currentVersion)) return true;
            if (supportedVersions == null || !supportedVersions.Any()) return true;

            return supportedVersions.Any(v =>
                string.Equals(v.Version.Trim(), currentVersion.Trim(), StringComparison.OrdinalIgnoreCase));
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