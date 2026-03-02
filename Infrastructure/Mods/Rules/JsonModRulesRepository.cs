using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RimSharp.Infrastructure.Data;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.Rules
{
    public class JsonModRulesRepository : IModRulesRepository
    {
        private const string RulesFileName = "rules.json";
        private readonly string _rulesFilePath;
        private readonly string? _rulesDirectoryPath;
        private Dictionary<string, ModRule> _cachedRules;
        private bool _rulesLoaded = false;
        private readonly object _loadLock = new object(); // Lock for thread safety during load

        // Constructor now takes the application base path
        public JsonModRulesRepository(IDataUpdateService dataUpdateService)
        {
            if (dataUpdateService == null)
                throw new ArgumentNullException(nameof(dataUpdateService));

            _rulesFilePath = dataUpdateService.GetDataFilePath(RulesFileName);

            _rulesDirectoryPath = Path.GetDirectoryName(_rulesFilePath);

            Console.WriteLine($"[DEBUG] JsonModRulesRepository initialized. Rules file path: '{_rulesFilePath}'");
            _cachedRules = new Dictionary<string, ModRule>(StringComparer.OrdinalIgnoreCase);
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

                Console.WriteLine($"[DEBUG] JsonModRulesRepository.GetAllRules called. Attempting to load from: '{_rulesFilePath}'");
                try
                {
                    if (!string.IsNullOrEmpty(_rulesDirectoryPath) && !Directory.Exists(_rulesDirectoryPath))
                    {
                        Console.WriteLine($"[DEBUG] Rules directory not found at '{_rulesDirectoryPath}'. Creating...");
                        Directory.CreateDirectory(_rulesDirectoryPath);
                        Console.WriteLine($"[DEBUG] Rules directory created.");
                    }
                    if (!File.Exists(_rulesFilePath))
                    {
                        Console.WriteLine($"[WARNING] Rules file not found in cache at '{_rulesFilePath}'. Returning empty rules. App may need to restart or run update check.");
                        _cachedRules = new Dictionary<string, ModRule>(StringComparer.OrdinalIgnoreCase);
                        return _cachedRules;
                    }

                    // 3. File exists, proceed with loading
                    Console.WriteLine($"[DEBUG] Rules file found. Reading content...");
                    var json = File.ReadAllText(_rulesFilePath);

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                    // Directly deserialize the structure expected by ModRule
                    var root = JsonSerializer.Deserialize<RulesJsonRoot>(json, options);
                    if (root == null || root.Rules == null)
                    {
                        Console.WriteLine("[DEBUG] JSON structure invalid or 'rules' key missing/null. Using empty dictionary.");
                        _cachedRules = new Dictionary<string, ModRule>(StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        Console.WriteLine("[DEBUG] Deserializing 'rules' object into Dictionary<string, ModRule>...");
                        // Convert to a case-insensitive dictionary during assignment
                        _cachedRules = new Dictionary<string, ModRule>(root.Rules, StringComparer.OrdinalIgnoreCase);
                        Console.WriteLine($"[DEBUG] Successfully deserialized rules. Found {_cachedRules.Count} rule entries.");

                        // var normalizedRules = new Dictionary<string, ModRule>(StringComparer.OrdinalIgnoreCase);
                        // foreach (var kvp in root.Rules)
                        // {
                        //     normalizedRules[kvp.Key.ToLowerInvariant()] = kvp.Value;

                        // _cachedRules = normalizedRules;

                    }
                }
                catch (JsonException jsonEx)
                {
                    Console.WriteLine($"[ERROR] Error parsing JSON rules file '{_rulesFilePath}': {jsonEx.Message}. Using empty rules.");
                    _cachedRules = new Dictionary<string, ModRule>(StringComparer.OrdinalIgnoreCase);
                }
                catch (IOException ioEx)
                {
                    Console.WriteLine($"[ERROR] IO error accessing rules file '{_rulesFilePath}': {ioEx.Message}. Using empty rules.");
                    _cachedRules = new Dictionary<string, ModRule>(StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Unexpected error loading mod rules from '{_rulesFilePath}': {ex.Message}");
                    _cachedRules = new Dictionary<string, ModRule>(StringComparer.OrdinalIgnoreCase); // Default to empty on error
                }
                finally
                {
                    _rulesLoaded = true; // Mark as loaded even if an error occurred (to avoid retrying constantly)
                }

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
            else
            {

                return new ModRule();
            }
        }

        private class RulesJsonRoot
        {
            public Dictionary<string, ModRule>? Rules { get; set; }
        }
    }
}


