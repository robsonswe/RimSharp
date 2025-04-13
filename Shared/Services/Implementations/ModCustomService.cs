using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

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

        /// <summary>
        /// Initializes a new instance of the ModCustomService class.
        /// </summary>
        /// <param name="appBasePath">The base path of the application.</param>
        public ModCustomService(string appBasePath)
        {
            _appBasePath = appBasePath ?? throw new ArgumentNullException(nameof(appBasePath));
            _customFolderPath = Path.Combine(_appBasePath, "Custom");
            _modsJsonPath = Path.Combine(_customFolderPath, "Mods.json");
            _modsData = new ModsCustomRoot();
        }

        /// <summary>
        /// Ensures the custom folder and mods file exist and are properly initialized.
        /// </summary>
        private async Task EnsureInitializedAsync()
        {
            if (_isInitialized)
                return;

            try
            {
                // Ensure Custom directory exists
                if (!Directory.Exists(_customFolderPath))
                {
                    Directory.CreateDirectory(_customFolderPath);
                    Debug.WriteLine($"Created custom folder at: {_customFolderPath}");
                }

                // Ensure _modsData is initialized
                if (_modsData == null)
                {
                    _modsData = new ModsCustomRoot { Mods = new Dictionary<string, ModCustomInfo>() };
                }

                // Check if Mods.json exists
                if (!File.Exists(_modsJsonPath))
                {
                    // Create new file with default content
                    await SaveModsDataAsync();
                    Debug.WriteLine($"Created new Mods.json file at: {_modsJsonPath}");
                }
                else
                {
                    // Load existing file
                    await LoadModsDataAsync();
                }

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing ModCustomService: {ex.Message}");
                // Ensure we have at least an empty collection to work with
                if (_modsData == null || _modsData.Mods == null)
                {
                    _modsData = new ModsCustomRoot { Mods = new Dictionary<string, ModCustomInfo>() };
                }
                // Don't rethrow - we'll continue with an empty collection
            }
        }


        /// <summary>
        /// Loads the mods data from the JSON file.
        /// </summary>
        private async Task LoadModsDataAsync()
        {
            try
            {
                string json = await File.ReadAllTextAsync(_modsJsonPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                _modsData = JsonSerializer.Deserialize<ModsCustomRoot>(json, options) ?? new ModsCustomRoot();

                // Ensure Mods property is initialized
                if (_modsData.Mods == null)
                {
                    _modsData.Mods = new Dictionary<string, ModCustomInfo>();
                }

                Debug.WriteLine($"Loaded Mods.json with {_modsData.Mods.Count} custom mods");
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"Error parsing Mods.json: {ex.Message}");
                _modsData = new ModsCustomRoot { Mods = new Dictionary<string, ModCustomInfo>() };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading Mods.json: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Saves the current mods data to the JSON file.
        /// </summary>
        private async Task SaveModsDataAsync()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                string json = JsonSerializer.Serialize(_modsData, options);
                await File.WriteAllTextAsync(_modsJsonPath, json);
                Debug.WriteLine($"Saved Mods.json with {_modsData.Mods.Count} custom mods");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving Mods.json: {ex.Message}");
                throw;
            }
        }

        /// <inheritdoc />
        public Dictionary<string, ModCustomInfo> GetAllCustomMods()
        {
            EnsureInitializedAsync().Wait();
            return new Dictionary<string, ModCustomInfo>(_modsData.Mods);
        }

        /// <inheritdoc />
        public ModCustomInfo GetCustomModInfo(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
                throw new ArgumentNullException(nameof(packageId));

            EnsureInitializedAsync().Wait();

            string normalizedId = packageId.ToLowerInvariant();

            // Try exact match first
            if (_modsData.Mods.TryGetValue(normalizedId, out var info))
            {
                return info;
            }

            // Try case-insensitive match as fallback
            var matchingKey = _modsData.Mods.Keys
                .FirstOrDefault(k => string.Equals(k, normalizedId, StringComparison.OrdinalIgnoreCase));

            if (matchingKey != null)
            {
                return _modsData.Mods[matchingKey];
            }

            // Not found, return null
            return null;
        }

        /// <inheritdoc />
        public async Task SaveCustomModInfoAsync(string packageId, ModCustomInfo customInfo)
        {
            if (string.IsNullOrEmpty(packageId))
                throw new ArgumentNullException(nameof(packageId));

            if (customInfo == null)
                throw new ArgumentNullException(nameof(customInfo));

            await EnsureInitializedAsync();

            string normalizedId = packageId.ToLowerInvariant();

            // Clean up empty collections before saving
            CleanupEmptyCollections(customInfo);

            // Update or add the custom mod info
            _modsData.Mods[normalizedId] = customInfo;

            // Save changes to file
            await SaveModsDataAsync();

            Debug.WriteLine($"Saved custom info for mod: {packageId}");
        }

        /// <inheritdoc />
        public async Task RemoveCustomModInfoAsync(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
                throw new ArgumentNullException(nameof(packageId));

            await EnsureInitializedAsync();

            string normalizedId = packageId.ToLowerInvariant();

            // Try exact match first
            bool removed = _modsData.Mods.Remove(normalizedId);

            if (!removed)
            {
                // Try case-insensitive match as fallback
                var matchingKey = _modsData.Mods.Keys
                    .FirstOrDefault(k => string.Equals(k, normalizedId, StringComparison.OrdinalIgnoreCase));

                if (matchingKey != null)
                {
                    _modsData.Mods.Remove(matchingKey);
                    removed = true;
                }
            }

            if (removed)
            {
                await SaveModsDataAsync();
                Debug.WriteLine($"Removed custom info for mod: {packageId}");
            }
            else
            {
                Debug.WriteLine($"No custom info found to remove for mod: {packageId}");
            }
        }

        /// <inheritdoc />
        public void ApplyCustomInfoToMods(IEnumerable<ModItem> mods)
        {
            if (mods == null)
                throw new ArgumentNullException(nameof(mods));

            try
            {
                EnsureInitializedAsync().Wait(); // Still not ideal, but let's make it safer

                // Ensure we have mod data
                if (_modsData == null || _modsData.Mods == null)
                {
                    Debug.WriteLine("No custom mod data available to apply");
                    return; // Exit early instead of causing an exception
                }

                foreach (var mod in mods)
                {
                    // Skip core and expansion mods
                    if (mod.ModType == ModType.Core || mod.ModType == ModType.Expansion)
                        continue;

                    if (string.IsNullOrEmpty(mod.PackageId))
                        continue;

                    var customInfo = GetCustomModInfo(mod.PackageId);
                    if (customInfo == null)
                        continue;

                    // Apply custom information to the mod
                    ApplyCustomInfoToMod(mod, customInfo);
                }

                Debug.WriteLine($"Applied custom info to applicable mods");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ApplyCustomInfoToMods: {ex.Message}");
                // Continue without applying custom info
            }
        }

        /// <summary>
        /// Applies custom information to a specific mod item.
        /// </summary>
        private void ApplyCustomInfoToMod(ModItem mod, ModCustomInfo customInfo)
        {
            // Apply ExternalUrl
            if (!string.IsNullOrEmpty(customInfo.ExternalUrl))
            {
                mod.ExternalUrl = customInfo.ExternalUrl;
            }

            if (!string.IsNullOrEmpty(customInfo.Tags))
            {
                mod.Tags = customInfo.Tags;
            }

            // Apply SupportedVersions
            if (customInfo.SupportedVersions != null && customInfo.SupportedVersions.Count > 0)
            {
                // Create a lookup of existing versions for efficient checking
                var existingVersions = mod.SupportedVersions
                    .ToDictionary(v => v.Version.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);

                // Add versions from custom info that don't already exist
                foreach (string version in customInfo.SupportedVersions)
                {
                    string versionLower = version.ToLowerInvariant();
                    if (!existingVersions.ContainsKey(versionLower))
                    {
                        mod.SupportedVersions.Add(new VersionSupport(version, true)); // Set unofficial = true
                        Debug.WriteLine($"Added unofficial support for version {version} to mod {mod.PackageId}");
                    }
                }
            }

            // Apply LoadBefore
            if (customInfo.LoadBefore != null && customInfo.LoadBefore.Count > 0)
            {
                foreach (var target in customInfo.LoadBefore.Keys)
                {
                    if (!mod.LoadBefore.Contains(target))
                    {
                        mod.LoadBefore.Add(target);
                        Debug.WriteLine($"Added LoadBefore: {mod.PackageId} -> {target}");
                    }
                }
            }

            // Apply LoadAfter
            if (customInfo.LoadAfter != null && customInfo.LoadAfter.Count > 0)
            {
                foreach (var target in customInfo.LoadAfter.Keys)
                {
                    if (!mod.LoadAfter.Contains(target))
                    {
                        mod.LoadAfter.Add(target);
                        Debug.WriteLine($"Added LoadAfter: {mod.PackageId} -> {target}");
                    }
                }
            }

            // Apply LoadBottom
            if (customInfo.LoadBottom != null && customInfo.LoadBottom.Value)
            {
                mod.LoadBottom = true;
                Debug.WriteLine($"Applied LoadBottom=true to mod {mod.PackageId}");
            }

            // Apply IncompatibleWith
            if (customInfo.IncompatibleWith != null && customInfo.IncompatibleWith.Count > 0)
            {
                foreach (var target in customInfo.IncompatibleWith.Keys)
                {
                    if (!mod.IncompatibleWith.Contains(target))
                    {
                        mod.IncompatibleWith.Add(target);
                        Debug.WriteLine($"Added Incompatibility: {mod.PackageId} -> {target}");
                    }
                }
            }
        }

        /// <summary>
        /// Cleans up empty collections to avoid storing unnecessary data.
        /// </summary>
        private void CleanupEmptyCollections(ModCustomInfo customInfo)
        {
            if (customInfo.LoadBefore.Count == 0)
                customInfo.LoadBefore = null;

            if (customInfo.LoadAfter.Count == 0)
                customInfo.LoadAfter = null;

            if (customInfo.IncompatibleWith.Count == 0)
                customInfo.IncompatibleWith = null;

            if (customInfo.SupportedVersions.Count == 0)
                customInfo.SupportedVersions = null;

            // For LoadBottom, we need to check if it exists and has a non-true value
            if (customInfo.LoadBottom != null && !customInfo.LoadBottom.Value)
                customInfo.LoadBottom = null;
        }
    }
}