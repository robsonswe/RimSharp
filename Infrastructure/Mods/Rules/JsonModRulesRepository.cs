using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.Rules
{
    public class JsonModRulesRepository : IModRulesRepository
    {
        private const string RulesFileName = "rules.json";
        private readonly string _rulesFilePath;
        private readonly string _rulesDirectoryPath;
        private Dictionary<string, ModRule> _cachedRules;
        private bool _rulesLoaded = false;
        private readonly object _loadLock = new object(); // Lock for thread safety during load

        // Constructor now takes the application base path
        public JsonModRulesRepository(string appBasePath)
        {
            if (string.IsNullOrEmpty(appBasePath))
                throw new ArgumentNullException(nameof(appBasePath));

            // Updated path calculation
            _rulesDirectoryPath = Path.Combine(appBasePath, "Rules", "db");
            _rulesFilePath = Path.Combine(_rulesDirectoryPath, RulesFileName);
            Console.WriteLine($"[DEBUG] JsonModRulesRepository initialized. Rules file path: '{_rulesFilePath}'");
            _cachedRules = new Dictionary<string, ModRule>(StringComparer.OrdinalIgnoreCase); // Initialize with comparer
        }

        public Dictionary<string, ModRule> GetAllRules()
        {
            // Double-check locking for thread safety
            if (_rulesLoaded)
            {
                // Console.WriteLine($"[DEBUG] Returning cached rules. Count: {_cachedRules.Count}");
                return _cachedRules;
            }

            lock (_loadLock)
            {
                // Check again inside the lock
                if (_rulesLoaded)
                {
                    return _cachedRules;
                }

                Console.WriteLine($"[DEBUG] JsonModRulesRepository.GetAllRules called. Attempting to load from: '{_rulesFilePath}'");
                try
                {
                    // 1. Ensure the directory exists
                    if (!Directory.Exists(_rulesDirectoryPath))
                    {
                        Console.WriteLine($"[DEBUG] Rules directory not found at '{_rulesDirectoryPath}'. Creating...");
                        Directory.CreateDirectory(_rulesDirectoryPath);
                        Console.WriteLine($"[DEBUG] Rules directory created.");
                    }

                    // 2. Check if the file exists, create with default if not
                    if (!File.Exists(_rulesFilePath))
                    {
                        Console.WriteLine($"[DEBUG] Rules file not found at '{_rulesFilePath}'. Creating with default content.");
                        File.WriteAllText(_rulesFilePath, "{\"rules\":{}}"); // Default content
                        Console.WriteLine($"[DEBUG] Default rules file created.");
                        _rulesLoaded = true;
                        _cachedRules = new Dictionary<string, ModRule>(StringComparer.OrdinalIgnoreCase); // Ensure it's empty and case-insensitive
                        return _cachedRules; // Return the empty dictionary
                    }

                    // 3. File exists, proceed with loading
                    Console.WriteLine($"[DEBUG] Rules file found. Reading content...");
                    var json = File.ReadAllText(_rulesFilePath);
                    // Console.WriteLine($"[DEBUG] Rules file read successfully. Length: {json.Length} characters.");

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                    // Console.WriteLine("[DEBUG] Deserializing root JSON object...");
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

                        // Optional: Normalize keys if needed, though comparer handles lookups
                        // var normalizedRules = new Dictionary<string, ModRule>(StringComparer.OrdinalIgnoreCase);
                        // foreach (var kvp in root.Rules)
                        // {
                        //     normalizedRules[kvp.Key.ToLowerInvariant()] = kvp.Value;
                        // }
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
            // Console.WriteLine($"[DEBUG] JsonModRulesRepository.GetRulesForMod called for packageId: '{packageId}'");
            var rules = GetAllRules(); // Ensures rules are loaded (uses caching)

            if (string.IsNullOrEmpty(packageId))
            {
                 // Console.WriteLine($"[DEBUG] packageId is null or empty. Returning default ModRule.");
                 return new ModRule();
            }

            // Use case-insensitive lookup thanks to the dictionary's comparer
            if (rules.TryGetValue(packageId, out var rule)) // No need to ToLowerInvariant() here
            {
                // Console.WriteLine($"[DEBUG] Found rules for packageId '{packageId}'.");
                // if (rule.LoadBottom != null) Console.WriteLine($"[DEBUG] Found LoadBottom={rule.LoadBottom.Value} for packageId '{packageId}'");
                return rule;
            }
            else
            {
                // Console.WriteLine($"[DEBUG] No rules found for packageId '{packageId}'. Returning default ModRule.");
                return new ModRule();
            }
        }

        private class RulesJsonRoot
        {
            public Dictionary<string, ModRule> Rules { get; set; }
        }
    }
}
