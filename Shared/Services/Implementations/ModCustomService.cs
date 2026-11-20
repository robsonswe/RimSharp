using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Infrastructure.Logging;
using System.Threading;

namespace RimSharp.Shared.Services.Implementations
{
    public class ModCustomService : IModCustomService
    {
        private readonly string _appBasePath;
        private readonly string _customFolderPath;
        private readonly string _modsJsonPath;
        private ModsCustomRootInternal _modsData;
        private bool _isInitialized = false;
        private readonly object _lock = new object();
        private readonly ILoggerService? _logger;

        // Tracks exactly what THIS service last applied to each packageId's
        // ModItem. Most custom properties have no provenance on ModItem, so on
        // a re-apply we must strip our own previous contributions before adding
        // the new ones — otherwise a removed value lingers on the live (and
        // folder-scan-cached) ModItem until a full restart. Guarded by _lock.
        private readonly Dictionary<string, LastAppliedCustom> _lastApplied =
            new(StringComparer.OrdinalIgnoreCase);

        public ModCustomService(string appBasePath, ILoggerService? logger = null)
        {
            _appBasePath = appBasePath ?? throw new ArgumentNullException(nameof(appBasePath));
            _customFolderPath = Path.Combine(_appBasePath, "Data", "Custom");
            _modsJsonPath = Path.Combine(_customFolderPath, "Mods.json");
            _modsData = new ModsCustomRootInternal { Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase) };
            _logger = logger;
        }

        public void Initialize()
        {
            if (_isInitialized) return;

            lock (_lock)
            {
                if (_isInitialized) return;

                try
                {
                    if (!Directory.Exists(_customFolderPath))
                    {
                        Directory.CreateDirectory(_customFolderPath);
                    }

                    if (!File.Exists(_modsJsonPath))
                    {
                        File.WriteAllText(_modsJsonPath, "{\"Mods\":{}}");
                        _modsData = new ModsCustomRootInternal { Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase) };
                    }
                    else
                    {
                        LoadModsDataSync();
                    }

                    _isInitialized = true;
                }
                catch (Exception ex)
                {
                    _logger?.LogException(ex, "Error initializing ModCustomService.", nameof(ModCustomService));
                    _modsData = new ModsCustomRootInternal { Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase) };
                    _isInitialized = true;
                }
            }
        }

        private void LoadModsDataSync()
        {
            try
            {
                string json = File.ReadAllText(_modsJsonPath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var loadedData = JsonSerializer.Deserialize<ModsCustomRootInternal>(json, options);

                if (loadedData?.Mods != null)
                {
                    _modsData.Mods = new Dictionary<string, ModCustomInfo>(loadedData.Mods, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    _modsData.Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogException(ex, "Error loading Mods.json sync", nameof(ModCustomService));
                _modsData = new ModsCustomRootInternal { Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase) };
            }
        }

        private async Task SaveModsDataAsync()
        {
            string json;
            lock (_lock)
            {
                var options = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
                json = JsonSerializer.Serialize(_modsData, options);
            }
            
            if (!Directory.Exists(_customFolderPath)) Directory.CreateDirectory(_customFolderPath);
            await File.WriteAllTextAsync(_modsJsonPath, json);
        }

        public Dictionary<string, ModCustomInfo> GetAllCustomMods()
        {
            Initialize();
            lock (_lock)
            {
                return new Dictionary<string, ModCustomInfo>(_modsData.Mods ?? new Dictionary<string, ModCustomInfo>(), StringComparer.OrdinalIgnoreCase);
            }
        }

        public ModCustomInfo? GetCustomModInfo(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return null;
            Initialize();
            lock (_lock)
            {
                if (_modsData.Mods != null && _modsData.Mods.TryGetValue(packageId, out var info))
                {
                    return info;
                }
            }
            return null;
        }

        public async Task SaveCustomModInfoAsync(string packageId, ModCustomInfo customInfo)
        {
            if (string.IsNullOrEmpty(packageId)) throw new ArgumentNullException(nameof(packageId));
            if (customInfo == null) throw new ArgumentNullException(nameof(customInfo));

            Initialize();
            CleanupEmptyCollections(customInfo);

            lock (_lock)
            {
                _modsData.Mods![packageId.ToLowerInvariant()] = customInfo;
            }

            await SaveModsDataAsync();
        }

        public async Task RemoveCustomModInfoAsync(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) throw new ArgumentNullException(nameof(packageId));
            Initialize();

            bool removed;
            LastAppliedCustom? prev = null;
            lock (_lock)
            {
                removed = _modsData.Mods!.Remove(packageId.ToLowerInvariant());
                if (removed)
                    _lastApplied.TryGetValue(packageId, out prev);
            }

            if (removed)
            {
                await SaveModsDataAsync();

                // Strip the values from the live ModItem we last applied them to
                // so direct callers that never re-apply (IModCustomService
                // consumers outside ModService) don't leave stale custom values
                // on screen. The load-path strip in ApplyCustomInfoToMods stays
                // as a second layer for folder-scan-cached instances.
                if (prev != null)
                {
                    lock (_lock)
                    {
                        if (prev.AppliedToInstanceRef != null &&
                            prev.AppliedToInstanceRef.TryGetTarget(out var liveMod))
                        {
                            // StripCustomInfoFromMod's InstanceId guard still applies.
                            StripCustomInfoFromMod(liveMod, prev);
                        }
                        _lastApplied.Remove(packageId);
                    }
                }
            }
        }

        public void ApplyCustomInfoToMods(IEnumerable<ModItem> mods)
        {
            if (mods == null) return;
            Initialize();

            var modsList = mods.Where(m => m != null && !string.IsNullOrEmpty(m.PackageId)).ToList();
            if (!modsList.Any()) return;

            lock (_lock)
            {
                int versionsAdded = 0;
                foreach (var mod in modsList)
                {
                    if (_modsData.Mods != null && _modsData.Mods.TryGetValue(mod.PackageId, out var customInfo))
                    {
                        // ApplyCustomInfoToMod strips the previous pass first.
                        ApplyCustomInfoToMod(mod, customInfo, ref versionsAdded);
                    }
                    else if (_lastApplied.TryGetValue(mod.PackageId, out var prev))
                    {
                        // The custom record was removed entirely (or this load
                        // has no record for the mod). Strip what we previously
                        // applied so a folder-scan-cached ModItem does not keep
                        // stale values. This is the path used by
                        // ModService.RemoveCustomModInfoAsync -> LoadModsAsync.
                        StripCustomInfoFromMod(mod, prev);
                        _lastApplied.Remove(mod.PackageId);
                    }
                }
            }
        }

        private void ApplyCustomInfoToMod(ModItem mod, ModCustomInfo customInfo, ref int versionsAdded)
        {
            // Strip exactly what this service applied on the previous pass so a
            // removed/changed value takes effect on the live ModItem immediately.
            if (_lastApplied.TryGetValue(mod.PackageId, out var prev))
                StripCustomInfoFromMod(mod, prev);

            var current = new LastAppliedCustom
            {
                AppliedToInstance = mod.InstanceId,
                AppliedToInstanceRef = new WeakReference<ModItem>(mod)
            };

            mod.IsFavorite = customInfo.Favorite ?? false;
            current.AppliedFavorite = mod.IsFavorite;

            if (!string.IsNullOrEmpty(customInfo.ExternalUrl))
            {
                mod.ExternalUrl = customInfo.ExternalUrl;
                current.AppliedExternalUrl = customInfo.ExternalUrl;
            }

            if (!string.IsNullOrEmpty(customInfo.Tags))
            {
                var existingTags = mod.TagList;
                var customTags = customInfo.Tags.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                               .Select(t => t.Trim())
                                               .Where(t => !string.IsNullOrEmpty(t))
                                               .ToList();
                // Union semantics: keep the first occurrence's casing and only
                // record tags that were genuinely new (so we never strip a
                // base/Steam tag that merely matched a custom one).
                var knownTags = new HashSet<string>(existingTags, StringComparer.OrdinalIgnoreCase);
                var addedTags = customTags.Where(t => knownTags.Add(t)).ToList();
                var combinedTags = existingTags.Concat(addedTags).ToList();
                mod.Tags = string.Join(", ", combinedTags);
                mod.InvalidateTagListCache();
                current.AddedTags.AddRange(addedTags);
            }

            if (customInfo.SupportedVersions != null)
            {
                var existingVersions = mod.SupportedVersions.Select(v => v.Version).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string customVersion in customInfo.SupportedVersions)
                {
                    if (!string.IsNullOrWhiteSpace(customVersion) && existingVersions.Add(customVersion))
                    {
                        mod.SupportedVersions.Add(new VersionSupport(customVersion, VersionSource.Custom, unofficial: true));
                        current.CustomVersions.Add(customVersion);
                        versionsAdded++;
                    }
                }
            }

            if (customInfo.LoadBefore != null)
            {
                foreach (var target in customInfo.LoadBefore.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(target) && !mod.LoadBefore.Contains(target, StringComparer.OrdinalIgnoreCase))
                    {
                        mod.LoadBefore.Add(target);
                        current.AddedLoadBefore.Add(target);
                    }
                }
            }

            if (customInfo.LoadAfter != null)
            {
                foreach (var target in customInfo.LoadAfter.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(target) && !mod.LoadAfter.Contains(target, StringComparer.OrdinalIgnoreCase))
                    {
                        mod.LoadAfter.Add(target);
                        current.AddedLoadAfter.Add(target);
                    }
                }
            }

            if (customInfo.LoadBottom?.Value == true)
            {
                // Remember the pre-custom value: ModRulesService also writes
                // this flag (before the custom layer in the load cycle), so a
                // custom-only re-apply must restore the rule's value rather
                // than blindly forcing false.
                current.PreviousLoadBottom = mod.LoadBottom;
                current.SetLoadBottom = true;
                mod.LoadBottom = true;
            }

            if (customInfo.LoadTop?.Value == true)
            {
                current.PreviousLoadTop = mod.LoadTop;
                current.SetLoadTop = true;
                mod.LoadTop = true;
            }

            if (customInfo.IncompatibleWith != null)
            {
                foreach (var entry in customInfo.IncompatibleWith)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Key) && !mod.IncompatibleWith.ContainsKey(entry.Key))
                    {
                        mod.IncompatibleWith.Add(entry.Key, entry.Value);
                        current.AddedIncompatibilities.Add(entry.Key);
                    }
                }
            }

            _lastApplied[mod.PackageId] = current;
        }

        /// <summary>
        /// Removes exactly the values recorded by the previous application.
        /// Only touches values this service actually added to THIS ModItem
        /// instance; a fresh instance parsed after a folder-scan cache miss
        /// carries no custom contributions and must keep its base values.
        /// </summary>
        private static void StripCustomInfoFromMod(ModItem mod, LastAppliedCustom prev)
        {
            if (prev.AppliedToInstance != mod.InstanceId) return;

            if (prev.CustomVersions.Count > 0)
            {
                mod.SupportedVersions.RemoveAll(v =>
                    v.Source == VersionSource.Custom &&
                    prev.CustomVersions.Contains(v.Version, StringComparer.OrdinalIgnoreCase));
            }

            if (prev.AddedTags.Count > 0)
            {
                var remainingTags = mod.TagList;
                remainingTags.RemoveAll(t => prev.AddedTags.Contains(t, StringComparer.OrdinalIgnoreCase));
                mod.Tags = string.Join(", ", remainingTags);
                mod.InvalidateTagListCache();
            }

            if (prev.AddedLoadBefore.Count > 0)
                mod.LoadBefore.RemoveAll(x => prev.AddedLoadBefore.Contains(x, StringComparer.OrdinalIgnoreCase));

            if (prev.AddedLoadAfter.Count > 0)
                mod.LoadAfter.RemoveAll(x => prev.AddedLoadAfter.Contains(x, StringComparer.OrdinalIgnoreCase));

            foreach (var key in prev.AddedIncompatibilities)
                mod.IncompatibleWith.Remove(key);

            if (prev.SetLoadBottom)
                mod.LoadBottom = prev.PreviousLoadBottom;

            if (prev.SetLoadTop)
                mod.LoadTop = prev.PreviousLoadTop;

            if (prev.AppliedExternalUrl != null &&
                string.Equals(mod.ExternalUrl, prev.AppliedExternalUrl, StringComparison.Ordinal))
            {
                mod.ExternalUrl = string.Empty;
            }

            if (prev.AppliedFavorite)
                mod.IsFavorite = false;
        }

        /// <summary>
        /// The exact values the custom layer added to one ModItem on its last
        /// application. Kept per packageId so a strip/re-apply cycle can be
        /// precise without adding provenance fields to the ModItem model.
        /// </summary>
        private sealed class LastAppliedCustom
        {
            public Guid AppliedToInstance { get; set; }

            /// <summary>
            /// Weak handle to the exact ModItem instance the values were applied
            /// to, so a direct RemoveCustomModInfoAsync call can strip them from
            /// the live object without waiting for a subsequent re-apply. Weak
            /// so a discarded/cached-away ModItem is never kept alive.
            /// </summary>
            public WeakReference<ModItem>? AppliedToInstanceRef { get; set; }

            public List<string> CustomVersions { get; } = new();
            public List<string> AddedTags { get; } = new();
            public List<string> AddedLoadBefore { get; } = new();
            public List<string> AddedLoadAfter { get; } = new();
            public bool SetLoadBottom { get; set; }
            public bool PreviousLoadBottom { get; set; }
            public bool SetLoadTop { get; set; }
            public bool PreviousLoadTop { get; set; }
            public List<string> AddedIncompatibilities { get; } = new();
            public string? AppliedExternalUrl { get; set; }
            public bool AppliedFavorite { get; set; }
        }

        private void CleanupEmptyCollections(ModCustomInfo customInfo)
        {
            if (customInfo.LoadBefore?.Count == 0) customInfo.LoadBefore = null!;
            if (customInfo.LoadAfter?.Count == 0) customInfo.LoadAfter = null!;
            if (customInfo.IncompatibleWith?.Count == 0) customInfo.IncompatibleWith = null!;
            if (customInfo.SupportedVersions?.Count == 0) customInfo.SupportedVersions = null!;
            if (customInfo.LoadBottom != null && customInfo.LoadBottom.Value == false) customInfo.LoadBottom = null!;
            if (customInfo.LoadTop != null && customInfo.LoadTop.Value == false) customInfo.LoadTop = null!;
            if (customInfo.Favorite == false) customInfo.Favorite = null!;

            customInfo.SupportedVersions?.RemoveAll(string.IsNullOrWhiteSpace);
            if (customInfo.SupportedVersions?.Count == 0) customInfo.SupportedVersions = null!;

            Action<Dictionary<string, ModDependencyRule>?> cleanupDepRule = dict =>
            {
                if (dict == null) return;
                foreach (var rule in dict.Values)
                {
                    if (rule.Name?.Count == 0) rule.Name = null!;
                    if (rule.Comment?.Count == 0) rule.Comment = null!;
                }
            };
            Action<Dictionary<string, ModIncompatibilityRule>?> cleanupIncompRule = dict =>
            {
                if (dict == null) return;
                foreach (var rule in dict.Values)
                {
                    if (rule.Name?.Count == 0) rule.Name = null!;
                    if (rule.Comment?.Count == 0) rule.Comment = null!;
                }
            };
            cleanupDepRule(customInfo.LoadBefore);
            cleanupDepRule(customInfo.LoadAfter);
            cleanupIncompRule(customInfo.IncompatibleWith);
            if (customInfo.LoadBottom != null && customInfo.LoadBottom.Comment?.Count == 0) customInfo.LoadBottom.Comment = null!;
            if (customInfo.LoadTop != null && customInfo.LoadTop.Comment?.Count == 0) customInfo.LoadTop.Comment = null!;
        }

        private class ModsCustomRootInternal
        {
            public Dictionary<string, ModCustomInfo>? Mods { get; set; }
        }
    }
}
