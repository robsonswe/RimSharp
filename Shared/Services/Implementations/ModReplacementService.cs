using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using RimSharp.Infrastructure.Logging; // Assuming ILoggerService is here
using RimSharp.Infrastructure.Data;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Shared.Services.Implementations
{
    public class ModReplacementService : IModReplacementService
    {
        private const string UseThisInsteadModFolderId = "3396308787";
        private const string UseThisInsteadReplacementsSubFolder = "Replacements";
        private const string DatabaseReplacementsFileName = "replacements.json";
        // Updated: Relative path for the DB rules/replacements folder
        private readonly IDataUpdateService _dataUpdateService;

        private readonly IPathService _pathService;
        private readonly ILoggerService _logger;
        private Dictionary<string, ModReplacementInfo> _replacementsCache = null;
        private bool _isInitialized = false;
        private readonly object _lock = new object();

        // Constructor needs application base path in addition to others
        public ModReplacementService(IPathService pathService, IDataUpdateService dataUpdateService, ILoggerService logger)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _dataUpdateService = dataUpdateService ?? throw new ArgumentNullException(nameof(dataUpdateService));
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

        // --- MODIFIED METHOD ---
        public ModReplacementInfo GetReplacementByPackageId(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return null;

            var cache = GetAllReplacements(); // Ensures cache is loaded
            string normalizedId = packageId.ToLowerInvariant(); // Normalize input

            // To prevent ambiguity where a replacement mod shares a packageId with the original,
            // this method will only find replacements for mods that are defined *without* a Steam ID in our database.
            // If an original mod has a Steam ID in a replacement rule, it MUST be looked up via GetReplacementBySteamId.
            // This makes packageId a true fallback identifier for local or non-workshop mods.
            return cache.Values.FirstOrDefault(r =>
                string.IsNullOrEmpty(r.SteamId) && // CRITICAL: Only match rules where the original mod lacks a SteamID.
                !string.IsNullOrEmpty(r.ModId) &&
                r.ModId.Equals(normalizedId, StringComparison.OrdinalIgnoreCase));
        }
        // --- END OF MODIFICATION ---


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
        /// Populates the provided dictionary. Ensures directory and file exist.
        /// </summary>
        /// <returns>The number of items successfully loaded from JSON.</returns>
        private int LoadFromJson(Dictionary<string, ModReplacementInfo> targetDictionary)
        {
            // Get the full file path from the data update service
            var jsonFilePath = _dataUpdateService.GetDataFilePath(DatabaseReplacementsFileName);
            // Derive the directory path from the file path
            var dbDirectoryPath = Path.GetDirectoryName(jsonFilePath);
            int count = 0;

            try
            {
                // 1. Ensure the directory exists
                if (dbDirectoryPath != null && !Directory.Exists(dbDirectoryPath))
                {
                    _logger.LogInfo($"Creating replacement database directory: '{dbDirectoryPath}'", nameof(ModReplacementService));
                    Directory.CreateDirectory(dbDirectoryPath);
                }

                // 2. Check if the file exists, create with default if not
                if (!File.Exists(jsonFilePath))
                {
                    _logger.LogWarning($"Replacement database file '{jsonFilePath}' not found. Creating with default content.", nameof(ModReplacementService));
                    File.WriteAllText(jsonFilePath, "{\"mods\":{}}"); // Default content
                    _logger.LogInfo($"Default replacement file created at '{jsonFilePath}'.", nameof(ModReplacementService));
                    return 0; // Return 0 as no data was loaded from the (newly created) file
                }

                // 3. File exists, proceed with loading
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
                        var info = kvp.Value;
                        if (info == null) // Simplified check
                        {
                            _logger.LogWarning($"Skipping null entry in '{jsonFilePath}' for key '{kvp.Key}'.", nameof(ModReplacementService));
                            continue;
                        }

                        // Use the original mod's SteamId as the primary key for the dictionary
                        string key = info.SteamId?.Trim().ToLowerInvariant();
                        if (string.IsNullOrEmpty(key))
                        {
                            // This is a rule for a non-steam mod, so it can't be a primary key in our SteamId-keyed dictionary.
                            // It will still be added to the dictionary values and can be found by GetReplacementByPackageId.
                            // We'll use a unique key for it to store it.
                            key = $"packageid_{info.ModId?.Trim().ToLowerInvariant() ?? Guid.NewGuid().ToString()}";
                        }

                        if (targetDictionary.ContainsKey(key))
                        {
                            _logger.LogWarning($"Duplicate key '{key}' detected in '{jsonFilePath}'. Overwriting previous entry.", nameof(ModReplacementService));
                        }

                        info.ModId = info.ModId?.Trim().ToLowerInvariant();
                        info.ReplacementModId = info.ReplacementModId?.Trim().ToLowerInvariant();
                        info.Source = ReplacementSource.Database;

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
                _logger.LogException(ioEx, $"IO error accessing file '{jsonFilePath}'.", nameof(ModReplacementService));
            }
            catch (UnauthorizedAccessException uaEx)
            {
                _logger.LogException(uaEx, $"Permission error accessing directory '{dbDirectoryPath}' or file '{jsonFilePath}'.", nameof(ModReplacementService));
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

                        string key = info.SteamId?.Trim().ToLowerInvariant();

                        // Use a placeholder key if the original mod has no SteamID
                        if (string.IsNullOrEmpty(key))
                        {
                            key = $"packageid_{info.ModId ?? Guid.NewGuid().ToString()}";
                        }

                        // --- PRIORITY CHECK ---
                        // Only add if this key hasn't been loaded from the database (JSON) already.
                        // Database source has priority over 'Use This Instead'.
                        if (!targetDictionary.ContainsKey(key))
                        {
                            targetDictionary.Add(key, info);
                            count++;
                        }

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

        // Helper class to match the JSON structure {"mods": {...}}
        // Using original name from service file, assuming it matches replacements.json structure
        private class ReplacementJsonRoot
        {
            public Dictionary<string, ModReplacementInfo> Mods { get; set; }
        }
    }
}