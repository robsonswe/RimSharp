#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.Replacements
{
    /// <summary>
    /// The user-configurable, priority-ordered registry of Mod Replacement sources
    /// (local mod folder, GitHub repo snapshot, and curated RimSharpDB).
    /// </summary>
    public class ModReplacementSourceRegistry
    {
        public const string ConfigKey = "replacements_sources";
        public const string CuratedSourceId = "rimsharp-db";
        public const string ModSourceId = "mod-use-this-instead";
        public const string ModFolderId = "3396308787";
        public const string CommunitySourceId = "community-replacements";
        public const string CommunitySourceUrl =
            "https://raw.githubusercontent.com/emipa606/UseThisInstead/main/replacements.json.gz";

        /// <summary>
        /// Id of the index-driven replacement source. Its dataset is defined by
        /// <see cref="IndexedRecordsSourceUrl"/> on the registry record; the
        /// loader/sync dispatch on <see cref="ModRuleSourceType.IndexedRecords"/>,
        /// never on this id or the URL.
        /// </summary>
        public const string IndexedRecordsSourceId = "community-index-replacements";
        public const string IndexedRecordsSourceUrl = "https://jumli.sysrqmagician.dev/";

        /// <summary>
        /// Config flag recording that the one-time IndexedRecords migration ran,
        /// so a user removing the source later is never undone on next startup.
        /// </summary>
        public const string IndexedRecordsMigrationFlagKey = "replacements_indexedrecords_migrated";

        private const string CuratedSourceName = "RimSharpDB";
        private const string ModSourceName = "Use This Instead (Mod)";
        private const string CommunitySourceName = "Use This Instead (GitHub)";
        private const string IndexedRecordsSourceName = "Community Index (Reasons)";

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IConfigService _configService;
        private List<ModRuleSourceRecord> _sources;

        public ModReplacementSourceRegistry(IConfigService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _sources = new List<ModRuleSourceRecord>();
            Reload();
        }

        public IReadOnlyList<ModRuleSourceRecord> Sources => _sources;

        public IReadOnlyList<ModRuleSourceRecord> GetEnabledSources() =>
            _sources.Where(s => s.Enabled).ToList();

        public void Reload()
        {
            var raw = _configService.GetConfigValue(ConfigKey);
            var parsed = TryParse(raw);
            if (parsed == null)
            {
                _sources = CreateDefaults();
                MarkIndexedRecordsMigrationDone();
                return;
            }

            // One-time migration: configs persisted before the index-driven
            // source became a default do not contain it. Append it ONCE and
            // record that, so a later user removal is never resurrected.
            if (!IsIndexedRecordsMigrationDone())
            {
                if (!parsed.Any(s => string.Equals(s.Id, IndexedRecordsSourceId, StringComparison.OrdinalIgnoreCase)))
                {
                    parsed.Add(CreateDefaults().First(s => s.Id == IndexedRecordsSourceId));
                }
                MarkIndexedRecordsMigrationDone();
            }
            _sources = parsed;
        }

        private bool IsIndexedRecordsMigrationDone() =>
            string.Equals(_configService.GetConfigValue(IndexedRecordsMigrationFlagKey), "true", StringComparison.OrdinalIgnoreCase);

        private void MarkIndexedRecordsMigrationDone()
        {
            _configService.SetConfigValue(IndexedRecordsMigrationFlagKey, "true");
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
            Save();
        }

        public void ReplaceAll(IEnumerable<ModRuleSourceRecord> sources)
        {
            var incoming = (sources ?? Enumerable.Empty<ModRuleSourceRecord>())
                .Where(s => s != null)
                .ToList();

            // Always ensure the curated RimSharpDB source is present
            if (!incoming.Any(s => string.Equals(s.Id, CuratedSourceId, StringComparison.OrdinalIgnoreCase)))
            {
                var existingCurated = _sources.FirstOrDefault(s => string.Equals(s.Id, CuratedSourceId, StringComparison.OrdinalIgnoreCase));
                incoming.Add(existingCurated ?? CreateDefaults().First(s => s.Id == CuratedSourceId));
            }
            else
            {
                var curated = incoming.First(s => string.Equals(s.Id, CuratedSourceId, StringComparison.OrdinalIgnoreCase));
                curated.Id = CuratedSourceId;
            }

            // Always preserve the mod source if it was omitted from the UI (e.g. because it wasn't installed)
            if (!incoming.Any(s => string.Equals(s.Id, ModSourceId, StringComparison.OrdinalIgnoreCase)))
            {
                var existingMod = _sources.FirstOrDefault(s => string.Equals(s.Id, ModSourceId, StringComparison.OrdinalIgnoreCase));
                incoming.Insert(0, existingMod ?? CreateDefaults().First(s => s.Id == ModSourceId));
            }
            else
            {
                var mod = incoming.First(s => string.Equals(s.Id, ModSourceId, StringComparison.OrdinalIgnoreCase));
                mod.Id = ModSourceId;
                mod.Type = ModRuleSourceType.Mod;
                mod.ModFolderId = ModFolderId;
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

        public static List<ModRuleSourceRecord> CreateDefaults() => new()
        {
            new ModRuleSourceRecord
            {
                Id = ModSourceId,
                Name = ModSourceName,
                Type = ModRuleSourceType.Mod,
                ModFolderId = ModFolderId,
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
            },
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
                Id = IndexedRecordsSourceId,
                Name = IndexedRecordsSourceName,
                Type = ModRuleSourceType.IndexedRecords,
                Url = IndexedRecordsSourceUrl,
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
                    if (string.Equals(source.Id, ModSourceId, StringComparison.OrdinalIgnoreCase))
                    {
                        source.Type = ModRuleSourceType.Mod;
                        source.ModFolderId = ModFolderId;
                    }
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
