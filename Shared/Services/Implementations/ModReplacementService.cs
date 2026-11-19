using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using RimSharp.Infrastructure.Logging; 
using RimSharp.Infrastructure.Data;
using RimSharp.Infrastructure.Mods.Replacements;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Shared.Services.Implementations
{
    public class ModReplacementService : IModReplacementService
    {
        private const string UseThisInsteadModFolderId = "3396308787";
        private const string UseThisInsteadReplacementsSubFolder = "Replacements";
        private const string DatabaseReplacementsFileName = "replacements.json";
        private readonly IDataUpdateService _dataUpdateService;
        private readonly IPathService _pathService;
        private readonly ILoggerService _logger;
        private readonly Infrastructure.Mods.Replacements.ModReplacementSourceRegistry? _sourceRegistry;
        private Dictionary<string, List<ModReplacementInfo>>? _replacementsCache;
        private bool _isInitialized = false;
        private readonly object _lock = new object();

        public ModReplacementService(
            IPathService pathService,
            IDataUpdateService dataUpdateService,
            ILoggerService logger,
            Infrastructure.Mods.Replacements.ModReplacementSourceRegistry? sourceRegistry = null)
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
                _replacementsCache = null;
                _isInitialized = false;
            }
        }

        public Dictionary<string, List<ModReplacementInfo>> GetAllReplacements()
        {
            if (_isInitialized && _replacementsCache != null)
            {
                return _replacementsCache;
            }

            lock (_lock)
            {
                if (_isInitialized && _replacementsCache != null)
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

        public ModReplacementInfo? GetReplacementBySteamId(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId)) return null;

            var cache = GetAllReplacements();
            string normalizedId = steamId.ToLowerInvariant();

            return cache.TryGetValue(normalizedId, out var suggestions)
                ? GetPrimary(suggestions)
                : null;
        }

        public ModReplacementInfo? GetReplacementByPackageId(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return null;

            var cache = GetAllReplacements();
            string normalizedId = packageId.ToLowerInvariant();

            foreach (var kvp in cache)
            {
                var first = kvp.Value.FirstOrDefault();
                if (first != null &&
                    string.IsNullOrEmpty(first.SteamId) &&
                    !string.IsNullOrEmpty(first.ModId) &&
                    first.ModId.Equals(normalizedId, StringComparison.OrdinalIgnoreCase))
                {
                    return GetPrimary(kvp.Value);
                }
            }

            return null;
        }

        public IReadOnlyList<ModReplacementInfo> GetReplacementsBySteamId(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId)) return Array.Empty<ModReplacementInfo>();

            var cache = GetAllReplacements();
            string normalizedId = steamId.ToLowerInvariant();

            return cache.TryGetValue(normalizedId, out var suggestions)
                ? suggestions
                : Array.Empty<ModReplacementInfo>();
        }

        public IReadOnlyList<ModReplacementInfo> GetReplacementsByPackageId(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return Array.Empty<ModReplacementInfo>();

            var cache = GetAllReplacements();
            string normalizedId = packageId.ToLowerInvariant();

            foreach (var kvp in cache)
            {
                var first = kvp.Value.FirstOrDefault();
                if (first != null &&
                    string.IsNullOrEmpty(first.SteamId) &&
                    !string.IsNullOrEmpty(first.ModId) &&
                    first.ModId.Equals(normalizedId, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            return Array.Empty<ModReplacementInfo>();
        }

        /// <summary>
        /// The primary suggestion is the first reason-carrying one (highest-priority
        /// reason among them), falling back to the highest-priority suggestion overall.
        /// The cache lists are already ordered by source priority.
        /// </summary>
        private static ModReplacementInfo? GetPrimary(IReadOnlyList<ModReplacementInfo>? suggestions)
        {
            if (suggestions == null || suggestions.Count == 0) return null;
            return suggestions.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Reason)) ?? suggestions[0];
        }

        /// <summary>
        /// Identity of the suggested target mod. Steam ids and package ids are
        /// compared case-insensitively; a rule with no target gets a unique id so
        /// it is never collapsed with another rule.
        /// </summary>
        private static string GetTargetIdentity(ModReplacementInfo info)
        {
            if (!string.IsNullOrWhiteSpace(info.ReplacementSteamId))
                return "s:" + info.ReplacementSteamId.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(info.ReplacementModId))
                return "p:" + info.ReplacementModId.Trim().ToLowerInvariant();
            return "u:" + Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// Adds a suggestion for a source mod. Suggestions that target different
        /// mods are all kept. When two suggestions target the same mod, the one
        /// carrying a reason wins; otherwise the earlier (higher-priority) source
        /// is kept.
        /// </summary>
        private static bool AddSuggestion(
            Dictionary<string, List<SuggestionEntry>> target,
            string key,
            ModReplacementInfo info,
            int priority)
        {
            if (!target.TryGetValue(key, out var list))
            {
                list = new List<SuggestionEntry>();
                target[key] = list;
            }

            var targetId = GetTargetIdentity(info);
            for (int i = 0; i < list.Count; i++)
            {
                if (!string.Equals(list[i].TargetId, targetId, StringComparison.Ordinal)) continue;

                bool existingHasReason = !string.IsNullOrWhiteSpace(list[i].Info.Reason);
                bool newHasReason = !string.IsNullOrWhiteSpace(info.Reason);
                if (newHasReason && !existingHasReason)
                {
                    list[i] = new SuggestionEntry(targetId, priority, info);
                    return true;
                }
                // Existing wins: it either carries the reason, or both/neither
                // carry one and it came from the higher-priority source.
                return false;
            }

            list.Add(new SuggestionEntry(targetId, priority, info));
            return true;
        }

        private static Dictionary<string, List<ModReplacementInfo>> Finalize(
            Dictionary<string, List<SuggestionEntry>> raw)
        {
            var result = new Dictionary<string, List<ModReplacementInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in raw)
            {
                result[kvp.Key] = kvp.Value
                    .OrderBy(e => e.Priority) // stable: preserves within-source order
                    .Select(e => e.Info)
                    .ToList();
            }
            return result;
        }

        private sealed class SuggestionEntry
        {
            public string TargetId { get; }
            public int Priority { get; }
            public ModReplacementInfo Info { get; }

            public SuggestionEntry(string targetId, int priority, ModReplacementInfo info)
            {
                TargetId = targetId;
                Priority = priority;
                Info = info;
            }
        }

        private Dictionary<string, List<ModReplacementInfo>> LoadReplacements()
        {
            var raw = new Dictionary<string, List<SuggestionEntry>>(StringComparer.OrdinalIgnoreCase);

            if (_sourceRegistry != null)
            {
                var sources = _sourceRegistry.GetEnabledSources();
                for (int priority = 0; priority < sources.Count; priority++)
                {
                    var source = sources[priority];
                    if (source.Type == ModRuleSourceType.Mod)
                    {
                        int count = LoadFromUseThisInsteadXml(raw, priority);
                        _logger.LogDebug($"Loaded {count} replacements from mod folder '{source.Name}'.", nameof(ModReplacementService));
                    }
                    else if (string.Equals(source.Id, Infrastructure.Mods.Replacements.ModReplacementSourceRegistry.CuratedSourceId, StringComparison.OrdinalIgnoreCase))
                    {
                        int count = LoadFromJson(raw, priority);
                        _logger.LogDebug($"Loaded {count} replacements from curated database (gaps only).", nameof(ModReplacementService));
                    }
                    else if (source.Type == ModRuleSourceType.RepoFile && !string.IsNullOrWhiteSpace(source.Url))
                    {
                        int count = LoadFromUseThisInsteadRepository(source.Id, raw, priority);
                        _logger.LogDebug($"Loaded {count} replacements from repository snapshot '{source.Name}'.", nameof(ModReplacementService));
                    }
                    else if (source.Type == ModRuleSourceType.LocalFile && !string.IsNullOrWhiteSpace(source.LocalPath))
                    {
                        int count = LoadFromLocalFile(source, raw, priority);
                        _logger.LogDebug($"Loaded {count} replacements from local file '{source.LocalPath}'.", nameof(ModReplacementService));
                    }
                    else if (source.Type == ModRuleSourceType.IndexedRecords)
                    {
                        int count = LoadFromIndexedRecords(source, raw, priority);
                        _logger.LogDebug($"Loaded {count} replacements from indexed source '{source.Name}'.", nameof(ModReplacementService));
                    }
                }
            }
            else
            {
                // Fallback default order: mod -> repo -> curated
                int utiCount = LoadFromUseThisInsteadXml(raw, 0);
                _logger.LogDebug($"Loaded {utiCount} replacements from 'Use This Instead' mod folder.", nameof(ModReplacementService));

                int repoCount = LoadFromUseThisInsteadRepository("UseThisInstead", raw, 1);
                _logger.LogDebug($"Loaded {repoCount} replacements from 'Use This Instead' repository snapshot.", nameof(ModReplacementService));

                int dbCount = LoadFromJson(raw, 2);
                _logger.LogDebug($"Loaded {dbCount} replacements from JSON database (gaps only).", nameof(ModReplacementService));
            }

            return Finalize(raw);
        }

        private int LoadFromLocalFile(ModRuleSourceRecord source, Dictionary<string, List<SuggestionEntry>> targetDictionary, int priority)
        {
            var localPath = source.LocalPath;
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath)) return 0;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(localPath));
                if (doc.RootElement.TryGetProperty("rules", out var rules))
                {
                    // A local file is its own source kind; carry the registry
                    // record's display name so rows can name the exact source.
                    return ParseReplacementsRules(rules, targetDictionary, ReplacementSource.LocalFile, priority, source.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to parse local replacement file '{localPath}': {ex.Message}", nameof(ModReplacementService));
            }
            return 0;
        }

        /// <summary>
        /// Loads replacement rules from an index-driven source that was synced
        /// locally by DataUpdateService. The index maps source ids to record ids;
        /// each cached record is parsed by <see cref="IndexedRecordsFormatAdapter"/>.
        /// Only curated UseAlternative notices produce rules, and each rule
        /// carries its reason and the registry source's display name.
        /// </summary>
        private int LoadFromIndexedRecords(ModRuleSourceRecord source, Dictionary<string, List<SuggestionEntry>> targetDictionary, int priority)
        {
            var indexPath = _dataUpdateService.GetExternalFilePath("Replacements", source.Id, "index.json");
            if (!File.Exists(indexPath)) return 0;

            int count = 0;
            try
            {
                using var indexDoc = JsonDocument.Parse(File.ReadAllText(indexPath));
                if (indexDoc.RootElement.ValueKind != JsonValueKind.Object) return 0;

                var sourceDir = Path.GetDirectoryName(indexPath)!;
                var seenRecordIds = new HashSet<int>();

                foreach (var prop in indexDoc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetInt32(out var recordId)) continue;
                    if (!seenRecordIds.Add(recordId)) continue; // one rule per record

                    var recordPath = Path.Combine(sourceDir, $"{recordId}.json");
                    if (!File.Exists(recordPath)) continue;

                    try
                    {
                        var record = IndexedRecordsFormatAdapter.ParseRecord(File.ReadAllText(recordPath));
                        var alt = record?.CuratedAlternative;
                        if (record == null || alt == null) continue;

                        var info = new ModReplacementInfo
                        {
                            ModId = (record.PackageId ?? string.Empty).Trim().ToLowerInvariant(),
                            SteamId = (record.WorkshopId ?? string.Empty).Trim(),
                            ReplacementName = alt.TargetName ?? string.Empty,
                            ReplacementSteamId = alt.TargetWorkshopId ?? string.Empty,
                            Reason = alt.Reason, // null stays an absent JSON property
                            Source = ReplacementSource.IndexedRecords,
                            SourceName = source.Name
                        };

                        if (string.IsNullOrEmpty(info.SteamId) && string.IsNullOrEmpty(info.ModId)) continue;

                        string key = !string.IsNullOrEmpty(info.SteamId)
                            ? info.SteamId.ToLowerInvariant()
                            : $"packageid_{info.ModId}";
                        if (AddSuggestion(targetDictionary, key, info, priority)) count++;
                    }
                    catch (JsonException)
                    {
                        // One malformed record does not spoil the batch.
                    }
                    catch (IOException)
                    {
                        // A record that vanished mid-read is simply skipped.
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to load indexed replacement source '{source.Id}': {ex.Message}", nameof(ModReplacementService));
            }
            return count;
        }

        private int LoadFromUseThisInsteadRepository(string sourceId, Dictionary<string, List<SuggestionEntry>> targetDictionary, int priority)
        {
            var snapshotPath = _dataUpdateService.GetExternalFilePath("Replacements", sourceId, "replacements.json");
            if (!File.Exists(snapshotPath) && (sourceId == "community-replacements" || sourceId == "UseThisInstead"))
            {
                var altPath = _dataUpdateService.GetExternalFilePath("Replacements", "UseThisInstead", "replacements.json");
                if (File.Exists(altPath)) snapshotPath = altPath;
            }

            if (!File.Exists(snapshotPath))
            {
                _logger.LogInfo($"'Use This Instead' repository snapshot not found at '{snapshotPath}'. Skipping (no successful data sync yet).", nameof(ModReplacementService));
                return 0;
            }

            try
            {
                _logger.LogDebug($"Parsing 'Use This Instead' repository snapshot: '{snapshotPath}'", nameof(ModReplacementService));
                using var doc = JsonDocument.Parse(File.ReadAllText(snapshotPath));
                if (!doc.RootElement.TryGetProperty("rules", out var rules))
                {
                    _logger.LogWarning($"Repository snapshot '{snapshotPath}' has no 'rules' array.", nameof(ModReplacementService));
                    return 0;
                }

                return ParseReplacementsRules(rules, targetDictionary, ReplacementSource.UseThisInsteadRepo, priority);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, $"Unexpected error loading 'Use This Instead' repository snapshot from '{snapshotPath}'.", nameof(ModReplacementService));
                return 0;
            }
        }

        /// <summary>
        /// Parses the upstream "rules[]" shape shared by the Use This Instead
        /// mod (mod-folder replacements.json) and the GitHub repository
        /// snapshot. Every rule is offered as a suggestion; rules targeting the
        /// same mod are resolved by <see cref="AddSuggestion"/>.
        /// </summary>
        private static int ParseReplacementsRules(
            JsonElement rules,
            Dictionary<string, List<SuggestionEntry>> targetDictionary,
            ReplacementSource source,
            int priority,
            string? sourceName = null)
        {
            int count = 0;

            foreach (var rule in rules.EnumerateArray())
            {
                // A single malformed rule must not discard the whole snapshot.
                try
                {
                    var info = new ModReplacementInfo
                    {
                        SteamId = GetString(rule, "oldWorkshopId"),
                        ReplacementSteamId = GetString(rule, "newWorkshopId"),
                        Author = GetString(rule, "oldAuthor"),
                        ReplacementAuthor = GetString(rule, "newAuthor"),
                        ModId = GetString(rule, "oldPackageId"),
                        ReplacementModId = GetString(rule, "newPackageId"),
                        ModName = GetString(rule, "oldName"),
                        ReplacementName = GetString(rule, "newName"),
                        Versions = rule.TryGetProperty("oldVersions", out var oldVersions) ? JoinVersions(oldVersions) : string.Empty,
                        ReplacementVersions = rule.TryGetProperty("newVersions", out var newVersions) ? JoinVersions(newVersions) : string.Empty,
                        Source = source,
                        SourceName = sourceName
                    };

                    string key = info.SteamId?.Trim().ToLowerInvariant()!;
                    if (string.IsNullOrEmpty(key))
                    {
                        key = $"packageid_{info.ModId?.Trim().ToLowerInvariant() ?? Guid.NewGuid().ToString()}";
                    }

                    if (AddSuggestion(targetDictionary, key, info, priority))
                    {
                        count++;
                    }
                }
                catch (Exception)
                {
                    // Skipped: one bad rule does not invalidate the snapshot.
                }
            }

            return count;
        }

        private static string GetString(JsonElement rule, string propertyName)
        {
            return rule.TryGetProperty(propertyName, out var value)
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        /// <summary>
        /// Loads replacement data from the curated database at
        /// Data/Replacements/RimSharp/replacements.json.
        /// Populates the provided dictionary. Ensures directory and file exist.
        /// </summary>
        /// <returns>The number of items successfully loaded from JSON.</returns>
        private int LoadFromJson(Dictionary<string, List<SuggestionEntry>> targetDictionary, int priority)
        {
            var jsonFilePath = _dataUpdateService.GetExternalFilePath("Replacements", "RimSharp", DatabaseReplacementsFileName);
            var dbDirectoryPath = Path.GetDirectoryName(jsonFilePath);
            int count = 0;

            try
            {
                if (dbDirectoryPath != null && !Directory.Exists(dbDirectoryPath))
                {
                    _logger.LogInfo($"Creating replacement database directory: '{dbDirectoryPath}'", nameof(ModReplacementService));
                    Directory.CreateDirectory(dbDirectoryPath);
                }
                if (!File.Exists(jsonFilePath))
                {
                    _logger.LogWarning($"Replacement database file '{jsonFilePath}' not found. Creating with default content.", nameof(ModReplacementService));
                    File.WriteAllText(jsonFilePath, "{\"mods\":{}}");
                    _logger.LogInfo($"Default replacement file created at '{jsonFilePath}'.", nameof(ModReplacementService));
                    return 0; 
                }

                _logger.LogDebug($"Parsing replacement database: '{jsonFilePath}'", nameof(ModReplacementService));
                string jsonContent = File.ReadAllText(jsonFilePath);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var jsonData = JsonSerializer.Deserialize<ReplacementJsonRoot>(jsonContent, options);

                if (jsonData?.Mods != null)
                {
                    foreach (var kvp in jsonData.Mods)
                    {
                        var info = kvp.Value;
                        if (info == null)
                        {
                            _logger.LogWarning($"Skipping null entry in '{jsonFilePath}' for key '{kvp.Key}'.", nameof(ModReplacementService));
                            continue;
                        }

                        string key = info.SteamId?.Trim().ToLowerInvariant()!;
                        if (string.IsNullOrEmpty(key))
                        {
                            key = $"packageid_{info.ModId?.Trim().ToLowerInvariant() ?? Guid.NewGuid().ToString()}";
                        }

                        info.ModId = info.ModId?.Trim().ToLowerInvariant() ?? string.Empty;
                        info.ReplacementModId = info.ReplacementModId?.Trim().ToLowerInvariant() ?? string.Empty;
                        info.Source = ReplacementSource.Database;

                        if (AddSuggestion(targetDictionary, key, info, priority))
                        {
                            count++;
                        }
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
        /// Populates the provided dictionary with every rule found.
        /// </summary>
        /// <returns>The number of new items successfully loaded from XMLs.</returns>
        private static string JoinVersions(System.Text.Json.JsonElement versionsElement)
        {
            if (versionsElement.ValueKind != System.Text.Json.JsonValueKind.Array) return string.Empty;
            var items = new List<string>();
            foreach (var v in versionsElement.EnumerateArray())
            {
                var s = v.GetString();
                if (!string.IsNullOrWhiteSpace(s)) items.Add(s.Trim());
            }
            return string.Join(",", items);
        }

        private int LoadFromUseThisInsteadXml(Dictionary<string, List<SuggestionEntry>> targetDictionary, int priority)
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
                // Fallback: some installations keep replacements.json directly in mod folder
                var jsonPath = Path.Combine(utiModPath, "replacements.json");
                if (File.Exists(jsonPath))
                {
                    try
                    {
                        var jsonText = File.ReadAllText(jsonPath);
                        using var doc = JsonDocument.Parse(jsonText);
                        if (doc.RootElement.TryGetProperty("rules", out var rules))
                        {
                            count = ParseReplacementsRules(rules, targetDictionary, ReplacementSource.UseThisInstead, priority);
                        }
                        _logger.LogInfo($"Loaded {count} replacements from '{jsonPath}'.", nameof(ModReplacementService));
                        return count;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to parse replacements JSON '{jsonPath}': {ex.Message}");
                    }
                }
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
                        XElement? root = doc.Root;

                        if (root == null || root.Name != "ModReplacement")
                        {
                            _logger.LogWarning($"Skipping file '{filePath}'. Invalid root element or missing.", nameof(ModReplacementService));
                            continue;
                        }

                        var info = new ModReplacementInfo
                        {
                            Author = root.Element("Author")?.Value?.Trim() ?? string.Empty,
                            ModId = root.Element("ModId")?.Value?.Trim().ToLowerInvariant() ?? string.Empty, 
                            ModName = root.Element("ModName")?.Value?.Trim() ?? string.Empty,
                            SteamId = root.Element("SteamId")?.Value?.Trim() ?? string.Empty,
                            Versions = root.Element("Versions")?.Value?.Trim() ?? string.Empty,
                            ReplacementAuthor = root.Element("ReplacementAuthor")?.Value?.Trim() ?? string.Empty,
                            ReplacementModId = root.Element("ReplacementModId")?.Value?.Trim().ToLowerInvariant() ?? string.Empty, 
                            ReplacementName = root.Element("ReplacementName")?.Value?.Trim() ?? string.Empty,
                            ReplacementSteamId = root.Element("ReplacementSteamId")?.Value?.Trim() ?? string.Empty,
                            ReplacementVersions = root.Element("ReplacementVersions")?.Value?.Trim() ?? string.Empty,
                            Source = ReplacementSource.UseThisInstead
                        };

                        string key = info.SteamId?.Trim().ToLowerInvariant()!;

                        if (string.IsNullOrEmpty(key))
                        {
                            key = $"packageid_{info.ModId ?? Guid.NewGuid().ToString()}";
                        }

                        if (AddSuggestion(targetDictionary, key, info, priority))
                        {
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
            catch (Exception ex)
            {
                _logger.LogException(ex, $"Unexpected error processing 'Use This Instead' replacements folder '{replacementsPath}'.", nameof(ModReplacementService));
            }

            return count;
        }

        private class ReplacementJsonRoot
        {
            public Dictionary<string, ModReplacementInfo>? Mods { get; set; }
        }
    }
}
