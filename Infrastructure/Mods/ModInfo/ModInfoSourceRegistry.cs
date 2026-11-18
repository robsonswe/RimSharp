#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.ModInfo
{
    /// <summary>
    /// The user-configurable, priority-ordered registry of ModInfo (dictionary)
    /// database sources. Persisted under the <see cref="ConfigKey"/> config key.
    /// </summary>
    public class ModInfoSourceRegistry
    {
        public const string ConfigKey = "modinfo_sources";

        /// <summary>
        /// Config key holding a JSON array of source ids in user-configured
        /// priority order. Absent/empty means "use registry definition order".
        /// </summary>
        public const string PriorityOrderConfigKey = "modinfo_source_order";

        public const string CuratedSourceId = "rimsharp-db";
        private const string CuratedSourceName = "RimSharpDB";

        /// <summary>Id of the pre-filled RimSort Steam Workshop Database source.</summary>
        public const string SteamWorkshopDbSourceId = "steamworkshopdb";

        /// <summary>Display name of the pre-filled Steam Workshop Database source.</summary>
        public const string SteamWorkshopDbSourceName = "Steam Workshop DB";

        /// <summary>Raw URL of the upstream community Steam Workshop database.</summary>
        public const string SteamWorkshopDbSourceUrl =
            "https://raw.githubusercontent.com/RimSort/Steam-Workshop-Database/main/steamDB.json";

        /// <summary>
        /// Config flag recording that the one-time Steam Workshop DB migration
        /// already ran, so a user removing the source later is never undone.
        /// </summary>
        public const string SteamWorkshopDbMigrationFlagKey = "modinfo_steamworkshopdb_migrated";

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IConfigService _configService;
        private List<ModRuleSourceRecord> _sources;

        public ModInfoSourceRegistry(IConfigService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _sources = new List<ModRuleSourceRecord>();
            Reload();
        }

        public IReadOnlyList<ModRuleSourceRecord> Sources => _sources;

        public IReadOnlyList<ModRuleSourceRecord> GetEnabledSources() =>
            _sources.Where(s => s.Enabled).ToList();

        /// <summary>
        /// Enabled sources in merge priority order: ids listed in
        /// <see cref="PriorityOrderConfigKey"/> come first (in that order),
        /// followed by any enabled source not listed, in registry definition
        /// order. With no configured order this is today's definition order.
        /// </summary>
        public IReadOnlyList<ModRuleSourceRecord> GetEnabledSourcesInPriorityOrder()
        {
            var enabled = _sources.Where(s => s.Enabled).ToList();
            var order = ParsePriorityOrder(_configService.GetConfigValue(PriorityOrderConfigKey));
            if (order.Count == 0) return enabled;

            var result = new List<ModRuleSourceRecord>(enabled.Count);
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in order)
            {
                var match = enabled.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
                if (match != null && added.Add(match.Id)) result.Add(match);
            }
            foreach (var source in enabled)
            {
                if (added.Add(source.Id)) result.Add(source);
            }
            return result;
        }

        /// <summary>
        /// Persists the given source ids as the user-configured priority order.
        /// </summary>
        public void SetPriorityOrder(IEnumerable<string> ids)
        {
            var order = (ids ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .ToList();
            var json = JsonSerializer.Serialize(order, SerializerOptions);
            _configService.SetConfigValue(PriorityOrderConfigKey, json);
            _configService.SaveConfig();
        }

        /// <summary>
        /// Clears the configured order so registry definition order applies again.
        /// </summary>
        public void ClearPriorityOrder()
        {
            _configService.SetConfigValue(PriorityOrderConfigKey, string.Empty);
            _configService.SaveConfig();
        }

        private static List<string> ParsePriorityOrder(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(raw, SerializerOptions);
                if (parsed == null) return new List<string>();

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var result = new List<string>();
                foreach (var id in parsed)
                {
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var trimmed = id.Trim();
                    if (seen.Add(trimmed)) result.Add(trimmed);
                }
                return result;
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        public void Reload()
        {
            var raw = _configService.GetConfigValue(ConfigKey);
            var parsed = TryParse(raw);
            if (parsed == null)
            {
                _sources = CreateDefaults();
                MarkSteamWorkshopDbMigrationDone();
                return;
            }

            // One-time migration: configs persisted before the Steam Workshop DB
            // became a default contain only the curated entry. Append the default
            // ONCE and record it, so a later user removal is never resurrected on
            // the next startup. Curated entries keep their persisted id
            // (rimsharp-db) untouched.
            if (!IsSteamWorkshopDbMigrationDone())
            {
                if (!parsed.Any(s => string.Equals(s.Id, SteamWorkshopDbSourceId, StringComparison.OrdinalIgnoreCase)))
                {
                    parsed.Add(CreateDefaults().First(s => s.Id == SteamWorkshopDbSourceId));
                }
                MarkSteamWorkshopDbMigrationDone();
            }
            _sources = parsed;
        }

        private bool IsSteamWorkshopDbMigrationDone() =>
            string.Equals(_configService.GetConfigValue(SteamWorkshopDbMigrationFlagKey), "true", StringComparison.OrdinalIgnoreCase);

        private void MarkSteamWorkshopDbMigrationDone()
        {
            _configService.SetConfigValue(SteamWorkshopDbMigrationFlagKey, "true");
            _configService.SaveConfig();
        }

        public void Save()
        {
            var json = JsonSerializer.Serialize(_sources, SerializerOptions);
            _configService.SetConfigValue(ConfigKey, json);
            _configService.SaveConfig();
        }

        public void ResetToDefaults()
        {
            _sources = CreateDefaults();
            ClearPriorityOrder();
            Save();
        }

        public void ReplaceAll(IEnumerable<ModRuleSourceRecord> sources)
        {
            var incoming = (sources ?? Enumerable.Empty<ModRuleSourceRecord>())
                .Where(s => s != null)
                .ToList();

            // The curated RimSharpDB source is built-in: it must always be present.
            if (!incoming.Any(s => string.Equals(s.Id, CuratedSourceId, StringComparison.OrdinalIgnoreCase)))
            {
                incoming.Insert(0, CreateDefaults().First(s => s.Id == CuratedSourceId));
            }
            else
            {
                var curated = incoming.First(s => string.Equals(s.Id, CuratedSourceId, StringComparison.OrdinalIgnoreCase));
                curated.Id = CuratedSourceId;
            }

            _sources = incoming;
        }

        public bool Add(ModRuleSourceRecord source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(source.Id)) return false;
            if (Find(source.Id) != null) return false;

            _sources.Add(source);
            return true;
        }

        public bool Remove(string id)
        {
            var existing = Find(id);
            if (existing == null) return false;
            if (existing.Locked) return false;
            return _sources.Remove(existing);
        }

        public bool MoveUp(string id)
        {
            var index = IndexOf(id);
            if (index <= 0) return false;
            (_sources[index - 1], _sources[index]) = (_sources[index], _sources[index - 1]);
            return true;
        }

        public bool MoveDown(string id)
        {
            var index = IndexOf(id);
            if (index < 0 || index >= _sources.Count - 1) return false;
            (_sources[index + 1], _sources[index]) = (_sources[index], _sources[index + 1]);
            return true;
        }

        public bool SetEnabled(string id, bool enabled)
        {
            var source = Find(id);
            if (source == null) return false;
            source.Enabled = enabled;
            return true;
        }

        public ModRuleSourceRecord? Find(string id) =>
            _sources.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The compiled defaults: the curated RimSharpDB (highest priority, its
        /// file is owned by the existing SHA-gated sync) followed by the public
        /// RimSort Steam Workshop Database.
        /// </summary>
        public static List<ModRuleSourceRecord> CreateDefaults() => new()
        {
            new ModRuleSourceRecord
            {
                Id = CuratedSourceId,
                Name = CuratedSourceName,
                Type = ModRuleSourceType.RepoFile,
                Url = null,
                Enabled = true,
                Bundled = false
            },
            new ModRuleSourceRecord
            {
                Id = SteamWorkshopDbSourceId,
                Name = SteamWorkshopDbSourceName,
                Type = ModRuleSourceType.RepoFile,
                Url = SteamWorkshopDbSourceUrl,
                Enabled = true,
                Bundled = false
            }
        };

        private int IndexOf(string id)
        {
            for (int i = 0; i < _sources.Count; i++)
            {
                if (string.Equals(_sources[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static List<ModRuleSourceRecord>? TryParse(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            try
            {
                var parsed = JsonSerializer.Deserialize<List<ModRuleSourceRecord>>(raw, SerializerOptions);
                if (parsed == null) return null;

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var result = new List<ModRuleSourceRecord>();
                foreach (var source in parsed)
                {
                    if (source == null || string.IsNullOrWhiteSpace(source.Id)) continue;
                    if (!seen.Add(source.Id)) continue;
                    if (string.IsNullOrWhiteSpace(source.Name)) source.Name = source.Id;
                    result.Add(source);
                }
                return result;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
