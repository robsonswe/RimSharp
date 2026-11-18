#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.Versions
{
    /// <summary>
    /// The user-configurable, priority-ordered registry of NoVersionWarning sources
    /// (local mod folder and GitHub repository snapshot).
    /// </summary>
    public class NoVersionWarningSourceRegistry
    {
        public const string ConfigKey = "noversionwarning_sources";
        public const string ModSourceId = "mod-no-version-warning";
        public const string ModFolderId = "2599504692";
        public const string CommunitySourceId = "community-noversionwarning";
        public const string CommunitySourceUrl =
            "https://raw.githubusercontent.com/emipa606/NoVersionWarning/main";

        private const string ModSourceName = "No Version Warning (Mod)";
        private const string CommunitySourceName = "No Version Warning (GitHub)";

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IConfigService _configService;
        private List<ModRuleSourceRecord> _sources;

        public NoVersionWarningSourceRegistry(IConfigService configService)
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
