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
        /// <summary>
        /// Immutable-by-convention snapshot of all derived dictionary data. The
        /// dictionaries are fully populated before publication and never mutated
        /// afterwards, so a single volatile reference can be swapped atomically
        /// by <see cref="Invalidate"/> without ever exposing partially built state
        /// or a null window to concurrent readers.
        /// </summary>
        private sealed record CacheSnapshot(
            Dictionary<string, ModDictionaryEntry> BySteamId,
            Dictionary<string, List<ModDictionaryEntry>> ByPackageId,
            Dictionary<string, int> SourcePriority);

        private volatile CacheSnapshot? _snapshot;
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
                // A single atomic assignment drops the whole snapshot at once.
                _snapshot = null;
            }
        }

        public Dictionary<string, ModDictionaryEntry> GetAllEntries()
        {
            return GetSnapshot().BySteamId;
        }

        /// <summary>
        /// Returns the current snapshot, building it under <see cref="_lock"/> with
        /// double-checked locking when absent. The volatile field is read into a
        /// local exactly once per call and that local is returned, so a concurrent
        /// <see cref="Invalidate"/> can never turn the result null mid-operation.
        /// </summary>
        private CacheSnapshot GetSnapshot()
        {
            var snap = _snapshot;
            if (snap != null)
            {
                return snap;
            }

            lock (_lock)
            {
                snap = _snapshot;
                if (snap != null)
                {
                    return snap;
                }

                _logger.LogInfo("Initializing ModDictionaryService cache.", nameof(ModDictionaryService));
                snap = LoadEntries();
                _snapshot = snap;
                _logger.LogInfo($"ModDictionaryService cache initialized. Found {snap.BySteamId.Count} unique mod dictionary entries.", nameof(ModDictionaryService));
                return snap;
            }
        }

        public ModDictionaryEntry? GetEntryBySteamId(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId)) return null;

            var snap = GetSnapshot();
            // BySteamId uses StringComparer.OrdinalIgnoreCase, so no lowercasing needed.
            snap.BySteamId.TryGetValue(steamId, out var entry);
            return entry;
        }

        public ModDictionaryEntry? GetEntryByPackageId(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return null;

            var snap = GetSnapshot();
            if (!snap.ByPackageId.TryGetValue(packageId, out var candidates) ||
                candidates.Count == 0)
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
                .ThenBy(entry => GetSourcePriority(snap.SourcePriority, entry))
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

            var snap = GetSnapshot();
            if (snap.ByPackageId.TryGetValue(packageId, out var candidates))
            {
                // The index is keyed by steamId, so candidates are already deduped.
                return new List<ModDictionaryEntry>(candidates);
            }
            return new List<ModDictionaryEntry>();
        }

        private static int GetSourcePriority(
            Dictionary<string, int> sourcePriority,
            ModDictionaryEntry entry) =>
            sourcePriority.TryGetValue(entry.SourceId, out var priority) ? priority : int.MaxValue;

        private CacheSnapshot LoadEntries()
        {
            var results = new Dictionary<string, ModDictionaryEntry>(StringComparer.OrdinalIgnoreCase);
            var priority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int order = 0;

            if (_sourceRegistry != null)
            {
                foreach (var source in _sourceRegistry.GetEnabledSourcesInPriorityOrder())
                {
                    priority[source.Id] = order++;
                    LoadSource(source, results);
                }
            }
            else
            {
                priority[Infrastructure.Mods.ModInfo.ModInfoSourceRegistry.CuratedSourceId] = order++;
                var jsonPath = _dataUpdateService.GetExternalFilePath("ModInfo", "RimSharp", DatabaseDictionaryFileName);
                LoadFromJsonFile(jsonPath, results, Infrastructure.Mods.ModInfo.ModInfoSourceRegistry.CuratedSourceId);
            }

            var packageIndex = BuildPackageIndex(results);

            _logger.LogDebug($"Loaded {results.Count} entries across ModInfo database sources.", nameof(ModDictionaryService));
            return new CacheSnapshot(results, packageIndex, priority);
        }

        private void LoadSource(ModRuleSourceRecord source, Dictionary<string, ModDictionaryEntry> results)
        {
            if (string.Equals(source.Id, Infrastructure.Mods.ModInfo.ModInfoSourceRegistry.CuratedSourceId, StringComparison.OrdinalIgnoreCase))
            {
                var jsonPath = _dataUpdateService.GetExternalFilePath("ModInfo", "RimSharp", DatabaseDictionaryFileName);
                LoadFromJsonFile(jsonPath, results, source.Id);
            }
            else if (source.Type == ModRuleSourceType.RepoFile && !string.IsNullOrWhiteSpace(source.Url))
            {
                // Resolve the same URL-derived path the sync writes to
                // (e.g. steamDB.json), not a fixed db.json name.
                var jsonPath = _dataUpdateService.GetModInfoSourceFilePath(source.Id, source.Url);
                if (jsonPath == null) return;
                LoadFromJsonFile(jsonPath, results, source.Id);
            }
            else if (source.Type == ModRuleSourceType.LocalFile && !string.IsNullOrWhiteSpace(source.LocalPath))
            {
                LoadFromJsonFile(source.LocalPath, results, source.Id);
            }
        }

        private static Dictionary<string, List<ModDictionaryEntry>> BuildPackageIndex(
            Dictionary<string, ModDictionaryEntry> entries)
        {
            var index = new Dictionary<string, List<ModDictionaryEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries.Values)
            {
                if (string.IsNullOrWhiteSpace(entry.PackageId)) continue;
                if (!index.TryGetValue(entry.PackageId, out var list))
                {
                    list = new List<ModDictionaryEntry>();
                    index[entry.PackageId] = list;
                }
                list.Add(entry);
            }
            return index;
        }

        /// <summary>
        /// Merges an incoming entry into the one already owned by an earlier
        /// (higher-priority) source. Only empty fields are filled; the owner's
        /// values are never overwritten.
        /// </summary>
        private static void MergeEntry(
            Dictionary<string, ModDictionaryEntry> target,
            string steamIdLower,
            ModDictionaryEntry incoming)
        {
            if (!target.TryGetValue(steamIdLower, out var existing))
            {
                target[steamIdLower] = incoming;
                return;
            }

            if (string.IsNullOrWhiteSpace(existing.SteamName)) existing.SteamName = incoming.SteamName;
            if (string.IsNullOrWhiteSpace(existing.AboutName)) existing.AboutName = incoming.AboutName;
            if (string.IsNullOrWhiteSpace(existing.PackageId)) existing.PackageId = incoming.PackageId;
            if (string.IsNullOrWhiteSpace(existing.Authors)) existing.Authors = incoming.Authors;

            if (existing.Versions == null || existing.Versions.Count == 0)
            {
                existing.Versions = incoming.Versions ?? new List<string>();
            }
            else if (incoming.Versions != null)
            {
                foreach (var version in incoming.Versions)
                {
                    if (!string.IsNullOrEmpty(version) && !existing.Versions.Contains(version))
                    {
                        existing.Versions.Add(version);
                    }
                }
            }

            // Published is deliberately first-writer-wins. Corner case: steamDB's
            // absent `unpublished` defaults to Published=true, so when a lower-
            // priority source loaded first for a steamId it can mask an explicit
            // published=false from a source loaded later. We keep whichever value
            // the first source wrote rather than trying to reconcile the two.
        }

        private int LoadFromJsonFile(
            string jsonFilePath,
            Dictionary<string, ModDictionaryEntry> targetDictionary,
            string sourceId)
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
                                    // Curated `name` is workshop-title lineage (83%
                                    // match steamName vs 4% About name), so it maps
                                    // to SteamName; AboutName stays empty.
                                    SteamName = details.Name,
                                    AboutName = string.Empty,
                                    Versions = details.Versions ?? new List<string>(),
                                    Authors = details.Authors,
                                    Published = details.Published,
                                    SourceId = sourceId
                                };

                                if (!targetDictionary.ContainsKey(steamIdLower)) count++;
                                MergeEntry(targetDictionary, steamIdLower, entry);
                            }
                        }
                    }
                }
                else
                {
                    // Steam Workshop DB shape: { "version": <int>, "database": { "<publishedfileid>": { ... } } }
                    count = LoadSteamWorkshopDbEntries(root, targetDictionary, sourceId);
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
        /// Maps the workshop-ID-keyed Steam Workshop DB shape into the normalized
        /// <see cref="ModDictionaryEntry"/> fields. The current snapshot nests
        /// the entries under a root-level <c>database</c> object; the legacy flat
        /// shape (root keyed by publishedfileid) is still accepted as a fallback.
        /// Entries are merged field-by-field into whatever an earlier, higher-
        /// priority source already contributed for the same steamId.
        /// </summary>
        private static int LoadSteamWorkshopDbEntries(
            JsonElement root,
            Dictionary<string, ModDictionaryEntry> targetDictionary,
            string sourceId)
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

                var entry = ParseWorkshopEntry(entryElement, steamIdLower, sourceId);

                if (!targetDictionary.ContainsKey(steamIdLower)) count++;
                MergeEntry(targetDictionary, steamIdLower, entry);
            }
            return count;
        }

        /// <summary>
        /// Reads every recognized field of a Steam Workshop DB entry in a single
        /// pass over its JSON properties. Each field honors the first matching
        /// property (case-insensitive), preserving the previous per-field lookup
        /// behavior while avoiding six separate linear scans per entry.
        /// </summary>
        private static ModDictionaryEntry ParseWorkshopEntry(
            JsonElement entryElement,
            string steamIdLower,
            string sourceId)
        {
            string packageId = string.Empty;
            string steamName = string.Empty;
            string aboutName = string.Empty;
            var versions = new List<string>();
            string authors = string.Empty;
            bool published = true;

            bool packageIdSeen = false;
            bool steamNameSeen = false;
            bool aboutNameSeen = false;
            bool versionsSeen = false;
            bool authorsSeen = false;
            bool unpublishedSeen = false;

            foreach (var prop in entryElement.EnumerateObject())
            {
                if (!packageIdSeen && string.Equals(prop.Name, "packageId", StringComparison.OrdinalIgnoreCase))
                {
                    packageIdSeen = true;
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        packageId = (prop.Value.GetString() ?? string.Empty).ToLowerInvariant();
                    }
                }
                else if (!steamNameSeen && string.Equals(prop.Name, "steamName", StringComparison.OrdinalIgnoreCase))
                {
                    steamNameSeen = true;
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        steamName = prop.Value.GetString() ?? string.Empty;
                    }
                }
                else if (!aboutNameSeen && string.Equals(prop.Name, "name", StringComparison.OrdinalIgnoreCase))
                {
                    aboutNameSeen = true;
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        aboutName = prop.Value.GetString() ?? string.Empty;
                    }
                }
                else if (!versionsSeen && string.Equals(prop.Name, "gameVersions", StringComparison.OrdinalIgnoreCase))
                {
                    versionsSeen = true;
                    ReadStringOrArrayToList(prop.Value, versions);
                }
                else if (!authorsSeen && string.Equals(prop.Name, "authors", StringComparison.OrdinalIgnoreCase))
                {
                    authorsSeen = true;
                    authors = ReadStringOrArrayJoined(prop.Value);
                }
                else if (!unpublishedSeen && string.Equals(prop.Name, "unpublished", StringComparison.OrdinalIgnoreCase))
                {
                    unpublishedSeen = true;
                    if (prop.Value.ValueKind == JsonValueKind.True) published = false;
                    else if (prop.Value.ValueKind == JsonValueKind.False) published = true;
                }
            }

            return new ModDictionaryEntry
            {
                PackageId = packageId,
                SteamId = steamIdLower,
                SteamName = steamName,
                AboutName = aboutName,
                Versions = versions,
                Authors = authors,
                Published = published,
                SourceId = sourceId
            };
        }

        private static void ReadStringOrArrayToList(JsonElement value, List<string> result)
        {
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
        }

        private static string ReadStringOrArrayJoined(JsonElement value)
        {
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
