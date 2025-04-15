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

namespace RimSharp.Shared.Services.Implementations
{
    /// <summary>
    /// Implementation of IModCustomService that manages user-defined custom mod information.
    /// </summary>
    public class ModCustomService : IModCustomService
    {
        private readonly string _appBasePath;
        private readonly string _customFolderPath;
        private readonly string _modsJsonPath;
        private ModsCustomRoot _modsData;
        private bool _isInitialized = false;
        private readonly object _initLock = new object(); // Lock for initialization
        private readonly ILoggerService _logger; // Optional: Inject logger if available


        // Optional: Inject logger if registered and needed
        public ModCustomService(string appBasePath, ILoggerService logger = null)
        {
            _appBasePath = appBasePath ?? throw new ArgumentNullException(nameof(appBasePath));
            _customFolderPath = Path.Combine(_appBasePath, "Custom");
            _modsJsonPath = Path.Combine(_customFolderPath, "Mods.json");
            _modsData = new ModsCustomRoot(); // Start with empty
            _logger = logger; // Store logger
        }

        // Make EnsureInitializedAsync truly async and use locking
        private async Task EnsureInitializedAsync()
        {
             if (_isInitialized) return;

             // Use a lock to prevent race conditions during initialization
             bool acquiredLock = false;
             try
             {
                 // Simple locking mechanism; consider SemaphoreSlim for async-specific waits if needed
                 System.Threading.Monitor.Enter(_initLock, ref acquiredLock);

                 // Double-check after acquiring the lock
                 if (_isInitialized) return;

                 _logger?.LogInfo("Initializing ModCustomService...", nameof(ModCustomService));

                 // Ensure Custom directory exists
                 if (!Directory.Exists(_customFolderPath))
                 {
                     Directory.CreateDirectory(_customFolderPath);
                     _logger?.LogInfo($"Created custom folder at: {_customFolderPath}", nameof(ModCustomService));
                 }

                 // Ensure _modsData is initialized (should be by constructor, but double-check)
                 if (_modsData == null)
                 {
                     _modsData = new ModsCustomRoot { Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase) };
                 }
                 // Ensure the inner dictionary uses case-insensitive comparer
                 else if (_modsData.Mods == null || _modsData.Mods.Comparer != StringComparer.OrdinalIgnoreCase)
                 {
                     _modsData.Mods = new Dictionary<string, ModCustomInfo>(_modsData.Mods ?? new Dictionary<string, ModCustomInfo>(), StringComparer.OrdinalIgnoreCase);
                 }


                 // Check if Mods.json exists
                 if (!File.Exists(_modsJsonPath))
                 {
                     _logger?.LogInfo($"Mods.json not found at '{_modsJsonPath}'. Creating new file.", nameof(ModCustomService));
                     await SaveModsDataAsync(); // Save default empty structure
                 }
                 else
                 {
                     _logger?.LogDebug($"Loading existing Mods.json from '{_modsJsonPath}'.", nameof(ModCustomService));
                     await LoadModsDataAsync(); // Load existing file
                 }

                 _isInitialized = true;
                 _logger?.LogInfo($"ModCustomService initialized. Loaded custom data for {_modsData.Mods.Count} mods.", nameof(ModCustomService));
             }
             catch (Exception ex)
             {
                 _logger?.LogException(ex, "Error initializing ModCustomService.", nameof(ModCustomService));
                 // Ensure we have at least an empty collection (case-insensitive) to work with
                 if (_modsData == null || _modsData.Mods == null || _modsData.Mods.Comparer != StringComparer.OrdinalIgnoreCase)
                 {
                      _modsData = new ModsCustomRoot { Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase) };
                 }
                 // Allow application to continue, potentially without custom data
             }
             finally
             {
                 if (acquiredLock) System.Threading.Monitor.Exit(_initLock);
             }
        }


        /// <summary>
        /// Loads the mods data from the JSON file. Should be called within EnsureInitializedAsync.
        /// </summary>
        private async Task LoadModsDataAsync()
        {
            try
            {
                string json = await File.ReadAllTextAsync(_modsJsonPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // For reading properties like "mods" or "Mods"
                };

                var loadedData = JsonSerializer.Deserialize<ModsCustomRoot>(json, options);

                // Ensure Mods property is initialized and uses case-insensitive comparer
                if (loadedData?.Mods != null)
                {
                    _modsData.Mods = new Dictionary<string, ModCustomInfo>(loadedData.Mods, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    _modsData.Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase);
                }

                _logger?.LogDebug($"Loaded Mods.json with {_modsData.Mods.Count} custom mods", nameof(ModCustomService));
            }
            catch (JsonException ex)
            {
                 _logger?.LogException(ex, $"Error parsing Mods.json: {ex.Message}. Initializing with empty custom data.", nameof(ModCustomService));
                 _modsData = new ModsCustomRoot { Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase) };
            }
            catch (Exception ex)
            {
                _logger?.LogException(ex, "Error loading Mods.json. Initializing with empty custom data.", nameof(ModCustomService));
                _modsData = new ModsCustomRoot { Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase) };
                // Potentially rethrow if loading is critical and failure should halt something
            }
        }

        /// <summary>
        /// Saves the current mods data to the JSON file. Should be called within EnsureInitializedAsync or after modifications.
        /// </summary>
        private async Task SaveModsDataAsync()
        {
            try
            {
                 // Ensure the target directory exists before writing
                 Directory.CreateDirectory(_customFolderPath);

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    // Optional: Ignore null values to keep JSON cleaner
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                };

                // Ensure _modsData and _modsData.Mods are not null before serialization
                if (_modsData == null) _modsData = new ModsCustomRoot();
                if (_modsData.Mods == null) _modsData.Mods = new Dictionary<string, ModCustomInfo>(StringComparer.OrdinalIgnoreCase);

                string json = JsonSerializer.Serialize(_modsData, options);
                await File.WriteAllTextAsync(_modsJsonPath, json);
                _logger?.LogDebug($"Saved Mods.json with {_modsData.Mods.Count} custom mods", nameof(ModCustomService));
            }
            catch (Exception ex)
            {
                _logger?.LogException(ex, "Error saving Mods.json", nameof(ModCustomService));
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
            if (string.IsNullOrEmpty(packageId)) return null; // Allow null return

            // Block and wait for initialization if called synchronously
            EnsureInitializedAsync().ConfigureAwait(false).GetAwaiter().GetResult();

            // Use lowercase for lookup
            if (_modsData.Mods.TryGetValue(packageId.ToLowerInvariant(), out var info))
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

            await EnsureInitializedAsync(); // Ensure loaded before modifying

            string normalizedId = packageId.ToLowerInvariant();

            // Clean up potentially empty collections before saving
            CleanupEmptyCollections(customInfo);

            // Update or add the custom mod info (using normalized ID)
            _modsData.Mods[normalizedId] = customInfo;

            // Save changes to file
            await SaveModsDataAsync();

            _logger?.LogInfo($"Saved custom info for mod: {packageId}", nameof(ModCustomService));
        }

        /// <inheritdoc />
        public async Task RemoveCustomModInfoAsync(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
                throw new ArgumentNullException(nameof(packageId));

            await EnsureInitializedAsync(); // Ensure loaded before modifying

            string normalizedId = packageId.ToLowerInvariant();
            bool removed = _modsData.Mods.Remove(normalizedId);

            if (removed)
            {
                await SaveModsDataAsync();
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
            if (mods == null) return; // Allow empty list

            try
            {
                 // Block and wait for initialization if called synchronously
                EnsureInitializedAsync().ConfigureAwait(false).GetAwaiter().GetResult();

                if (_modsData == null || _modsData.Mods == null || !_modsData.Mods.Any())
                {
                    _logger?.LogDebug("No custom mod data available to apply.", nameof(ModCustomService));
                    return; // Exit early
                }

                int customAppliedCount = 0;
                int versionsAdded = 0;

                foreach (var mod in mods)
                {
                    if (mod == null || string.IsNullOrEmpty(mod.PackageId)) continue;

                    // Skip core/expansion? Optional, custom info might be useful even for them.
                    // if (mod.ModType == ModType.Core || mod.ModType == ModType.Expansion) continue;

                    // Use GetCustomModInfo which handles normalization and null check
                    var customInfo = GetCustomModInfo(mod.PackageId); // Already ensures initialized
                    if (customInfo == null) continue;

                    // Apply custom information to the mod
                    ApplyCustomInfoToMod(mod, customInfo, ref versionsAdded);
                    customAppliedCount++;
                }

                //_logger?.LogDebug($"Applied custom info to {customAppliedCount} mods. Added {versionsAdded} custom version entries.", nameof(ModCustomService));
            }
            catch (Exception ex)
            {
                _logger?.LogException(ex, "Error in ApplyCustomInfoToMods", nameof(ModCustomService));
                // Continue without applying custom info if there's an error
            }
        }

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
                 // Append or replace? Let's append unique tags for now.
                 var existingTags = mod.TagList; // Uses the parsed list
                 var customTags = customInfo.Tags.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                                .Select(t => t.Trim())
                                                .Where(t => !string.IsNullOrEmpty(t));
                 var combinedTags = existingTags.Union(customTags, StringComparer.OrdinalIgnoreCase).ToList();
                 mod.Tags = string.Join(", ", combinedTags);
                 mod.InvalidateTagListCache(); // Force TagList to be recalculated on next access
            }


            // Apply SupportedVersions
            if (customInfo.SupportedVersions != null && customInfo.SupportedVersions.Count > 0)
            {
                 // Use a HashSet for efficient duplicate checking (case-insensitive)
                 var existingVersions = mod.SupportedVersions
                                         .Select(v => v.Version)
                                         .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (string customVersion in customInfo.SupportedVersions)
                {
                    if (string.IsNullOrWhiteSpace(customVersion)) continue;

                     // Add custom version only if it doesn't already exist
                     if (existingVersions.Add(customVersion)) // Returns true if added
                     {
                         mod.SupportedVersions.Add(new VersionSupport(customVersion, VersionSource.Custom, unofficial: true)); // Mark as Custom source and unofficial
                         versionsAdded++;
                        // Debug.WriteLine($"[DEBUG] Added Custom support: {mod.PackageId} -> {customVersion}");
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
                        // Debug.WriteLine($"[DEBUG] Added Custom LoadBefore: {mod.PackageId} -> {target}");
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
                        // Debug.WriteLine($"[DEBUG] Added Custom LoadAfter: {mod.PackageId} -> {target}");
                    }
                }
            }

            // Apply LoadBottom (only if true in custom)
            // If LoadBottom is null or its Value is false in customInfo, it won't override existing true values.
            if (customInfo.LoadBottom != null && customInfo.LoadBottom.Value)
            {
                 // Debug.WriteLine($"[DEBUG] Applying Custom LoadBottom=true to mod {mod.PackageId}");
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
                        // Debug.WriteLine($"[DEBUG] Added Custom Incompatibility: {mod.PackageId} -> {target}");
                    }
                }
            }
        }


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

             // For LoadBottom, only keep it if Value is true. Set to null otherwise.
             if (customInfo.LoadBottom != null && !customInfo.LoadBottom.Value)
             {
                 customInfo.LoadBottom = null;
             }
             // Also remove null/empty strings if they somehow got added
             customInfo.SupportedVersions?.RemoveAll(string.IsNullOrWhiteSpace);
             customInfo.LoadBefore?.Remove(null); // Should not happen with Dict keys, but safe
             customInfo.LoadAfter?.Remove(null);
             customInfo.IncompatibleWith?.Remove(null);

             // Recurse/check inner lists/dictionaries if they could be empty? (e.g., rules Name/Comment lists)
             // Example for LoadBefore rules:
             if (customInfo.LoadBefore != null) {
                 foreach(var rule in customInfo.LoadBefore.Values) {
                    if (rule.Name?.Count == 0) rule.Name = null;
                    if (rule.Comment?.Count == 0) rule.Comment = null;
                 }
             }
             // ... repeat for LoadAfter, Incompatibilities ...
             if (customInfo.LoadAfter != null) {
                 foreach(var rule in customInfo.LoadAfter.Values) {
                    if (rule.Name?.Count == 0) rule.Name = null;
                    if (rule.Comment?.Count == 0) rule.Comment = null;
                 }
             }
             if (customInfo.IncompatibleWith != null) {
                 foreach(var rule in customInfo.IncompatibleWith.Values) {
                    if (rule.Name?.Count == 0) rule.Name = null;
                    if (rule.Comment?.Count == 0) rule.Comment = null;
                 }
             }
             if (customInfo.LoadBottom?.Comment?.Count == 0) customInfo.LoadBottom.Comment = null;

        }
    }
}
