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
        private readonly IModCustomService _customService;
        private readonly IMlieVersionService _mlieVersionService; // <<< ADDED
        private readonly ILoggerService _logger; // <<< ADDED

        private static readonly HashSet<string> TextureExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".psd", ".tga", ".dds", ".bmp", ".gif", ".psb"
        };

        public ModService(IPathService pathService, IModRulesService rulesService, IModCustomService customService, IMlieVersionService mlieVersionService, ILoggerService logger)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _rulesService = rulesService ?? throw new ArgumentNullException(nameof(rulesService));
            _customService = customService ?? throw new ArgumentNullException(nameof(customService));
            _mlieVersionService = mlieVersionService ?? throw new ArgumentNullException(nameof(mlieVersionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IEnumerable<ModItem> GetLoadedMods() => _allMods;

        public void LoadMods()
        {
            _logger.LogInfo("Starting synchronous mod loading...", nameof(ModService));
            _allMods.Clear();
            _currentMajorVersion = _pathService.GetMajorGameVersion();
            _logger.LogDebug($"Current Major Game Version: {_currentMajorVersion}", nameof(ModService));

            var gamePath = _pathService.GetGamePath();
            var configPath = _pathService.GetConfigPath();
            var modsPath = _pathService.GetModsPath();

            if (string.IsNullOrEmpty(gamePath) || string.IsNullOrEmpty(configPath) || string.IsNullOrEmpty(modsPath))
            {
                _logger.LogWarning("One or more required paths (Game, Config, Mods) are not set. Mod loading aborted.", nameof(ModService));
                return;
            }

            // 1. Load Base Mod Info (About.xml) - Includes Official Versions
            _logger.LogDebug("Loading core mods...", nameof(ModService));
            LoadCoreMods(gamePath); // Already adds to _allMods
            _logger.LogDebug("Loading workshop/local mods...", nameof(ModService));
            LoadWorkshopMods(modsPath); // Already adds to _allMods

            // 2. Get Mlie Data (Do this early before applying rules/custom)
            _logger.LogDebug("Retrieving Mlie version data...", nameof(ModService));
            var mlieVersions = _mlieVersionService.GetMlieVersions();

            // 3. Apply Mlie Versions (Priority: Official -> Mlie)
            _logger.LogDebug("Applying Mlie version data...", nameof(ModService));
            ApplyMlieVersions(_allMods, mlieVersions);

            // 4. Apply Rules Service Info (Versions + Other Rules)
            _logger.LogDebug("Applying ModRulesService data...", nameof(ModService));
            // ModRulesService.ApplyRulesToMods needs modification (see Step 7)
            _rulesService.ApplyRulesToMods(_allMods);

            // 5. Apply Custom Service Info (Versions + Other Customizations)
            _logger.LogDebug("Applying ModCustomService data...", nameof(ModService));
            // ModCustomService.ApplyCustomInfoToMods needs modification (see Step 8)
            _customService.ApplyCustomInfoToMods(_allMods);

            // 6. Final Checks (Outdated Status)
            _logger.LogDebug("Calculating outdated status...", nameof(ModService));
            foreach (var mod in _allMods)
            {
                // Use the comprehensive SupportedVersions list now
                mod.IsOutdatedRW = !IsVersionSupported(_currentMajorVersion, mod.SupportedVersions) && mod.SupportedVersions.Any();
            }

            // 7. Set Active Status
            _logger.LogDebug("Setting active mod status from ModsConfig.xml...", nameof(ModService));
            SetActiveMods(configPath);
            _logger.LogInfo($"Synchronous mod loading complete. Loaded {_allMods.Count} mods.", nameof(ModService));
        }

        /// <summary>
        /// Applies version compatibility information from the Mlie service to the mods list.
        /// Only adds versions if they are not already present from the Official source.
        /// </summary>
        private void ApplyMlieVersions(List<ModItem> mods, Dictionary<string, List<string>> mlieVersions)
        {
            if (mlieVersions == null || mlieVersions.Count == 0)
            {
                _logger.LogDebug("No Mlie version data found or provided to apply.", nameof(ModService));
                return;
            }

            int versionsAdded = 0;
            foreach (var mod in mods)
            {
                if (string.IsNullOrEmpty(mod.PackageId)) continue;

                // Mlie data uses lowercase keys
                if (mlieVersions.TryGetValue(mod.PackageId.ToLowerInvariant(), out var supportedMlieVersions))
                {
                    if (supportedMlieVersions != null)
                    {
                        // Create a HashSet of existing versions for quick lookups (case-insensitive)
                        var existingVersions = mod.SupportedVersions
                                                  .Select(v => v.Version)
                                                  .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        foreach (var mlieVersion in supportedMlieVersions)
                        {
                            // Add Mlie version only if it doesn't already exist
                            if (existingVersions.Add(mlieVersion)) // .Add returns true if item was added (i.e., not present)
                            {
                                mod.SupportedVersions.Add(new VersionSupport(mlieVersion, VersionSource.Mlie, unofficial: true)); // Mlie versions are considered unofficial overrides
                                versionsAdded++;
                                //_logger.LogTrace($"Added Mlie support: {mod.PackageId} -> {mlieVersion}", nameof(ModService));
                            }
                        }
                    }
                }
            }
            _logger.LogDebug($"Applied Mlie versions. Added {versionsAdded} new version entries.", nameof(ModService));
        }





        #region Timestamp File Creation

        /// <summary>
        /// Creates the DateStamp and timestamp.txt files for a successfully downloaded mod
        /// within the specified directory.
        /// </summary>
        /// <param name="modDirectoryPath">The full path to the directory containing the downloaded mod files (e.g., the temporary SteamCMD location).</param>
        /// <param name="steamId">The Steam Workshop ID of the mod (used for context/logging).</param>
        /// <param name="publishDate">The publish date in Steam's format (d MMM yyyy @ h:mmtt).</param>
        /// <param name="standardDate">The publish date in standard format (dd/MM/yyyy HH:mm:ss).</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous file creation operation.</returns>
        public async Task CreateTimestampFilesAsync(string modDirectoryPath, string steamId, string publishDate, string standardDate)
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(modDirectoryPath))
            {
                _logger.LogError($"[CreateTimestampFilesAsync] Invalid modDirectoryPath provided: null or whitespace", nameof(ModService));
                throw new ArgumentNullException(nameof(modDirectoryPath)); // Or handle differently if needed
            }
            if (string.IsNullOrWhiteSpace(steamId) || !long.TryParse(steamId, out _))
            {
                _logger.LogError($"[CreateTimestampFilesAsync] Invalid SteamId provided: '{steamId}' for path '{modDirectoryPath}'", nameof(ModService));
                // Don't throw here, maybe log and return? Or let caller handle? Let's log and return for now.
                // Caller (SteamCmdDownloader) should handle this failure if it's critical.
                return;
            }
            if (string.IsNullOrWhiteSpace(publishDate) || string.IsNullOrWhiteSpace(standardDate))
            {
                _logger.LogWarning($"[CreateTimestampFilesAsync] Missing publishDate ('{publishDate}') or standardDate ('{standardDate}') for mod {steamId} at '{modDirectoryPath}'. Timestamp files will not be created.", nameof(ModService));
                // Throw an exception because the caller expects these files to be created
                throw new ArgumentException("PublishDate and StandardDate cannot be null or empty for timestamp creation.");
            }


            try
            {
                // Use the provided modDirectoryPath directly
                if (!Directory.Exists(modDirectoryPath))
                {
                    _logger.LogError($"[CreateTimestampFilesAsync] Target mod directory does not exist: '{modDirectoryPath}' for SteamID {steamId}", nameof(ModService));
                    throw new DirectoryNotFoundException($"Target mod directory does not exist: {modDirectoryPath}");
                }

                var aboutDir = Path.Combine(modDirectoryPath, "About");
                var dateStampPath = Path.Combine(modDirectoryPath, "DateStamp"); // Stays in root of mod folder
                var timestampPath = Path.Combine(aboutDir, "timestamp.txt"); // Goes inside About folder

                _logger.LogDebug($"[CreateTimestampFilesAsync] Preparing to write timestamps for mod {steamId} in '{modDirectoryPath}'", nameof(ModService));

                // Ensure the 'About' subdirectory exists
                Directory.CreateDirectory(aboutDir);
                _logger.LogDebug($"[CreateTimestampFilesAsync] Ensured 'About' directory exists: '{aboutDir}'", nameof(ModService));

                // Write the files asynchronously
                // Trim dates just in case
                string finalPublishDate = publishDate.Trim();
                string finalStandardDate = standardDate.Trim();

                await File.WriteAllTextAsync(dateStampPath, finalPublishDate);
                _logger.LogDebug($"[CreateTimestampFilesAsync] Wrote DateStamp: '{dateStampPath}'", nameof(ModService));

                await File.WriteAllTextAsync(timestampPath, finalStandardDate);
                _logger.LogDebug($"[CreateTimestampFilesAsync] Wrote timestamp.txt: '{timestampPath}'", nameof(ModService));

                _logger.LogInfo($"[CreateTimestampFilesAsync] Successfully created timestamp files for mod {steamId} in '{modDirectoryPath}'", nameof(ModService));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError($"[CreateTimestampFilesAsync] Permission error writing timestamp files for mod {steamId} at '{modDirectoryPath}': {ex.Message}", nameof(ModService));
                throw; // Re-throw so the caller knows the operation failed
            }
            catch (IOException ex)
            {
                _logger.LogError($"[CreateTimestampFilesAsync] IO error writing timestamp files for mod {steamId} at '{modDirectoryPath}': {ex.Message}", nameof(ModService));
                throw; // Re-throw
            }
            catch (Exception ex) // Catch unexpected errors
            {
                _logger.LogError($"[CreateTimestampFilesAsync] Unexpected error writing timestamp files for mod {steamId} at '{modDirectoryPath}': {ex.Message}\n{ex.StackTrace}", nameof(ModService));
                throw; // Re-throw
            }
        }

        #endregion

        public async Task LoadModsAsync()
        {
            _logger.LogInfo("Starting asynchronous mod loading...", nameof(ModService));
            var gamePath = _pathService.GetGamePath();
            var configPath = _pathService.GetConfigPath();
            var modsPath = _pathService.GetModsPath();
            _currentMajorVersion = _pathService.GetMajorGameVersion();
            _logger.LogDebug($"Current Major Game Version: {_currentMajorVersion}", nameof(ModService));


            if (string.IsNullOrEmpty(gamePath) || string.IsNullOrEmpty(configPath) || string.IsNullOrEmpty(modsPath))
            {
                _logger.LogWarning("One or more required paths (Game, Config, Mods) are not set. Mod loading aborted.", nameof(ModService));
                _allMods.Clear();
                return;
            }

            var mods = new ConcurrentBag<ModItem>();

            // 1. Load Base Mod Info (About.xml) - Includes Official Versions
            _logger.LogDebug("Loading core and workshop/local mods concurrently...", nameof(ModService));
            await Task.WhenAll(
                Task.Run(() => LoadCoreModsAsync(gamePath, mods)),
                Task.Run(() => LoadWorkshopModsAsync(modsPath, mods))
            );

            // Update the main collection - do this before applying other sources
            _allMods.Clear();
            _allMods.AddRange(mods);
            _logger.LogDebug($"Initial parsing complete. Found {_allMods.Count} mods.", nameof(ModService));

            // --- Apply information in priority order ---

            // 2. Get Mlie Data (Do this early before applying rules/custom)
            _logger.LogDebug("Retrieving Mlie version data...", nameof(ModService));
            var mlieVersions = _mlieVersionService.GetMlieVersions(); // Sync call ok, as it caches

            // 3. Apply Mlie Versions (Priority: Official -> Mlie)
            _logger.LogDebug("Applying Mlie version data...", nameof(ModService));
            ApplyMlieVersions(_allMods, mlieVersions); // Apply to the list directly

            // 4. Apply Rules Service Info (Versions + Other Rules)
            _logger.LogDebug("Applying ModRulesService data...", nameof(ModService));
            // ModRulesService.ApplyRulesToMods needs modification (see Step 7)
            await Task.Run(() => _rulesService.ApplyRulesToMods(_allMods));

            // 5. Apply Custom Service Info (Versions + Other Customizations)
            _logger.LogDebug("Applying ModCustomService data...", nameof(ModService));
            // ModCustomService.ApplyCustomInfoToMods needs modification (see Step 8)
            // Note: ApplyCustomInfoToMods uses EnsureInitializedAsync().Wait() internally, which isn't ideal.
            // If ModCustomService becomes truly async, await it here. For now, Task.Run is fine.
            await Task.Run(() => _customService.ApplyCustomInfoToMods(_allMods));


            // 6. Final Checks (Outdated Status)
            _logger.LogDebug("Calculating outdated status...", nameof(ModService));
            foreach (var mod in _allMods)
            {
                // Use the comprehensive SupportedVersions list now
                mod.IsOutdatedRW = !IsVersionSupported(_currentMajorVersion, mod.SupportedVersions) && mod.SupportedVersions.Any();
            }

            // 7. Set Active Status
            _logger.LogDebug("Setting active mod status from ModsConfig.xml...", nameof(ModService));
            await Task.Run(() => SetActiveMods(configPath));

            _logger.LogInfo($"Asynchronous mod loading complete. Loaded {_allMods.Count} mods.", nameof(ModService));
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
                mod.Assemblies = CheckForAssemblies(mod.Path);
                mod.Textures = CheckForTextures(mod.Path);
                mod.SizeInfo = CalculateModSize(mod); // <-- ADDED

                // Set core/expansion flags
                bool isCoreMod = mod.PackageId == "Ludeon.RimWorld";
                mod.ModType = isCoreMod ? ModType.Core : ModType.Expansion;
                // Append "[DLC]" to name if it's an expansion
                if (mod.ModType == ModType.Expansion && !string.IsNullOrEmpty(mod.Name)) // Check ModType
                {
                    mod.Name = $"{mod.Name} [DLC]";
                }
                // If it's a Core or Expansion mod, its supported version is the current game version.
                // Only add as a fallback if the About.xml list was empty.
                if ((mod.ModType == ModType.Core || mod.ModType == ModType.Expansion) && !mod.SupportedVersions.Any())
                {
                    if (!string.IsNullOrEmpty(_currentMajorVersion) && !_currentMajorVersion.StartsWith("N/A"))
                    {
                        mod.SupportedVersions.Add(new VersionSupport(_currentMajorVersion, VersionSource.Official, unofficial: false));
                    }
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
                mod.Assemblies = CheckForAssemblies(mod.Path);
                mod.Textures = CheckForTextures(mod.Path);
                mod.SizeInfo = await Task.Run(() => CalculateModSize(mod)); // <-- ADDED

                // Set core/expansion flags
                bool isCoreMod = mod.PackageId == "Ludeon.RimWorld";
                mod.ModType = isCoreMod ? ModType.Core : ModType.Expansion;
                if (mod.ModType == ModType.Expansion && !string.IsNullOrEmpty(mod.Name))
                {
                    mod.Name = $"{mod.Name} [DLC]";
                }
                // If it's a Core or Expansion mod, its supported version is the current game version.
                // Only add as a fallback if the About.xml list was empty.
                if ((mod.ModType == ModType.Core || mod.ModType == ModType.Expansion) && !mod.SupportedVersions.Any())
                {
                    if (!string.IsNullOrEmpty(_currentMajorVersion) && !_currentMajorVersion.StartsWith("N/A"))
                    {
                        mod.SupportedVersions.Add(new VersionSupport(_currentMajorVersion, VersionSource.Official, unofficial: false));
                    }
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
                mod.Assemblies = CheckForAssemblies(mod.Path);
                mod.Textures = CheckForTextures(mod.Path);
                mod.SizeInfo = CalculateModSize(mod); // <-- ADDED

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
                mod.Assemblies = CheckForAssemblies(mod.Path);
                mod.Textures = CheckForTextures(mod.Path);
                mod.SizeInfo = await Task.Run(() => CalculateModSize(mod)); // <-- ADDED

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

        #region Mod Size Calculation

        private ModSizeInfo CalculateModSize(ModItem mod)
        {
            if (string.IsNullOrEmpty(mod.Path) || !Directory.Exists(mod.Path))
            {
                return new ModSizeInfo();
            }

            try
            {
                var strippablePaths = GetStrippablePaths(mod);
                var allFiles = new DirectoryInfo(mod.Path).EnumerateFiles("*", SearchOption.AllDirectories);
                var validFiles = allFiles
                    .Where(f => !strippablePaths.Any(junk => f.FullName.StartsWith(junk, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                long totalSize = validFiles.Sum(f => f.Length);

                string aboutFolderPathWithSeparator = Path.Combine(mod.Path, "About") + Path.DirectorySeparatorChar;
                var textureFiles = validFiles
                    .Where(f => TextureExtensions.Contains(f.Extension) &&
                                !f.FullName.StartsWith(aboutFolderPathWithSeparator, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var textureGroups = textureFiles
                    .GroupBy(f => Path.Combine(f.DirectoryName ?? "", Path.GetFileNameWithoutExtension(f.Name)), StringComparer.OrdinalIgnoreCase);

                long minTextureSize = 0;
                long maxTextureSize = 0;

                foreach (var group in textureGroups)
                {
                    var pngFile = group.FirstOrDefault(f => f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase));
                    var ddsFile = group.FirstOrDefault(f => f.Extension.Equals(".dds", StringComparison.OrdinalIgnoreCase));

                    if (ddsFile != null && pngFile != null)
                    {
                        minTextureSize += ddsFile.Length;
                        maxTextureSize += pngFile.Length;
                    }
                    else if (ddsFile != null)
                    {
                        minTextureSize += ddsFile.Length;
                        maxTextureSize += ddsFile.Length;
                    }
                    else if (pngFile != null)
                    {
                        minTextureSize += pngFile.Length;
                        maxTextureSize += pngFile.Length;
                    }
                }

                return new ModSizeInfo
                {
                    TotalSize = totalSize,
                    MinTextureSize = minTextureSize,
                    MaxTextureSize = maxTextureSize,
                    // VRAM properties are no longer set here
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"CRITICAL ERROR during disk size calculation for '{mod.Name}': {ex.Message}\n{ex.StackTrace}", nameof(ModService));
                return new ModSizeInfo();
            }
        }

        private HashSet<string> GetStrippablePaths(ModItem mod)
        {
            var potentialDeletions = new Dictionary<string, (long Size, bool isDir)>(StringComparer.OrdinalIgnoreCase);
            ScanForStrippablePathsRecursively(
                new DirectoryInfo(mod.Path),
                mod.Path,
                mod,
                _pathService.GetMajorGameVersion(),
                isModRoot: true,
                potentialDeletions
            );
            return new HashSet<string>(potentialDeletions.Keys, StringComparer.OrdinalIgnoreCase);
        }

        private void ScanForStrippablePathsRecursively(
            DirectoryInfo currentDir,
            string modRootPath,
            ModItem mod,
            string majorGameVersion,
            bool isModRoot,
            IDictionary<string, (long Size, bool isDir)> potentialDeletions)
        {
            if (potentialDeletions.ContainsKey(currentDir.FullName)) return;

            var junkNameComponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Source", ".git", ".github", ".vs", ".vscode" };
            var exactJunkFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "Properties" };
            var junkFileExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".csproj", ".csproj.user", ".sln" };
            char[] nameDelimiters = { '-', '_', ' ' };

            var versionFolders = new List<(string VersionString, DirectoryInfo Dir)>();
            var otherFolders = new List<DirectoryInfo>();

            try
            {
                foreach (var dir in currentDir.EnumerateDirectories())
                {
                    var potentialVersionString = dir.Name.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? dir.Name.Substring(1) : dir.Name;
                    var versionString = new string(potentialVersionString.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).TrimEnd('.');

                    if (Version.TryParse(versionString, out _))
                    {
                        versionFolders.Add((versionString, dir));
                    }
                    else
                    {
                        otherFolders.Add(dir);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning($"Could not enumerate directories in '{currentDir.FullName}' for size calculation: {ex.Message}", nameof(ModService));
                return;
            }


            if (versionFolders.Any())
            {
                var essentialVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (isModRoot)
                {
                    var supportedVersions = mod.SupportedVersionStrings.ToHashSet();
                    var latestAvailableSupported = versionFolders
                        .Where(vf => supportedVersions.Contains(vf.VersionString))
                        .Select(vf => Version.TryParse(vf.VersionString, out var v) ? v : null)
                        .Where(v => v != null).OrderByDescending(v => v).FirstOrDefault();

                    if (latestAvailableSupported != null) essentialVersions.Add($"{latestAvailableSupported.Major}.{latestAvailableSupported.Minor}");

                    if (supportedVersions.Contains(majorGameVersion) && versionFolders.Any(vf => vf.VersionString.Equals(majorGameVersion)))
                    {
                        essentialVersions.Add(majorGameVersion);
                    }
                }
                else
                {
                    var latestAvailable = versionFolders
                        .Select(vf => Version.TryParse(vf.VersionString, out var v) ? v : null)
                        .Where(v => v != null).OrderByDescending(v => v).FirstOrDefault();
                    if (latestAvailable != null) essentialVersions.Add($"{latestAvailable.Major}.{latestAvailable.Minor}");
                }

                foreach (var (versionString, dir) in versionFolders)
                {
                    if (!essentialVersions.Contains(versionString))
                    {
                        potentialDeletions[dir.FullName] = (0, true);
                    }
                }
            }

            foreach (var otherDir in otherFolders)
            {
                bool isJunkByNameComponent = otherDir.Name.StartsWith(".git", StringComparison.OrdinalIgnoreCase) ||
                                             otherDir.Name.Split(nameDelimiters).Any(part => junkNameComponents.Contains(part));
                bool isExactJunkFolder = exactJunkFolders.Contains(otherDir.Name);

                if (isJunkByNameComponent || isExactJunkFolder)
                {
                    potentialDeletions[otherDir.FullName] = (0, true);
                }
                else
                {
                    ScanForStrippablePathsRecursively(otherDir, modRootPath, mod, majorGameVersion, false, potentialDeletions);
                }
            }

            try
            {
                foreach (var file in currentDir.EnumerateFiles())
                {
                    if (file.Name.StartsWith(".git", StringComparison.OrdinalIgnoreCase) || junkFileExtensions.Contains(file.Extension))
                    {
                        potentialDeletions[file.FullName] = (file.Length, false);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning($"Could not enumerate files in '{currentDir.FullName}' for size calculation: {ex.Message}", nameof(ModService));
            }

            if (isModRoot)
            {
                var loadFoldersFile = new DirectoryInfo(modRootPath).GetFiles("loadFolders.xml", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (loadFoldersFile != null && potentialDeletions.Any())
                {
                    try
                    {
                        var doc = XDocument.Load(loadFoldersFile.FullName);
                        var pathsToKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var rootEssentialVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var supported = mod.SupportedVersionStrings.ToHashSet();
                        if (supported.Contains(majorGameVersion)) rootEssentialVersions.Add(majorGameVersion);

                        var latestSupported = mod.SupportedVersionStrings
                            .Select(vStr => Version.TryParse(vStr, out var v) ? v : null)
                            .Where(v => v != null).OrderByDescending(v => v).FirstOrDefault();
                        if (latestSupported != null) rootEssentialVersions.Add($"{latestSupported.Major}.{latestSupported.Minor}");

                        foreach (var version in rootEssentialVersions)
                        {
                            var versionNode = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("v" + version, StringComparison.OrdinalIgnoreCase));
                            if (versionNode != null)
                            {
                                foreach (var path in versionNode.Elements("li").Select(li => Path.GetFullPath(Path.Combine(modRootPath, li.Value.Trim().Replace('/', Path.DirectorySeparatorChar)))))
                                {
                                    pathsToKeep.Add(path);
                                }
                            }
                        }

                        var pathsToRescue = potentialDeletions.Keys
                            .Where(path => pathsToKeep.Any(keepPath => path.Equals(keepPath, StringComparison.OrdinalIgnoreCase) || path.StartsWith(keepPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                            .ToList();

                        foreach (var path in pathsToRescue)
                        {
                            potentialDeletions.Remove(path);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Error parsing loadFolders.xml for mod size calc '{mod.Name}': {ex.Message}", nameof(ModService));
                    }
                }
            }
        }
        #endregion

        /// <summary>
        /// Checks if a mod directory contains C# assemblies (.dll files) anywhere inside it.
        /// </summary>
        /// <param name="modDirectoryPath">The root path of the mod directory.</param>
        /// <returns>True if any .dll files are found, false otherwise.</returns>
        private bool CheckForAssemblies(string modDirectoryPath)
        {
            if (string.IsNullOrEmpty(modDirectoryPath) || !Directory.Exists(modDirectoryPath))
            {
                return false;
            }

            try
            {
                // Search the entire mod directory recursively for any .dll file.
                // Using EnumerateFiles and Any() is efficient as it stops on the first match.
                return Directory.EnumerateFiles(modDirectoryPath, "*.dll", SearchOption.AllDirectories).Any();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                // Log the error but don't crash the loading process. Assume no assemblies if we can't check.
                _logger.LogWarning($"Could not check for assemblies in '{modDirectoryPath}' due to an error: {ex.Message}", nameof(ModService));
                Debug.WriteLine($"Could not check for assemblies in '{modDirectoryPath}' due to an error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if a mod directory contains texture files, ignoring the 'About' folder.
        /// </summary>
        /// <param name="modDirectoryPath">The root path of the mod directory.</param>
        /// <returns>True if texture files are found, false otherwise.</returns>
        private bool CheckForTextures(string modDirectoryPath)
        {
            if (string.IsNullOrEmpty(modDirectoryPath) || !Directory.Exists(modDirectoryPath))
            {
                return false;
            }

            try
            {
                // We need to exclude the 'About' folder, which contains non-game preview images.
                string aboutFolderPathWithSeparator = Path.Combine(modDirectoryPath, "About") + Path.DirectorySeparatorChar;

                // Enumerate all files recursively
                return Directory.EnumerateFiles(modDirectoryPath, "*.*", SearchOption.AllDirectories)
                                .Any(filePath =>
                                {
                                    // Check if the file is NOT inside the 'About' folder.
                                    if (filePath.StartsWith(aboutFolderPathWithSeparator, StringComparison.OrdinalIgnoreCase))
                                    {
                                        return false; // It's in the About folder, so ignore it.
                                    }
                                    // Check if the file has a valid texture extension.
                                    return TextureExtensions.Contains(Path.GetExtension(filePath));
                                });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                _logger.LogWarning($"Could not check for textures in '{modDirectoryPath}' due to an error: {ex.Message}", nameof(ModService));
                Debug.WriteLine($"Could not check for textures in '{modDirectoryPath}' due to an error: {ex.Message}");
                return false;
            }
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
                    .Select(x => new VersionSupport(x.Value, VersionSource.Official, unofficial: false))
                    .ToList() ?? new List<VersionSupport>();


                string urlFromXml = root.Element("url")?.Value?.Trim(); // Trim whitespace from the input.
                string validatedUrl = null;

                if (!string.IsNullOrWhiteSpace(urlFromXml))
                {
                    // First, check if the string is already a well-formed absolute URI with a web scheme.
                    if (Uri.TryCreate(urlFromXml, UriKind.Absolute, out Uri uriResult) &&
                        (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                    {
                        validatedUrl = urlFromXml; // The URL is valid as-is.
                    }
                    // Otherwise, check if it is a valid schemaless domain. To be valid,
                    // it must not contain a scheme and MUST contain a dot.
                    // This correctly rejects single-word strings like "none".
                    else if (!urlFromXml.Contains("://") && urlFromXml.Contains("."))
                    {
                        if (Uri.TryCreate("http://" + urlFromXml, UriKind.Absolute, out Uri tempUri) && tempUri.Host.Equals(urlFromXml, StringComparison.OrdinalIgnoreCase))
                        {
                            validatedUrl = "http://" + urlFromXml; // Prepend scheme to make it navigable.
                        }
                    }
                }

                var mod = new ModItem
                {
                    Name = name,
                    PackageId = root.Element("packageId")?.Value,
                    Authors = root.Element("author")?.Value ??
                              string.Join(", ", root.Element("authors")?.Elements("li").Select(x => x.Value) ?? Array.Empty<string>()),
                    Description = root.Element("description")?.Value,
                    ModVersion = root.Element("modVersion")?.Value,
                    ModIconPath = root.Element("modIconPath")?.Value,
                    Url = validatedUrl,
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
            var incompatibleIds = root.Element("incompatibleWith")?.Elements("li")
                .Select(x => x.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            if (incompatibleIds == null) return;

            foreach (var packageId in incompatibleIds)
            {
                var reason = $"Incompatible according to '{mod.Name}' authors";

                mod.IncompatibleWith.Add(packageId, new ModIncompatibilityRule { HardIncompatibility = true, Comment = new List<string> { reason } });
            }
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
        public async Task SaveCustomModInfoAsync(string packageId, ModCustomInfo customInfo)
        {
            if (string.IsNullOrEmpty(packageId))
                throw new ArgumentNullException(nameof(packageId));

            if (customInfo == null)
                throw new ArgumentNullException(nameof(customInfo));

            await _customService.SaveCustomModInfoAsync(packageId, customInfo);

            // After saving, reload the custom info into the mods
            _customService.ApplyCustomInfoToMods(_allMods);
        }

        // Add method for removing custom mod info
        public async Task RemoveCustomModInfoAsync(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
                throw new ArgumentNullException(nameof(packageId));

            await _customService.RemoveCustomModInfoAsync(packageId);

            // Reload mods to reflect the removal
            await LoadModsAsync();
        }


        // Add method to get custom mod info
        public ModCustomInfo GetCustomModInfo(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
                throw new ArgumentNullException(nameof(packageId));

            if (_customService == null)
                throw new InvalidOperationException("IModCustomService was not initialized.");

            return _customService.GetCustomModInfo(packageId);
        }

    }
}