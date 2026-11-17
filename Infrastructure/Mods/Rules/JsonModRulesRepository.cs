#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using RimSharp.Infrastructure.Data;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.Rules
{
    /// <summary>
    /// Composite rules repository over the priority-ordered rule source
    /// registry. Each enabled source contributes its own
    /// <c>{"rules": {packageId: ModRule}}</c> file; the effective set is the
    /// per-field merge of all of them (see <see cref="MergeInto"/>). The merged
    /// result is cached until <see cref="Invalidate"/> is called (registry
    /// change or source refresh).
    /// </summary>
    public class JsonModRulesRepository : IModRulesRepository
    {
        private const string RulesFileName = "rules.json";
        private const string CuratedCacheFolder = "RimSharp";

        private static readonly JsonSerializerOptions DeserializeOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IDataUpdateService _dataUpdateService;
        private readonly ModRuleSourceRegistry _registry;
        private readonly ILoggerService? _logger;
        private readonly object _loadLock = new object(); // Lock for thread safety during load

        private Dictionary<string, ModRule> _cachedRules =
            new(StringComparer.OrdinalIgnoreCase);
        private long? _effectiveTimestamp;
        private bool _rulesLoaded;

        /// <summary>
        /// Constructs the composite repository. A null <paramref name="registry"/>
        /// falls back to the compiled default sources (curated RimSharpDB +
        /// community rules), which keeps single-source consumers and tests
        /// working without a config file.
        /// </summary>
        public JsonModRulesRepository(
            IDataUpdateService dataUpdateService,
            ModRuleSourceRegistry? registry = null,
            ILoggerService? logger = null)
        {
            _dataUpdateService = dataUpdateService ?? throw new ArgumentNullException(nameof(dataUpdateService));
            _registry = registry ?? new ModRuleSourceRegistry(new NullConfigService());
            _logger = logger;
        }

        /// <summary>
        /// The newest upstream timestamp across the loaded sources (the rules
        /// files carry one), or null when no source provides one. Used by the
        /// settings UI as the "rules as of" freshness signal.
        /// </summary>
        public long? EffectiveTimestamp
        {
            get
            {
                GetAllRules(); // ensure the cache (and timestamp) is warm
                return _effectiveTimestamp;
            }
        }

        /// <summary>
        /// Drops the cached effective set so the next read re-resolves the
        /// registry and re-merges every source file. Called after a registry
        /// change or a source refresh (live re-merge).
        /// </summary>
        public void Invalidate()
        {
            lock (_loadLock)
            {
                _rulesLoaded = false;
                _cachedRules = new Dictionary<string, ModRule>(StringComparer.OrdinalIgnoreCase);
                _effectiveTimestamp = null;
            }
        }

        public Dictionary<string, ModRule> GetAllRules()
        {
            // Double-check locking for thread safety
            if (_rulesLoaded)
            {
                return _cachedRules;
            }

            lock (_loadLock)
            {
                if (_rulesLoaded)
                {
                    return _cachedRules;
                }

                _cachedRules = LoadAndMergeSources();
                _rulesLoaded = true; // Mark as loaded even on partial failure (avoids retry storms)
                return _cachedRules;
            }
        }

        public ModRule GetRulesForMod(string packageId)
        {
            var rules = GetAllRules(); // Ensures rules are loaded (uses caching)

            if (string.IsNullOrEmpty(packageId))
            {
                return new ModRule();
            }

            if (rules.TryGetValue(packageId, out var rule)) // No need to ToLowerInvariant() here
            {
                return rule;
            }

            return new ModRule();
        }

        /// <summary>
        /// Loads every enabled source in priority order and merges them into
        /// the effective rule set. Each source is isolated: a missing or
        /// broken file is logged and skipped, never fatal to the others.
        /// </summary>
        private Dictionary<string, ModRule> LoadAndMergeSources()
        {
            var merged = new Dictionary<string, ModRule>(StringComparer.OrdinalIgnoreCase);
            long maxTimestamp = 0;

            foreach (var source in _registry.GetEnabledSources())
            {
                string? path;
                try
                {
                    path = ResolveSourceFilePath(source);
                }
                catch (Exception ex)
                {
                    LogWarning($"Could not resolve the file path of rule source '{source.Id}': {ex.Message}");
                    continue;
                }

                if (string.IsNullOrEmpty(path))
                {
                    LogWarning($"Rule source '{source.Id}' has no resolvable file (missing URL/path); skipping.");
                    continue;
                }

                if (!File.Exists(path))
                {
                    LogWarning($"Rules file for source '{source.Id}' not found at '{path}'. Skipping this source.");
                    continue;
                }

                try
                {
                    var json = File.ReadAllText(path);
                    var (rules, timestamp) = ParseSourceFile(json);
                    if (timestamp > maxTimestamp)
                    {
                        maxTimestamp = timestamp;
                    }

                    MergeInto(merged, rules);
                }
                catch (JsonException jsonEx)
                {
                    LogWarning($"Error parsing rules file of source '{source.Id}' ('{path}'): {jsonEx.Message}. Skipping this source.");
                }
                catch (IOException ioEx)
                {
                    LogWarning($"IO error reading rules file of source '{source.Id}' ('{path}'): {ioEx.Message}. Skipping this source.");
                }
                catch (Exception ex)
                {
                    LogWarning($"Unexpected error loading rules of source '{source.Id}' ('{path}'): {ex.Message}. Skipping this source.");
                }
            }

            _effectiveTimestamp = maxTimestamp > 0 ? maxTimestamp : null;
            return merged;
        }

        /// <summary>
        /// Resolves the on-disk file of a source:
        /// <list type="bullet">
        /// <item>The curated RimSharpDB source reads the already-synced
        /// Data/Rules/RimSharp/rules.json (the SHA-gated sync owns it).</item>
        /// <item>repoFile sources read their cache at
        /// Data/Rules/<sourceId>/<file name from the URL>.</item>
        /// <item>localFile sources are read directly from the user's path.</item>
        /// </list>
        /// </summary>
        private string? ResolveSourceFilePath(ModRuleSourceRecord source)
        {
            if (source.Type == ModRuleSourceType.LocalFile)
            {
                return source.LocalPath;
            }

            // repoFile (and the curated source, which is never fetched here).
            var fileName = string.Equals(source.Id, ModRuleSourceRegistry.CuratedSourceId, StringComparison.OrdinalIgnoreCase)
                ? RulesFileName
                : GetFileNameFromUrl(source.Url);

            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            var folder = string.Equals(source.Id, ModRuleSourceRegistry.CuratedSourceId, StringComparison.OrdinalIgnoreCase)
                ? CuratedCacheFolder
                : source.Id;

            return _dataUpdateService.GetExternalFilePath("Rules", folder, fileName);
        }

        private static string? GetFileNameFromUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

            var fileName = Path.GetFileName(uri.AbsolutePath);
            return string.IsNullOrEmpty(fileName) ? null : Uri.UnescapeDataString(fileName);
        }

        /// <summary>
        /// Parses one source file. The upstream community database spells the
        /// incompatibility map "incompatibleWith" while the curated editor
        /// writes "incompatibilities"; both are accepted and normalized to
        /// <see cref="ModRule.Incompatibilities"/> before deserialization.
        /// </summary>
        private static (Dictionary<string, ModRule>? Rules, long Timestamp) ParseSourceFile(string json)
        {
            var normalized = NormalizeIncompatibilityKey(json);
            var root = JsonSerializer.Deserialize<RulesJsonRoot>(normalized, DeserializeOptions);
            return (root?.Rules, root?.Timestamp ?? 0);
        }

        /// <summary>
        /// Rewrites the upstream "incompatibleWith" alias to our
        /// "incompatibilities" property name (only when the canonical key is
        /// absent), so a single model binds both schemas. Files without the
        /// alias are returned untouched (fast path).
        /// </summary>
        private static string NormalizeIncompatibilityKey(string json)
        {
            if (json.IndexOf("incompatibleWith", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return json;
            }

            var root = JsonNode.Parse(json);
            if (root is not JsonObject rootObject || rootObject["rules"] is not JsonObject rulesObject)
            {
                return json;
            }

            foreach (var packageId in rulesObject.Select(entry => entry.Key).ToList())
            {
                if (rulesObject[packageId] is JsonObject ruleObject &&
                    ruleObject["incompatibleWith"] is JsonNode alias &&
                    ruleObject["incompatibilities"] is null)
                {
                    ruleObject["incompatibilities"] = alias.DeepClone();
                    ruleObject.Remove("incompatibleWith");
                }
            }

            return root.ToJsonString();
        }

        /// <summary>
        /// Merges one source's rules into the effective set. Sources are
        /// visited in priority order, so the first source carrying an entry
        /// claims it and lower-priority sources only fill the gaps:
        /// <list type="bullet">
        /// <item>loadBefore / loadAfter — target-key union; the per-target
        /// Name/Comment come from the highest-priority source carrying that
        /// target (already claimed entries are never overwritten).</item>
        /// <item>loadTop / loadBottom — OR (any source saying true wins).</item>
        /// <item>incompatibleWith — first-source-wins per target (an
        /// incompatibility claim is never softened by a lower-priority
        /// source). Hardness is honored when explicitly present and defaults
        /// to false (soft/advisory) for database-layer entries.</item>
        /// <item>supportedVersions — NOT merged: version suppression moved out
        /// of the rules schema into its own data source, so the rules layer
        /// keeps only the claiming (highest-priority) source's value.</item>
        /// </list>
        /// </summary>
        private static void MergeInto(Dictionary<string, ModRule> merged, Dictionary<string, ModRule>? incoming)
        {
            if (incoming == null || incoming.Count == 0)
            {
                return;
            }

            foreach (var entry in incoming)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                if (!merged.TryGetValue(entry.Key, out var existing))
                {
                    merged[entry.Key] = entry.Value;
                    continue;
                }

                MergeRule(existing, entry.Value);
            }
        }

        private static void MergeRule(ModRule existing, ModRule incoming)
        {
            // Explicit JSON nulls can wipe the property initializers.
            existing.LoadBefore ??= new Dictionary<string, ModDependencyRule>();
            existing.LoadAfter ??= new Dictionary<string, ModDependencyRule>();
            existing.Incompatibilities ??= new Dictionary<string, ModIncompatibilityRule>();

            MergeDependencies(existing.LoadBefore, incoming.LoadBefore);
            MergeDependencies(existing.LoadAfter, incoming.LoadAfter);

            existing.LoadTop = MergeFlag(existing.LoadTop, incoming.LoadTop);
            existing.LoadBottom = MergeFlag(existing.LoadBottom, incoming.LoadBottom);

            if (incoming.Incompatibilities != null && incoming.Incompatibilities.Count > 0)
            {
                existing.Incompatibilities ??= new Dictionary<string, ModIncompatibilityRule>();
                foreach (var incompatibility in incoming.Incompatibilities)
                {
                    if (string.IsNullOrWhiteSpace(incompatibility.Key)) continue;
                    // First-source-wins per target: an already claimed target is
                    // never overwritten by a lower-priority source.
                    if (!existing.Incompatibilities.ContainsKey(incompatibility.Key))
                    {
                        existing.Incompatibilities[incompatibility.Key] = incompatibility.Value;
                    }
                }
            }

            // supportedVersions is deliberately NOT merged: version suppression
            // left the rules schema (it now lives in its own data source), so
            // the claiming source's value is kept as-is.
        }

        private static void MergeDependencies(
            Dictionary<string, ModDependencyRule> existing,
            Dictionary<string, ModDependencyRule>? incoming)
        {
            if (incoming == null || incoming.Count == 0)
            {
                return;
            }

            foreach (var target in incoming)
            {
                if (string.IsNullOrWhiteSpace(target.Key)) continue;
                // Union: a target already claimed by a higher-priority source
                // keeps that source's Name/Comment.
                if (!existing.ContainsKey(target.Key))
                {
                    existing[target.Key] = target.Value;
                }
            }
        }

        private static LoadBottomRule? MergeFlag(LoadBottomRule? existing, LoadBottomRule? incoming)
        {
            if (existing == null) return incoming;
            if (incoming == null) return existing;

            return new LoadBottomRule
            {
                Value = existing.Value || incoming.Value,
                Comment = existing.Comment is { Count: > 0 } ? existing.Comment : incoming.Comment
            };
        }

        private void LogWarning(string message)
        {
            if (_logger != null)
            {
                _logger.LogWarning(message, nameof(JsonModRulesRepository));
            }
            else
            {
                Console.WriteLine($"[WARNING] {message}");
            }
        }

        private sealed class RulesJsonRoot
        {
            public Dictionary<string, ModRule>? Rules { get; set; }

            /// <summary>Upstream freshness marker (unix seconds); absent in curated files.</summary>
            public long Timestamp { get; set; }
        }

        /// <summary>
        /// Stand-in config used when no registry is injected: the compiled
        /// defaults are read-only in that mode, so persistence is a no-op.
        /// </summary>
        private sealed class NullConfigService : IConfigService
        {
            public string GetConfigValue(string key) => string.Empty;

            public void SetConfigValue(string key, string value)
            {
            }

            public void LoadConfig()
            {
            }

            public void SaveConfig()
            {
            }
        }
    }
}