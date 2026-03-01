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

        public ModCustomService(string appBasePath, ILoggerService? logger = null)
        {
            _appBasePath = appBasePath ?? throw new ArgumentNullException(nameof(appBasePath));
            _customFolderPath = Path.Combine(_appBasePath, "Rules", "Custom");
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
            lock (_lock)
            {
                removed = _modsData.Mods!.Remove(packageId.ToLowerInvariant());
            }

            if (removed)
            {
                await SaveModsDataAsync();
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
                if (_modsData.Mods == null || !_modsData.Mods.Any()) return;

                int versionsAdded = 0;
                foreach (var mod in modsList)
                {
                    if (_modsData.Mods.TryGetValue(mod.PackageId, out var customInfo))
                    {
                        ApplyCustomInfoToMod(mod, customInfo, ref versionsAdded);
                    }
                }
            }
        }

        private void ApplyCustomInfoToMod(ModItem mod, ModCustomInfo customInfo, ref int versionsAdded)
        {
            mod.IsFavorite = customInfo.Favorite ?? false;
            if (!string.IsNullOrEmpty(customInfo.ExternalUrl)) mod.ExternalUrl = customInfo.ExternalUrl;
            
            if (!string.IsNullOrEmpty(customInfo.Tags))
            {
                var existingTags = mod.TagList;
                var customTags = customInfo.Tags.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                               .Select(t => t.Trim())
                                               .Where(t => !string.IsNullOrEmpty(t));
                var combinedTags = existingTags.Union(customTags, StringComparer.OrdinalIgnoreCase).ToList();
                mod.Tags = string.Join(", ", combinedTags);
                mod.InvalidateTagListCache();
            }

            if (customInfo.SupportedVersions != null)
            {
                var existingVersions = mod.SupportedVersions.Select(v => v.Version).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string customVersion in customInfo.SupportedVersions)
                {
                    if (!string.IsNullOrWhiteSpace(customVersion) && existingVersions.Add(customVersion))
                    {
                        mod.SupportedVersions.Add(new VersionSupport(customVersion, VersionSource.Custom, unofficial: true));
                        versionsAdded++;
                    }
                }
            }

            if (customInfo.LoadBefore != null)
            {
                foreach (var target in customInfo.LoadBefore.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(target) && !mod.LoadBefore.Contains(target, StringComparer.OrdinalIgnoreCase))
                        mod.LoadBefore.Add(target);
                }
            }

            if (customInfo.LoadAfter != null)
            {
                foreach (var target in customInfo.LoadAfter.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(target) && !mod.LoadAfter.Contains(target, StringComparer.OrdinalIgnoreCase))
                        mod.LoadAfter.Add(target);
                }
            }

            if (customInfo.LoadBottom?.Value == true) mod.LoadBottom = true;

            if (customInfo.IncompatibleWith != null)
            {
                foreach (var entry in customInfo.IncompatibleWith)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Key) && !mod.IncompatibleWith.ContainsKey(entry.Key))
                        mod.IncompatibleWith.Add(entry.Key, entry.Value);
                }
            }
        }

        private void CleanupEmptyCollections(ModCustomInfo customInfo)
        {
            if (customInfo.LoadBefore?.Count == 0) customInfo.LoadBefore = null!;
            if (customInfo.LoadAfter?.Count == 0) customInfo.LoadAfter = null!;
            if (customInfo.IncompatibleWith?.Count == 0) customInfo.IncompatibleWith = null!;
            if (customInfo.SupportedVersions?.Count == 0) customInfo.SupportedVersions = null!;
            if (customInfo.LoadBottom != null && customInfo.LoadBottom.Value == false) customInfo.LoadBottom = null!;
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
        }

        private class ModsCustomRootInternal
        {
            public Dictionary<string, ModCustomInfo>? Mods { get; set; }
        }
    }
}
