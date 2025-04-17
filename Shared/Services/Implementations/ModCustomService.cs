using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Infrastructure.Logging; // Assuming logger is needed/available
using System.Threading; // Required for Monitor

namespace RimSharp.Shared.Services.Implementations
{
    /// <summary>
    /// Implementation of IModCustomService that manages user-defined custom mod information.
    /// Stores data in AppBasePath/Rules/Custom/Mods.json
    /// </summary>
    public class ModCustomService : IModCustomService
    {
        private readonly string _appBasePath;
        private readonly string _customFolderPath;
        private readonly string _modsJsonPath;
        private ModsCustomRoot _modsData;
        private bool _isInitialized = false;
        private readonly object _initLock = new object(); // Lock for initialization
        private readonly ILoggerService _logger;

        // Constructor takes appBasePath and optional logger
        public ModCustomService(string appBasePath, ILoggerService logger = null)
        {
            _appBasePath = appBasePath ?? throw new ArgumentNullException(nameof(appBasePath));
            // Updated path calculation
            _customFolderPath = Path.Combine(_appBasePath, "Rules", "Custom");
            _modsJsonPath = Path.Combine(_customFolderPath, "Mods.json");
            // Start with empty, case-insensitive dictionary
            _modsData = new ModsCustomRoot { Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase) };
            _logger = logger;
             _logger?.LogDebug($"ModCustomService initialized. Custom file path: '{_modsJsonPath}'", nameof(ModCustomService));
        }

        // Ensures initialization is complete, creating directory/file if needed.
        private async Task EnsureInitializedAsync()
        {
             if (_isInitialized) return;

             bool acquiredLock = false;
             try
             {
                 Monitor.Enter(_initLock, ref acquiredLock);

                 if (_isInitialized) return; // Double-check after acquiring lock

                 _logger?.LogInfo("Initializing ModCustomService...", nameof(ModCustomService));

                 // 1. Ensure Custom directory exists
                 if (!Directory.Exists(_customFolderPath))
                 {
                     _logger?.LogInfo($"Creating custom folder at: {_customFolderPath}", nameof(ModCustomService));
                     Directory.CreateDirectory(_customFolderPath);
                 }

                 // 2. Ensure _modsData is initialized correctly (should be by constructor, but safety check)
                 if (_modsData == null || _modsData.Mods == null || _modsData.Mods.Comparer != StringComparer.OrdinalIgnoreCase)
                 {
                     _logger?.LogWarning("ModsData was null or had wrong comparer during init, re-initializing.", nameof(ModCustomService));
                     _modsData = new ModsCustomRoot { Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase) };
                 }

                 // 3. Check if Mods.json exists, create with default if not, otherwise load
                 if (!File.Exists(_modsJsonPath))
                 {
                     _logger?.LogInfo($"Mods.json not found at '{_modsJsonPath}'. Creating with default content.", nameof(ModCustomService));
                     // Write the specified default content
                     await File.WriteAllTextAsync(_modsJsonPath, "{\"Mods\":{}}");
                     _logger?.LogInfo("Default Mods.json created.", nameof(ModCustomService));
                     // Keep the _modsData as the empty initialized dictionary
                 }
                 else
                 {
                     _logger?.LogDebug($"Loading existing Mods.json from '{_modsJsonPath}'.", nameof(ModCustomService));
                     await LoadModsDataAsync(); // Load existing file
                 }

                 _isInitialized = true;
                 _logger?.LogInfo($"ModCustomService initialized. Loaded/found custom data for {_modsData.Mods.Count} mods.", nameof(ModCustomService));
             }
             catch (IOException ioEx)
             {
                 _logger?.LogException(ioEx, $"IO error during ModCustomService initialization (Path: '{_customFolderPath}'). Using empty data.", nameof(ModCustomService));
                 _modsData = new ModsCustomRoot { Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase) }; // Reset to safe default
                 _isInitialized = true; // Mark as initialized even on error to prevent retry loops
             }
             catch (UnauthorizedAccessException uaEx)
             {
                 _logger?.LogException(uaEx, $"Permission error during ModCustomService initialization (Path: '{_customFolderPath}'). Using empty data.", nameof(ModCustomService));
                 _modsData = new ModsCustomRoot { Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase) }; // Reset to safe default
                 _isInitialized = true;
             }
             catch (Exception ex)
             {
                 _logger?.LogException(ex, "Unexpected error initializing ModCustomService. Using empty data.", nameof(ModCustomService));
                 _modsData = new ModsCustomRoot { Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase) }; // Reset to safe default
                 _isInitialized = true;
             }
             finally
             {
                 if (acquiredLock) Monitor.Exit(_initLock);
             }
        }


        /// <summary>
        /// Loads the mods data from the JSON file. Assumes file exists.
        /// </summary>
        private async Task LoadModsDataAsync()
        {
            try
            {
                string json = await File.ReadAllTextAsync(_modsJsonPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // For reading "Mods" or "mods"
                };

                var loadedData = JsonSerializer.Deserialize<ModsCustomRoot>(json, options);

                // Ensure Mods property is initialized and uses case-insensitive comparer
                if (loadedData?.Mods != null)
                {
                    // Replace existing dictionary with loaded data, ensuring case-insensitivity
                    _modsData.Mods = new Dictionary<string, ModCustomInfo>(loadedData.Mods, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    // File exists but is empty or malformed, use empty dictionary
                     _logger?.LogWarning($"Mods.json at '{_modsJsonPath}' was empty or lacked 'Mods' property. Initializing with empty custom data.", nameof(ModCustomService));
                    _modsData.Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase);
                }

                _logger?.LogDebug($"Loaded Mods.json with {_modsData.Mods.Count} custom mods", nameof(ModCustomService));
            }
            catch (JsonException ex)
            {
                 _logger?.LogException(ex, $"Error parsing Mods.json: {ex.Message}. Initializing with empty custom data.", nameof(ModCustomService));
                 _modsData = new ModsCustomRoot { Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase) };
            }
            catch (Exception ex) // Catch potential IO exceptions during read as well
            {
                _logger?.LogException(ex, $"Error loading Mods.json from '{_modsJsonPath}'. Initializing with empty custom data.", nameof(ModCustomService));
                _modsData = new ModsCustomRoot { Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase) };
            }
        }

        /// <summary>
        /// Saves the current mods data to the JSON file. Ensures directory exists.
        /// </summary>
        private async Task SaveModsDataAsync()
        {
            try
            {
                 // 1. Ensure the target directory exists before writing
                 if (!Directory.Exists(_customFolderPath))
                 {
                      _logger?.LogInfo($"Creating custom directory for saving: '{_customFolderPath}'", nameof(ModCustomService));
                      Directory.CreateDirectory(_customFolderPath);
                 }

                // 2. Prepare serialization options
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                };

                // 3. Ensure _modsData and _modsData.Mods are not null (shouldn't happen with init logic, but safety first)
                if (_modsData == null) _modsData = new ModsCustomRoot();
                if (_modsData.Mods == null) _modsData.Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase);

                // 4. Serialize and write
                string json = JsonSerializer.Serialize(_modsData, options);
                await File.WriteAllTextAsync(_modsJsonPath, json);
                _logger?.LogDebug($"Saved Mods.json with {_modsData.Mods.Count} custom mods to '{_modsJsonPath}'", nameof(ModCustomService));
            }
            catch (IOException ioEx)
            {
                 _logger?.LogException(ioEx, $"IO error saving Mods.json to '{_modsJsonPath}'", nameof(ModCustomService));
                 throw; // Rethrow to indicate save failure
            }
             catch (UnauthorizedAccessException uaEx)
            {
                 _logger?.LogException(uaEx, $"Permission error saving Mods.json to '{_modsJsonPath}'", nameof(ModCustomService));
                 throw; // Rethrow to indicate save failure
            }
            catch (Exception ex)
            {
                _logger?.LogException(ex, $"Unexpected error saving Mods.json to '{_modsJsonPath}'", nameof(ModCustomService));
                throw; // Rethrow so the calling operation knows saving failed
            }
        }

        /// <inheritdoc />
        public Dictionary<string, ModCustomInfo> GetAllCustomMods()
        {
             // Block and wait for initialization if called synchronously
             EnsureInitializedAsync().ConfigureAwait(false).GetAwaiter().GetResult();
             // Return a copy to prevent external modification of the cache
             return new Dictionary<string, ModCustomInfo>(_modsData.Mods, StringComparer.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public ModCustomInfo GetCustomModInfo(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return null;

            // Block and wait for initialization if called synchronously
            EnsureInitializedAsync().ConfigureAwait(false).GetAwaiter().GetResult();

            // Use case-insensitive lookup
            if (_modsData.Mods.TryGetValue(packageId, out var info)) // No ToLowerInvariant needed due to comparer
            {
                return info;
            }

            return null; // Not found
        }

        /// <inheritdoc />
        public async Task SaveCustomModInfoAsync(string packageId, ModCustomInfo customInfo)
        {
            if (string.IsNullOrEmpty(packageId))
                throw new ArgumentNullException(nameof(packageId));

            if (customInfo == null)
                throw new ArgumentNullException(nameof(customInfo));

            await EnsureInitializedAsync(); // Ensure loaded/initialized before modifying

            string normalizedId = packageId.ToLowerInvariant(); // Keep normalization for the key itself

            CleanupEmptyCollections(customInfo);

            // Update or add the custom mod info (using normalized ID)
            _modsData.Mods[normalizedId] = customInfo;

            // Save changes to file (SaveModsDataAsync now handles directory creation)
            await SaveModsDataAsync();

            _logger?.LogInfo($"Saved custom info for mod: {packageId}", nameof(ModCustomService));
        }

        /// <inheritdoc />
        public async Task RemoveCustomModInfoAsync(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
                throw new ArgumentNullException(nameof(packageId));

            await EnsureInitializedAsync(); // Ensure loaded/initialized before modifying

            string normalizedId = packageId.ToLowerInvariant(); // Keep normalization for the key
            bool removed = _modsData.Mods.Remove(normalizedId);

            if (removed)
            {
                await SaveModsDataAsync(); // Save the change
                 _logger?.LogInfo($"Removed custom info for mod: {packageId}", nameof(ModCustomService));
            }
            else
            {
                 _logger?.LogWarning($"No custom info found to remove for mod: {packageId}", nameof(ModCustomService));
            }
        }

        /// <inheritdoc />
        public void ApplyCustomInfoToMods(IEnumerable<ModItem> mods)
        {
            if (mods == null) return;

            try
            {
                 // Block and wait for initialization if called synchronously
                EnsureInitializedAsync().ConfigureAwait(false).GetAwaiter().GetResult();

                if (_modsData == null || _modsData.Mods == null || !_modsData.Mods.Any())
                {
                    _logger?.LogDebug("No custom mod data available to apply.", nameof(ModCustomService));
                    return;
                }

                int customAppliedCount = 0;
                int versionsAdded = 0;

                foreach (var mod in mods.Where(m => m != null && !string.IsNullOrEmpty(m.PackageId)))
                {
                    // Use GetCustomModInfo which handles init, normalization, and null check
                    var customInfo = GetCustomModInfo(mod.PackageId);
                    if (customInfo == null) continue;

                    ApplyCustomInfoToMod(mod, customInfo, ref versionsAdded);
                    customAppliedCount++;
                }

                // _logger?.LogDebug($"Applied custom info to {customAppliedCount} mods. Added {versionsAdded} custom version entries.", nameof(ModCustomService));
            }
            catch (Exception ex)
            {
                _logger?.LogException(ex, "Error in ApplyCustomInfoToMods", nameof(ModCustomService));
            }
        }

        // --- ApplyCustomInfoToMod remains the same ---
        /// <summary>
        /// Applies custom information to a specific mod item.
        /// </summary>
        private void ApplyCustomInfoToMod(ModItem mod, ModCustomInfo customInfo, ref int versionsAdded)
        {
            // Apply ExternalUrl
            if (!string.IsNullOrEmpty(customInfo.ExternalUrl))
            {
                mod.ExternalUrl = customInfo.ExternalUrl;
            }
            // Apply Tags
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

            // Apply SupportedVersions
            if (customInfo.SupportedVersions != null && customInfo.SupportedVersions.Count > 0)
            {
                 var existingVersions = mod.SupportedVersions
                                         .Select(v => v.Version)
                                         .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string customVersion in customInfo.SupportedVersions)
                {
                    if (string.IsNullOrWhiteSpace(customVersion)) continue;
                     if (existingVersions.Add(customVersion))
                     {
                         mod.SupportedVersions.Add(new VersionSupport(customVersion, VersionSource.Custom, unofficial: true));
                         versionsAdded++;
                     }
                }
            }

            // Apply LoadBefore
            if (customInfo.LoadBefore != null && customInfo.LoadBefore.Count > 0)
            {
                foreach (var target in customInfo.LoadBefore.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(target) && !mod.LoadBefore.Contains(target, StringComparer.OrdinalIgnoreCase))
                    {
                        mod.LoadBefore.Add(target);
                    }
                }
            }

            // Apply LoadAfter
            if (customInfo.LoadAfter != null && customInfo.LoadAfter.Count > 0)
            {
                foreach (var target in customInfo.LoadAfter.Keys)
                {
                     if (!string.IsNullOrWhiteSpace(target) && !mod.LoadAfter.Contains(target, StringComparer.OrdinalIgnoreCase))
                    {
                        mod.LoadAfter.Add(target);
                    }
                }
            }

            // Apply LoadBottom
            if (customInfo.LoadBottom != null && customInfo.LoadBottom.Value)
            {
                mod.LoadBottom = true;
            }

            // Apply IncompatibleWith
            if (customInfo.IncompatibleWith != null && customInfo.IncompatibleWith.Count > 0)
            {
                foreach (var target in customInfo.IncompatibleWith.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(target) && !mod.IncompatibleWith.Contains(target, StringComparer.OrdinalIgnoreCase))
                    {
                        mod.IncompatibleWith.Add(target);
                    }
                }
            }
        }


        // --- CleanupEmptyCollections remains the same ---
        /// <summary>
        /// Cleans up empty collections to avoid storing unnecessary null data in JSON.
        /// Sets collections to null if they are empty.
        /// </summary>
        private void CleanupEmptyCollections(ModCustomInfo customInfo)
        {
             if (customInfo.LoadBefore?.Count == 0) customInfo.LoadBefore = null;
             if (customInfo.LoadAfter?.Count == 0) customInfo.LoadAfter = null;
             if (customInfo.IncompatibleWith?.Count == 0) customInfo.IncompatibleWith = null;
             if (customInfo.SupportedVersions?.Count == 0) customInfo.SupportedVersions = null;
             if (customInfo.LoadBottom != null && !customInfo.LoadBottom.Value) customInfo.LoadBottom = null;

             customInfo.SupportedVersions?.RemoveAll(string.IsNullOrWhiteSpace);
             if (customInfo.SupportedVersions?.Count == 0) customInfo.SupportedVersions = null;

             Action<Dictionary<string, ModDependencyRule>> cleanupDepRule = dict => {
                 if (dict == null) return;
                 foreach(var rule in dict.Values) {
                    if (rule.Name?.Count == 0) rule.Name = null;
                    if (rule.Comment?.Count == 0) rule.Comment = null;
                 }
             };
             Action<Dictionary<string, ModIncompatibilityRule>> cleanupIncompRule = dict => {
                 if (dict == null) return;
                 foreach(var rule in dict.Values) {
                    if (rule.Name?.Count == 0) rule.Name = null;
                    if (rule.Comment?.Count == 0) rule.Comment = null;
                 }
             };
             cleanupDepRule(customInfo.LoadBefore);
             cleanupDepRule(customInfo.LoadAfter);
             cleanupIncompRule(customInfo.IncompatibleWith);
             if (customInfo.LoadBottom?.Comment?.Count == 0) customInfo.LoadBottom.Comment = null;
        }

        // Helper class to match the JSON structure {"Mods": {...}}
        // Note the capital "M" as requested for the default file content
        private class ModsCustomRoot
        {
            public Dictionary<string, ModCustomInfo> Mods { get; set; }
        }
    }
}
