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
        public const string CuratedSourceId = "rimsharp-db";
        private const string CuratedSourceName = "RimSharpDB";

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

        public void Reload()
        {
            var raw = _configService.GetConfigValue(ConfigKey);
            var parsed = TryParse(raw);
            _sources = parsed ?? CreateDefaults();
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
