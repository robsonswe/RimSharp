#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.Rules
{
    /// <summary>
    /// The user-configurable, priority-ordered registry of rule database
    /// sources. Persisted as a compact JSON array under the
    /// <see cref="ConfigKey"/> config key; the compiled defaults are used when
    /// the key is absent or malformed. Array order IS the priority: the first
    /// entry wins per-packageId / per-target during the merge.
    /// </summary>
    public class ModRuleSourceRegistry
    {
        /// <summary>ConfigService key holding the serialized source array.</summary>
        public const string ConfigKey = "rules_db_sources";

        /// <summary>Id of the curated RimSharpDB source (owned by the SHA-gated sync).</summary>
        public const string CuratedSourceId = "rimsharp-db";

        /// <summary>Id of the pre-filled RimSort Community-Rules-Database source.</summary>
        public const string CommunitySourceId = "community-rules";

        /// <summary>Raw URL of the upstream community rules file.</summary>
        public const string CommunitySourceUrl =
            "https://raw.githubusercontent.com/RimSort/Community-Rules-Database/main/communityRules.json";

        private const string CuratedSourceName = "RimSharpDB";
        private const string CommunitySourceName = "RimSortDB";

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IConfigService _configService;
        private List<ModRuleSourceRecord> _sources;

        public ModRuleSourceRegistry(IConfigService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _sources = new List<ModRuleSourceRecord>();
            Reload();
        }

        /// <summary>The sources in priority order (first = highest priority).</summary>
        public IReadOnlyList<ModRuleSourceRecord> Sources => _sources;

        /// <summary>The enabled sources in priority order.</summary>
        public IReadOnlyList<ModRuleSourceRecord> GetEnabledSources() =>
            _sources.Where(s => s.Enabled).ToList();

        /// <summary>
        /// Re-reads the registry from the config key. A missing or malformed
        /// value falls back to the compiled defaults (never throws).
        /// </summary>
        public void Reload()
        {
            var raw = _configService.GetConfigValue(ConfigKey);
            var parsed = TryParse(raw);
            _sources = parsed ?? CreateDefaults();
        }

        /// <summary>
        /// Persists the current registry to the config key as a compact
        /// (single-line) JSON array.
        /// </summary>
        public void Save()
        {
            var json = JsonSerializer.Serialize(_sources, SerializerOptions);
            _configService.SetConfigValue(ConfigKey, json);
            _configService.SaveConfig();
        }

        /// <summary>Replaces the registry with the compiled defaults and persists them.</summary>
        public void ResetToDefaults()
        {
            _sources = CreateDefaults();
            Save();
        }

        /// <summary>
        /// Replaces the whole registry (used by the settings dialog to write
        /// back its edited snapshot in one step).
        /// </summary>
        public void ReplaceAll(IEnumerable<ModRuleSourceRecord> sources)
        {
            var incoming = (sources ?? Enumerable.Empty<ModRuleSourceRecord>())
                .Where(s => s != null)
                .ToList();

            // The curated RimSharpDB source is hardcoded: a settings snapshot
            // that omits it (or tries to unlock it) is corrected here so the
            // source can never disappear from the registry.
            if (!incoming.Any(s => string.Equals(s.Id, CuratedSourceId, StringComparison.OrdinalIgnoreCase)))
            {
                incoming.Insert(0, CreateDefaults().First(s => s.Id == CuratedSourceId));
            }
            else
            {
                var curated = incoming.First(s => string.Equals(s.Id, CuratedSourceId, StringComparison.OrdinalIgnoreCase));
                curated.Id = CuratedSourceId; // keep the canonical id
            }

            _sources = incoming;
        }

        /// <summary>Appends a source at the lowest priority. Returns false when the id already exists.</summary>
        public bool Add(ModRuleSourceRecord source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(source.Id)) return false;
            if (Find(source.Id) != null) return false;

            _sources.Add(source);
            return true;
        }

        /// <summary>Removes a source by id. Returns false when the id is unknown.</summary>
        public bool Remove(string id)
        {
            var existing = Find(id);
            if (existing == null) return false;
            // The curated RimSharpDB source is hardcoded: it can never be
            // removed by hand or by the settings UI, only enabled/disabled.
            if (existing.Locked) return false;
            return _sources.Remove(existing);
        }

        /// <summary>Moves a source one slot toward the front (higher priority).</summary>
        public bool MoveUp(string id)
        {
            var index = IndexOf(id);
            if (index <= 0) return false;
            (_sources[index - 1], _sources[index]) = (_sources[index], _sources[index - 1]);
            return true;
        }

        /// <summary>Moves a source one slot toward the back (lower priority).</summary>
        public bool MoveDown(string id)
        {
            var index = IndexOf(id);
            if (index < 0 || index >= _sources.Count - 1) return false;
            (_sources[index + 1], _sources[index]) = (_sources[index], _sources[index + 1]);
            return true;
        }

        /// <summary>Sets the enabled flag of a source. Returns false when the id is unknown.</summary>
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
        /// RimSort Community-Rules-Database.
        /// </summary>
        public static List<ModRuleSourceRecord> CreateDefaults() => new()
        {
            new ModRuleSourceRecord
            {
                Id = CuratedSourceId,
                Name = CuratedSourceName,
                Type = ModRuleSourceType.RepoFile,
                Url = null, // never fetched by the registry sync: the curated SHA-gate owns this file
                Enabled = true,
                Bundled = false
            },
            new ModRuleSourceRecord
            {
                Id = CommunitySourceId,
                Name = CommunitySourceName,
                Type = ModRuleSourceType.RepoFile,
                Url = CommunitySourceUrl,
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

                // Drop structurally invalid entries (no id) and de-duplicate ids
                // (first occurrence wins) so a hand-edited config cannot produce
                // an ambiguous registry.
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var result = new List<ModRuleSourceRecord>();
                foreach (var source in parsed)
                {
                    if (source == null || string.IsNullOrWhiteSpace(source.Id)) continue;
                    if (!seen.Add(source.Id)) continue;
                    if (string.IsNullOrWhiteSpace(source.Name)) source.Name = source.Id;
                    // Legacy display-name migration (renamed sources).
                    if (source.Name == "RimSharpDB (curated)") source.Name = "RimSharpDB";
                    if (source.Name == "Community Rules Database") source.Name = "RimSortDB";
                    result.Add(source);
                }
                return result;
            }
            catch (JsonException)
            {
                return null; // malformed key -> compiled defaults
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }
    }
}