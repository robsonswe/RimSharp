using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using RimSharp.Infrastructure.Logging; // Assuming ILoggerService is here
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Shared.Services.Implementations
{
    public class ModReplacementService : IModReplacementService
    {
        private const string UseThisInsteadModFolderId = "3396308787";
        private const string UseThisInsteadReplacementsSubFolder = "Replacements";
        private const string DatabaseReplacementsFileName = "replacements.json";
        private const string DatabaseRulesFolderName = "Rules"; // Assuming rules are in [AppBase]/Rules

        private readonly IPathService _pathService;
        private readonly string _appBasePath; // Need the application base path for the JSON file
        private readonly ILoggerService _logger;
        private Dictionary<string, ModReplacementInfo> _replacementsCache = null;
        private bool _isInitialized = false;
        private readonly object _lock = new object();

        // Constructor needs application base path in addition to others
        public ModReplacementService(IPathService pathService, string appBasePath, ILoggerService logger)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _appBasePath = appBasePath ?? throw new ArgumentNullException(nameof(appBasePath)); // Store base path
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Dictionary<string, ModReplacementInfo> GetAllReplacements()
        {
            // Double-check locking for thread safety
            if (_isInitialized)
            {
                return _replacementsCache;
            }

            lock (_lock)
            {
                if (_isInitialized) // Check again inside lock
                {
                    return _replacementsCache;
                }

                _logger.LogInfo("Initializing ModReplacementService cache.", nameof(ModReplacementService));
                _replacementsCache = LoadReplacements();
                _isInitialized = true;
                _logger.LogInfo($"ModReplacementService cache initialized. Found {_replacementsCache.Count} unique mod replacement entries.", nameof(ModReplacementService));
                return _replacementsCache;
            }
        }

        public ModReplacementInfo GetReplacementBySteamId(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId)) return null;

            var cache = GetAllReplacements(); // Ensures cache is loaded
            string normalizedId = steamId.ToLowerInvariant();

            cache.TryGetValue(normalizedId, out var replacement);
            return replacement; // Returns null if not found
        }

        public ModReplacementInfo GetReplacementByPackageId(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return null;

            var cache = GetAllReplacements(); // Ensures cache is loaded
            string normalizedId = packageId.ToLowerInvariant(); // Normalize input

            // Find the first match based on the original ModId (case-insensitive)
            // We assume ModId from the source data might need normalization too
            return cache.Values.FirstOrDefault(r =>
                !string.IsNullOrEmpty(r.ModId) &&
                r.ModId.Equals(normalizedId, StringComparison.OrdinalIgnoreCase));
        }


        private Dictionary<string, ModReplacementInfo> LoadReplacements()
        {
            // Use case-insensitive keys for SteamIDs
            var results = new Dictionary<string, ModReplacementInfo>(StringComparer.OrdinalIgnoreCase);

            int dbCount = LoadFromJson(results);
            _logger.LogDebug($"Loaded {dbCount} replacements from JSON database.", nameof(ModReplacementService));

            int utiCount = LoadFromUseThisInsteadXml(results);
            _logger.LogDebug($"Loaded {utiCount} new replacements from 'Use This Instead' XMLs (duplicates ignored).", nameof(ModReplacementService));

            return results;
        }

        /// <summary>
        /// Loads replacement data from the replacements.json file.
        /// Populates the provided dictionary.
        /// </summary>
        /// <returns>The number of items successfully loaded from JSON.</returns>
        private int LoadFromJson(Dictionary<string, ModReplacementInfo> targetDictionary)
        {
            var jsonFilePath = Path.Combine(_appBasePath, DatabaseRulesFolderName, DatabaseReplacementsFileName);
            int count = 0;

            if (!File.Exists(jsonFilePath))
            {
                _logger.LogWarning($"Replacement database file not found at '{jsonFilePath}'. Skipping.", nameof(ModReplacementService));
                return 0;
            }

            try
            {
                _logger.LogDebug($"Parsing replacement database: '{jsonFilePath}'", nameof(ModReplacementService));
                string jsonContent = File.ReadAllText(jsonFilePath);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // Handle "mods" or "Mods" etc.
                };
                var jsonData = JsonSerializer.Deserialize<ReplacementJsonRoot>(jsonContent, options);

                if (jsonData?.Mods != null)
                {
                    foreach (var kvp in jsonData.Mods)
                    {
                        // The key in the JSON *is* the SteamId, but let's double-check the data inside too
                        var info = kvp.Value;
                        if (info == null || string.IsNullOrWhiteSpace(info.SteamId))
                        {
                            _logger.LogWarning($"Skipping invalid entry in '{jsonFilePath}': Key '{kvp.Key}' has missing or empty SteamId.", nameof(ModReplacementService));
                            continue;
                        }

                        // Ensure consistency - use SteamId from the object data as the key
                        string key = info.SteamId.Trim().ToLowerInvariant();
                        if (string.IsNullOrEmpty(key))
                        {
                            _logger.LogWarning($"Skipping invalid entry in '{jsonFilePath}': Entry data for key '{kvp.Key}' has empty SteamId.", nameof(ModReplacementService));
                            continue;
                        }

                        // Normalize the PackageIDs (ModId) if they exist
                        info.ModId = info.ModId?.Trim().ToLowerInvariant();
                        info.ReplacementModId = info.ReplacementModId?.Trim().ToLowerInvariant();

                        info.Source = ReplacementSource.Database; // Set source
                        targetDictionary[key] = info; // Add or overwrite
                        count++;
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.LogException(jsonEx, $"Error parsing JSON file '{jsonFilePath}'.", nameof(ModReplacementService));
            }
            catch (IOException ioEx)
            {
                _logger.LogException(ioEx, $"IO error reading file '{jsonFilePath}'.", nameof(ModReplacementService));
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, $"Unexpected error loading replacements from '{jsonFilePath}'.", nameof(ModReplacementService));
            }

            return count;
        }

        /// <summary>
        /// Loads replacement data from the 'Use This Instead' mod's XML files.
        /// Populates the provided dictionary, *only* adding entries if the SteamId doesn't already exist.
        /// </summary>
        /// <returns>The number of new items successfully loaded from XMLs.</returns>
        private int LoadFromUseThisInsteadXml(Dictionary<string, ModReplacementInfo> targetDictionary)
        {
            var modsPath = _pathService.GetModsPath();
            int count = 0;

            if (string.IsNullOrEmpty(modsPath) || !Directory.Exists(modsPath))
            {
                _logger.LogWarning($"Mods path not found or not set ('{modsPath}'). Cannot load 'Use This Instead' replacement data.", nameof(ModReplacementService));
                return 0;
            }

            var utiModPath = Path.Combine(modsPath, UseThisInsteadModFolderId);
            if (!Directory.Exists(utiModPath))
            {
                _logger.LogInfo($"'Use This Instead' mod ({UseThisInsteadModFolderId}) not found at '{utiModPath}'. Skipping its replacement data.", nameof(ModReplacementService));
                return 0;
            }

            var replacementsPath = Path.Combine(utiModPath, UseThisInsteadReplacementsSubFolder);
            if (!Directory.Exists(replacementsPath))
            {
                _logger.LogWarning($"Replacements subfolder '{UseThisInsteadReplacementsSubFolder}' not found within '{utiModPath}'. Skipping 'Use This Instead' data.", nameof(ModReplacementService));
                return 0;
            }

            _logger.LogDebug($"Scanning 'Use This Instead' replacements folder: '{replacementsPath}'", nameof(ModReplacementService));

            try
            {
                var xmlFiles = Directory.GetFiles(replacementsPath, "*.xml", SearchOption.TopDirectoryOnly);

                foreach (var filePath in xmlFiles)
                {
                    try
                    {
                        XDocument doc = XDocument.Load(filePath);
                        XElement root = doc.Root;

                        if (root == null || root.Name != "ModReplacement")
                        {
                            _logger.LogWarning($"Skipping file '{filePath}'. Invalid root element or missing.", nameof(ModReplacementService));
                            continue;
                        }

                        var info = new ModReplacementInfo
                        {
                            Author = root.Element("Author")?.Value?.Trim(),
                            ModId = root.Element("ModId")?.Value?.Trim().ToLowerInvariant(), // Normalize
                            ModName = root.Element("ModName")?.Value?.Trim(),
                            SteamId = root.Element("SteamId")?.Value?.Trim(),
                            Versions = root.Element("Versions")?.Value?.Trim(),
                            ReplacementAuthor = root.Element("ReplacementAuthor")?.Value?.Trim(),
                            ReplacementModId = root.Element("ReplacementModId")?.Value?.Trim().ToLowerInvariant(), // Normalize
                            ReplacementName = root.Element("ReplacementName")?.Value?.Trim(),
                            ReplacementSteamId = root.Element("ReplacementSteamId")?.Value?.Trim(),
                            ReplacementVersions = root.Element("ReplacementVersions")?.Value?.Trim(),
                            Source = ReplacementSource.UseThisInstead // Set source
                        };

                        // Validate mandatory field (SteamId)
                        if (string.IsNullOrWhiteSpace(info.SteamId))
                        {
                            _logger.LogWarning($"Skipping file '{filePath}'. Missing or empty <SteamId> element.", nameof(ModReplacementService));
                            continue;
                        }

                        string key = info.SteamId.ToLowerInvariant(); // Normalize key

                        // --- PRIORITY CHECK ---
                        // Only add if this SteamId hasn't been loaded from the database (JSON) already.
                        if (!targetDictionary.ContainsKey(key))
                        {
                            targetDictionary.Add(key, info);
                            count++;
                            // _logger.LogTrace($"Added UTI replacement: {info.SteamId} -> {info.ReplacementSteamId}", nameof(ModReplacementService));
                        }
                        // else: Already exists from DB source, skip UTI version.

                    }
                    catch (System.Xml.XmlException xmlEx)
                    {
                        _logger.LogException(xmlEx, $"Error parsing XML file '{filePath}'", nameof(ModReplacementService));
                    }
                    catch (IOException ioEx)
                    {
                        _logger.LogException(ioEx, $"IO error reading file '{filePath}'", nameof(ModReplacementService));
                    }
                }
            }
            catch (UnauthorizedAccessException uaEx)
            {
                _logger.LogException(uaEx, $"Permission error accessing 'Use This Instead' mod directory '{replacementsPath}' or files within.", nameof(ModReplacementService));
            }
            catch (Exception ex) // Catch-all for unexpected errors during directory traversal etc.
            {
                _logger.LogException(ex, $"Unexpected error processing 'Use This Instead' replacements folder '{replacementsPath}'.", nameof(ModReplacementService));
            }

            return count;
        }
    }
}
