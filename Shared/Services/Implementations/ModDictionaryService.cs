using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RimSharp.Infrastructure.Logging; 
using RimSharp.Infrastructure.Data;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Shared.Services.Implementations
{
    public class ModDictionaryService : IModDictionaryService
    {
        private const string DatabaseDictionaryFileName = "db.json";
        private readonly IPathService _pathService;
        private readonly ILoggerService _logger;
        private readonly IDataUpdateService _dataUpdateService;
        private readonly Infrastructure.Mods.ModInfo.ModInfoSourceRegistry? _sourceRegistry;
        private Dictionary<string, ModDictionaryEntry>? _dictionaryCache;
        private bool _isInitialized = false;
        private readonly object _lock = new object();

        public ModDictionaryService(
            IPathService pathService,
            IDataUpdateService dataUpdateService,
            ILoggerService logger,
            Infrastructure.Mods.ModInfo.ModInfoSourceRegistry? sourceRegistry = null)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _dataUpdateService = dataUpdateService ?? throw new ArgumentNullException(nameof(dataUpdateService)); 
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sourceRegistry = sourceRegistry;
        }

        public void Invalidate()
        {
            lock (_lock)
            {
                _dictionaryCache = null;
                _isInitialized = false;
            }
        }

        public Dictionary<string, ModDictionaryEntry> GetAllEntries()
        {
            if (_isInitialized && _dictionaryCache != null)
            {
                return _dictionaryCache;
            }

            lock (_lock)
            {
                if (_isInitialized && _dictionaryCache != null)
                {
                    return _dictionaryCache;
                }

                _logger.LogInfo("Initializing ModDictionaryService cache.", nameof(ModDictionaryService));
                _dictionaryCache = LoadEntries();
                _isInitialized = true;
                _logger.LogInfo($"ModDictionaryService cache initialized. Found {_dictionaryCache.Count} unique mod dictionary entries.", nameof(ModDictionaryService));
                return _dictionaryCache;
            }
        }

        public ModDictionaryEntry? GetEntryBySteamId(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId)) return null;

            var cache = GetAllEntries(); 
            string normalizedId = steamId.ToLowerInvariant();

            cache.TryGetValue(normalizedId, out var entry);
            return entry; 
        }

        public ModDictionaryEntry? GetEntryByPackageId(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return null;

            var cache = GetAllEntries(); 
            string normalizedId = packageId.ToLowerInvariant();

            var candidates = cache.Values.Where(entry =>
                !string.IsNullOrEmpty(entry.PackageId) &&
                entry.PackageId.Equals(normalizedId, StringComparison.OrdinalIgnoreCase))
                .ToList(); 

            if (candidates.Count == 0)
            {
                return null;
            }
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            _logger.LogDebug($"Found {candidates.Count} potential dictionary entries for packageId '{packageId}'. Applying prioritization.", nameof(ModDictionaryService));

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

            var prioritizedEntry = candidates
                .OrderByDescending(entry =>
                    isVersionValid &&
                    entry.Versions != null &&
                    entry.Versions.Contains(majorGameVersion))
                .ThenByDescending(entry => entry.Published)
                .FirstOrDefault();

            if (prioritizedEntry != null)
            {
                 _logger.LogDebug($"Selected entry for packageId '{packageId}' with SteamId '{prioritizedEntry.SteamId}' after prioritization.", nameof(ModDictionaryService));
            }
            else
            {
                 _logger.LogWarning($"Prioritization resulted in null for packageId '{packageId}' despite having {candidates.Count} candidates.", nameof(ModDictionaryService));
            }

            return prioritizedEntry;
        }

        public List<ModDictionaryEntry> GetAllEntriesByPackageId(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return new List<ModDictionaryEntry>();

            var cache = GetAllEntries(); 
            string normalizedId = packageId.ToLowerInvariant();

            return cache.Values.Where(entry =>
                !string.IsNullOrEmpty(entry.PackageId) &&
                entry.PackageId.Equals(normalizedId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private Dictionary<string, ModDictionaryEntry> LoadEntries()
        {
            var results = new Dictionary<string, ModDictionaryEntry>(StringComparer.OrdinalIgnoreCase);

            if (_sourceRegistry != null)
            {
                foreach (var source in _sourceRegistry.GetEnabledSources())
                {
                    if (string.Equals(source.Id, Infrastructure.Mods.ModInfo.ModInfoSourceRegistry.CuratedSourceId, StringComparison.OrdinalIgnoreCase))
                    {
                        var jsonPath = _dataUpdateService.GetExternalFilePath("ModInfo", "RimSharp", DatabaseDictionaryFileName);
                        LoadFromJsonFile(jsonPath, results);
                    }
                    else if (source.Type == ModRuleSourceType.RepoFile && !string.IsNullOrWhiteSpace(source.Url))
                    {
                        // Resolve the same URL-derived path the sync writes to
                        // (e.g. steamDB.json), not a fixed db.json name.
                        var jsonPath = _dataUpdateService.GetModInfoSourceFilePath(source.Id, source.Url);
                        if (jsonPath == null) continue;
                        LoadFromJsonFile(jsonPath, results);
                    }
                    else if (source.Type == ModRuleSourceType.LocalFile && !string.IsNullOrWhiteSpace(source.LocalPath))
                    {
                        LoadFromJsonFile(source.LocalPath, results);
                    }
                }
            }
            else
            {
                var jsonPath = _dataUpdateService.GetExternalFilePath("ModInfo", "RimSharp", DatabaseDictionaryFileName);
                LoadFromJsonFile(jsonPath, results);
            }

            _logger.LogDebug($"Loaded {results.Count} entries across ModInfo database sources.", nameof(ModDictionaryService));
            return results;
        }

        private int LoadFromJsonFile(string jsonFilePath, Dictionary<string, ModDictionaryEntry> targetDictionary)
        {
            var dbDirectoryPath = Path.GetDirectoryName(jsonFilePath);
            int count = 0;

            try
            {
                if (dbDirectoryPath != null && !Directory.Exists(dbDirectoryPath))
                {
                    _logger.LogInfo($"Creating dictionary database directory: '{dbDirectoryPath}'", nameof(ModDictionaryService));
                    Directory.CreateDirectory(dbDirectoryPath);
                }
                if (!File.Exists(jsonFilePath))
                {
                    _logger.LogWarning($"Dictionary database file '{jsonFilePath}' not found. Creating with default content.", nameof(ModDictionaryService));
                    File.WriteAllText(jsonFilePath, "{\"mods\":{}}");
                    _logger.LogInfo($"Default dictionary file created at '{jsonFilePath}'.", nameof(ModDictionaryService));
                    return 0;
                }

                _logger.LogDebug($"Parsing dictionary database: '{jsonFilePath}'", nameof(ModDictionaryService));

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                // Parse from the stream (not ReadAllText) to keep transient
                // allocation reasonable for the ~48 MB community database.
                using var stream = File.OpenRead(jsonFilePath);
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    _logger.LogWarning($"Dictionary database '{jsonFilePath}' has a non-object root; skipping.", nameof(ModDictionaryService));
                    return 0;
                }

                if (TryGetPropertyIgnoreCase(root, "mods", out _))
                {
                    // Curated db.json shape: { "mods": { packageId: { steamId: details } } }
                    var jsonData = root.Deserialize<ModDictionaryJsonRoot>(options);
                    if (jsonData?.Mods != null)
                    {
                        foreach (var packageKvp in jsonData.Mods)
                        {
                            string packageIdRaw = packageKvp.Key;
                            var steamEntries = packageKvp.Value;

                            if (string.IsNullOrWhiteSpace(packageIdRaw) || steamEntries == null) continue;

                            string packageIdLower = packageIdRaw.ToLowerInvariant();

                            foreach (var steamKvp in steamEntries)
                            {
                                string steamIdRaw = steamKvp.Key;
                                var details = steamKvp.Value;

                                if (string.IsNullOrWhiteSpace(steamIdRaw) || details == null) continue;

                                string steamIdLower = steamIdRaw.ToLowerInvariant();
                                var entry = new ModDictionaryEntry
                                {
                                    PackageId = packageIdLower,
                                    SteamId = steamIdLower,
                                    Name = details.Name,
                                    Versions = details.Versions ?? new List<string>(),
                                    Authors = details.Authors,
                                    Published = details.Published
                                };

                                if (!targetDictionary.ContainsKey(steamIdLower))
                                {
                                    targetDictionary[steamIdLower] = entry;
                                    count++;
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Steam Workshop DB shape: { "version": <int>, "database": { "<publishedfileid>": { ... } } }
                    count = LoadSteamWorkshopDbEntries(root, targetDictionary);
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.LogException(jsonEx, $"Error parsing JSON file '{jsonFilePath}'. Check for invalid boolean values for 'published'.", nameof(ModDictionaryService));
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
                _logger.LogException(ex, $"Unexpected error loading dictionary from '{jsonFilePath}'.", nameof(ModDictionaryService));
            }

            return count;
        }

        /// <summary>
        /// Maps the workshop-ID-keyed Steam Workshop DB shape into the existing
        /// <see cref="ModDictionaryEntry"/> fields. The current snapshot nests
        /// the entries under a root-level <c>database</c> object; the legacy flat
        /// shape (root keyed by publishedfileid) is still accepted as a fallback.
        /// First-source-wins is kept: an entry already loaded from a
        /// higher-priority source is not replaced.
        /// </summary>
        private static int LoadSteamWorkshopDbEntries(JsonElement root, Dictionary<string, ModDictionaryEntry> targetDictionary)
        {
            int count = 0;

            // Unwrap the nested snapshot first; only fall back to treating the
            // root itself as the entry map when there is no 'database' property.
            bool skipVersion = true;
            JsonElement entries = root;
            if (TryGetPropertyIgnoreCase(root, "database", out var database) &&
                database.ValueKind == JsonValueKind.Object)
            {
                entries = database;
                skipVersion = false;
            }

            foreach (var property in entries.EnumerateObject())
            {
                if (skipVersion &&
                    string.Equals(property.Name, "version", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var entryElement = property.Value;
                string steamIdLower = property.Name.ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(steamIdLower))
                {
                    continue;
                }

                string packageId = string.Empty;
                if (TryGetPropertyIgnoreCase(entryElement, "packageId", out var pkgElement) &&
                    pkgElement.ValueKind == JsonValueKind.String)
                {
                    packageId = (pkgElement.GetString() ?? string.Empty).ToLowerInvariant();
                }

                string name = string.Empty;
                if (TryGetPropertyIgnoreCase(entryElement, "name", out var nameElement) &&
                    nameElement.ValueKind == JsonValueKind.String)
                {
                    name = nameElement.GetString() ?? string.Empty;
                }

                var versions = ReadStringOrArrayToList(entryElement, "gameVersions");
                string authors = ReadStringOrArrayJoined(entryElement, "authors");

                bool published = true;
                if (TryGetPropertyIgnoreCase(entryElement, "unpublished", out var unpublishedElement))
                {
                    if (unpublishedElement.ValueKind == JsonValueKind.True) published = false;
                    else if (unpublishedElement.ValueKind == JsonValueKind.False) published = true;
                }

                var entry = new ModDictionaryEntry
                {
                    PackageId = packageId,
                    SteamId = steamIdLower,
                    Name = name,
                    Versions = versions,
                    Authors = authors,
                    Published = published
                };

                if (!targetDictionary.ContainsKey(steamIdLower))
                {
                    targetDictionary[steamIdLower] = entry;
                    count++;
                }
            }
            return count;
        }

        private static List<string> ReadStringOrArrayToList(JsonElement obj, string name)
        {
            var result = new List<string>();
            if (!TryGetPropertyIgnoreCase(obj, name, out var value)) return result;

            if (value.ValueKind == JsonValueKind.String)
            {
                var s = value.GetString();
                if (!string.IsNullOrEmpty(s)) result.Add(s);
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        result.Add(item.GetString() ?? string.Empty);
                    }
                }
            }
            return result;
        }

        private static string ReadStringOrArrayJoined(JsonElement obj, string name)
        {
            if (!TryGetPropertyIgnoreCase(obj, name, out var value)) return string.Empty;

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
            if (value.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(item.GetString() ?? string.Empty);
                    }
                }
                return string.Join(", ", parts);
            }
            return string.Empty;
        }

        private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
        {
            foreach (var property in obj.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Matches the root structure of db.json: { "mods": { ... } }
        /// </summary>
        private class ModDictionaryJsonRoot
        {
            public Dictionary<string, Dictionary<string, ModDetails>>? Mods { get; set; }
        }

        /// <summary>
        /// Matches the details stored under each steamId in db.json
        /// </summary>
        private class ModDetails
        {
            public string Name { get; set; } = string.Empty;
            public List<string> Versions { get; set; } = new();
            public string Authors { get; set; } = string.Empty;
            public bool Published { get; set; }
        }
    }
}
