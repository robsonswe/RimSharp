using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RimSharp.Infrastructure.Logging; // Assuming ILoggerService is here
using RimSharp.Shared.Models;        // For ModDictionaryEntry
using RimSharp.Shared.Services.Contracts; // For IPathService, ILoggerService, IModDictionaryService

namespace RimSharp.Shared.Services.Implementations // Adjust namespace as needed
{
    public class ModDictionaryService : IModDictionaryService
    {
        private const string DatabaseDictionaryFileName = "db.json";
        // Re-use the same relative path as replacements.json
        private readonly string _databaseRulesDbRelativePath = Path.Combine("Rules", "db");

        private readonly IPathService _pathService; // Now needed for GetEntryByPackageId logic
        private readonly string _appBasePath;
        private readonly ILoggerService _logger;
        private Dictionary<string, ModDictionaryEntry> _dictionaryCache = null; // Cache keyed by lowercase SteamId
        private bool _isInitialized = false;
        private readonly object _lock = new object();

        // Constructor remains the same - IPathService is already injected
        public ModDictionaryService(IPathService pathService, string appBasePath, ILoggerService logger)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _appBasePath = appBasePath ?? throw new ArgumentNullException(nameof(appBasePath));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Dictionary<string, ModDictionaryEntry> GetAllEntries()
        {
            // Double-check locking for thread safety
            if (_isInitialized)
            {
                return _dictionaryCache;
            }

            lock (_lock)
            {
                if (_isInitialized) // Check again inside lock
                {
                    return _dictionaryCache;
                }

                _logger.LogInfo("Initializing ModDictionaryService cache.", nameof(ModDictionaryService));
                _dictionaryCache = LoadEntries(); // Load from db.json
                _isInitialized = true;
                _logger.LogInfo($"ModDictionaryService cache initialized. Found {_dictionaryCache.Count} unique mod dictionary entries.", nameof(ModDictionaryService));
                return _dictionaryCache;
            }
        }

        public ModDictionaryEntry GetEntryBySteamId(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId)) return null;

            var cache = GetAllEntries(); // Ensures cache is loaded
            string normalizedId = steamId.ToLowerInvariant();

            cache.TryGetValue(normalizedId, out var entry);
            return entry; // Returns null if not found
        }

        public ModDictionaryEntry GetEntryByPackageId(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return null;

            var cache = GetAllEntries(); // Ensures cache is loaded
            string normalizedId = packageId.ToLowerInvariant();

            // 1. Find all potential candidates matching the packageId (case-insensitive)
            var candidates = cache.Values.Where(entry =>
                !string.IsNullOrEmpty(entry.PackageId) &&
                entry.PackageId.Equals(normalizedId, StringComparison.OrdinalIgnoreCase))
                .ToList(); // Materialize the list

            // 2. Handle simple cases: no matches or only one match
            if (candidates.Count == 0)
            {
                return null;
            }
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            // 3. Multiple candidates exist - apply prioritization logic
            _logger.LogDebug($"Found {candidates.Count} potential dictionary entries for packageId '{packageId}'. Applying prioritization.", nameof(ModDictionaryService));

            // 3a. Get the current major game version
            string majorGameVersion = _pathService.GetMajorGameVersion();
            bool isVersionValid = !string.IsNullOrEmpty(majorGameVersion) && !majorGameVersion.StartsWith("N/A");

            if (isVersionValid)
            {
                 _logger.LogDebug($"Prioritizing based on major game version: '{majorGameVersion}'", nameof(ModDictionaryService));
            }
            else
            {
                 _logger.LogDebug($"Cannot prioritize based on game version (Version: '{majorGameVersion}'). Proceeding with Published status.", nameof(ModDictionaryService));
            }

            // 3b. Sort the candidates based on the rules
            //    - Rule 1: Prioritize matching major game version (if version is valid)
            //    - Rule 2: Prioritize Published = true
            //    - Rule 3: FirstOrDefault handles the final tie-break if still needed
            var prioritizedEntry = candidates
                .OrderByDescending(entry =>
                    isVersionValid &&
                    entry.Versions != null && // Safety check
                    entry.Versions.Contains(majorGameVersion))
                .ThenByDescending(entry => entry.Published)
                .FirstOrDefault(); // Select the highest priority entry

            if (prioritizedEntry != null)
            {
                 _logger.LogDebug($"Selected entry for packageId '{packageId}' with SteamId '{prioritizedEntry.SteamId}' after prioritization.", nameof(ModDictionaryService));
            }
            else
            {
                 // This case should theoretically not happen if candidates.Count > 0,
                 // but log just in case.
                 _logger.LogWarning($"Prioritization resulted in null for packageId '{packageId}' despite having {candidates.Count} candidates.", nameof(ModDictionaryService));
            }

            return prioritizedEntry;
        }

        public List<ModDictionaryEntry> GetAllEntriesByPackageId(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return new List<ModDictionaryEntry>();

            var cache = GetAllEntries(); // Ensures cache is loaded
            string normalizedId = packageId.ToLowerInvariant();

            // Find all matches based on the PackageId (case-insensitive)
            return cache.Values.Where(entry =>
                !string.IsNullOrEmpty(entry.PackageId) &&
                entry.PackageId.Equals(normalizedId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private Dictionary<string, ModDictionaryEntry> LoadEntries()
        {
            // Use case-insensitive keys for SteamIDs in the dictionary
            var results = new Dictionary<string, ModDictionaryEntry>(StringComparer.OrdinalIgnoreCase);
            int loadedCount = LoadFromJson(results);
            _logger.LogDebug($"Loaded {loadedCount} entries from dictionary JSON database.", nameof(ModDictionaryService));
            return results;
        }

        private int LoadFromJson(Dictionary<string, ModDictionaryEntry> targetDictionary)
        {
            var dbDirectoryPath = Path.Combine(_appBasePath, _databaseRulesDbRelativePath);
            var jsonFilePath = Path.Combine(dbDirectoryPath, DatabaseDictionaryFileName);
            int count = 0;

            try
            {
                // 1. Ensure the directory exists
                if (!Directory.Exists(dbDirectoryPath))
                {
                    _logger.LogInfo($"Creating dictionary database directory: '{dbDirectoryPath}'", nameof(ModDictionaryService));
                    Directory.CreateDirectory(dbDirectoryPath);
                }

                // 2. Check if the file exists, create with default if not
                if (!File.Exists(jsonFilePath))
                {
                    _logger.LogWarning($"Dictionary database file '{jsonFilePath}' not found. Creating with default content.", nameof(ModDictionaryService));
                    File.WriteAllText(jsonFilePath, "{\"mods\":{}}"); // Default content (empty mods object)
                    _logger.LogInfo($"Default dictionary file created at '{jsonFilePath}'.", nameof(ModDictionaryService));
                    return 0; // Return 0 as no data was loaded
                }

                // 3. File exists, proceed with loading
                _logger.LogDebug($"Parsing dictionary database: '{jsonFilePath}'", nameof(ModDictionaryService));
                string jsonContent = File.ReadAllText(jsonFilePath);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // Handle "mods" or "Mods", "published" or "Published" etc.
                };

                // Deserialize into the structure matching db.json
                var jsonData = JsonSerializer.Deserialize<ModDictionaryJsonRoot>(jsonContent, options);

                if (jsonData?.Mods != null)
                {
                    // Iterate through Package IDs
                    foreach (var packageKvp in jsonData.Mods)
                    {
                        string packageIdRaw = packageKvp.Key;
                        var steamEntries = packageKvp.Value;

                        if (string.IsNullOrWhiteSpace(packageIdRaw) || steamEntries == null)
                        {
                            _logger.LogWarning($"Skipping entry in '{jsonFilePath}': Invalid packageId ('{packageIdRaw}') or null steam entries.", nameof(ModDictionaryService));
                            continue;
                        }

                        string packageIdLower = packageIdRaw.ToLowerInvariant();

                        // Iterate through Steam IDs within the Package ID
                        foreach (var steamKvp in steamEntries)
                        {
                            string steamIdRaw = steamKvp.Key;
                            var details = steamKvp.Value;

                            if (string.IsNullOrWhiteSpace(steamIdRaw) || details == null)
                            {
                                _logger.LogWarning($"Skipping entry for package '{packageIdRaw}' in '{jsonFilePath}': Invalid steamId ('{steamIdRaw}') or null details.", nameof(ModDictionaryService));
                                continue;
                            }

                            string steamIdLower = steamIdRaw.ToLowerInvariant();

                            // Create the flattened entry
                            var entry = new ModDictionaryEntry
                            {
                                PackageId = packageIdLower,
                                SteamId = steamIdLower,
                                Name = details.Name,
                                Versions = details.Versions ?? new List<string>(), // Ensure list exists
                                Authors = details.Authors,
                                Published = details.Published // Ensure this property exists in ModDetails and ModDictionaryEntry
                            };

                            // Add to the target dictionary using lowercase SteamId as the key
                            if (targetDictionary.ContainsKey(steamIdLower))
                            {
                                _logger.LogWarning($"Duplicate Steam ID '{steamIdLower}' found in '{jsonFilePath}'. Overwriting entry. Previous package: '{targetDictionary[steamIdLower].PackageId}', New package: '{packageIdLower}'.", nameof(ModDictionaryService));
                            }
                            targetDictionary[steamIdLower] = entry;
                            count++;
                        }
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                // Updated error message to include potential boolean issues
                _logger.LogException(jsonEx, $"Error parsing JSON file '{jsonFilePath}'. Check for invalid boolean values for 'published' (must be true or false, not strings like \"true\").", nameof(ModDictionaryService));
            }
            catch (IOException ioEx)
            {
                _logger.LogException(ioEx, $"IO error accessing file '{jsonFilePath}'.", nameof(ModDictionaryService));
            }
            catch (UnauthorizedAccessException uaEx)
            {
                _logger.LogException(uaEx, $"Permission error accessing directory '{dbDirectoryPath}' or file '{jsonFilePath}'.", nameof(ModDictionaryService));
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, $"Unexpected error loading dictionary entries from '{jsonFilePath}'.", nameof(ModDictionaryService));
            }

            return count;
        }

        // --- Helper classes for JSON deserialization ---

        /// <summary>
        /// Matches the root structure of db.json: { "mods": { ... } }
        /// </summary>
        private class ModDictionaryJsonRoot
        {
            // Key: packageId (string)
            // Value: Dictionary where Key is steamId (string) and Value is ModDetails
            public Dictionary<string, Dictionary<string, ModDetails>> Mods { get; set; }
        }

        /// <summary>
        /// Matches the details stored under each steamId in db.json
        /// </summary>
        private class ModDetails
        {
            public string Name { get; set; }
            public List<string> Versions { get; set; }
            public string Authors { get; set; }
            // Ensure this matches the case in your JSON or use [JsonPropertyName("published")] if needed
            public bool Published { get; set; }
        }
    }
}