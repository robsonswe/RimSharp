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
        private Dictionary<string, ModRule> _cachedRules;
        private bool _rulesLoaded = false;

        public JsonModRulesRepository()
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            _rulesFilePath = Path.Combine(appDir, "Rules", RulesFileName);
            Console.WriteLine($"[DEBUG] JsonModRulesRepository initialized. Rules file path: '{_rulesFilePath}'");
            _cachedRules = new Dictionary<string, ModRule>();
        }

        public Dictionary<string, ModRule> GetAllRules()
        {
            if (_rulesLoaded)
            {
                Console.WriteLine($"[DEBUG] Returning cached rules. Count: {_cachedRules.Count}");
                return _cachedRules;
            }

            Console.WriteLine($"[DEBUG] JsonModRulesRepository.GetAllRules called. Attempting to load from: '{_rulesFilePath}'");
            try
            {
                if (!File.Exists(_rulesFilePath))
                {
                    Console.WriteLine($"[DEBUG] Rules file not found at '{_rulesFilePath}'. Returning empty dictionary.");
                    _rulesLoaded = true;
                    return _cachedRules;
                }

                Console.WriteLine($"[DEBUG] Rules file found. Reading content...");
                var json = File.ReadAllText(_rulesFilePath);
                Console.WriteLine($"[DEBUG] Rules file read successfully. Length: {json.Length} characters.");

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                Console.WriteLine("[DEBUG] Deserializing root JSON object...");
                var root = JsonSerializer.Deserialize<Dictionary<string, object>>(json, options);
                if (root == null)
                {
                    Console.WriteLine("[DEBUG] Root JSON object deserialized to null. Returning empty dictionary.");
                    _rulesLoaded = true;
                    return _cachedRules;
                }
                Console.WriteLine("[DEBUG] Root JSON object deserialized successfully.");

                if (!root.TryGetValue("rules", out var rulesObj))
                {
                    Console.WriteLine("[DEBUG] 'rules' key not found in root JSON object. Returning empty dictionary.");
                    _rulesLoaded = true;
                    return _cachedRules;
                }
                Console.WriteLine("[DEBUG] 'rules' key found in root JSON object.");

                Console.WriteLine("[DEBUG] Serializing 'rules' object part...");
                var rulesJson = JsonSerializer.Serialize(rulesObj);
                Console.WriteLine("[DEBUG] Deserializing 'rules' object into Dictionary<string, ModRule>...");

                // Create a case-insensitive dictionary for mod rules
                var tempRules = JsonSerializer.Deserialize<Dictionary<string, ModRule>>(rulesJson, options)
                    ?? new Dictionary<string, ModRule>();

                // Convert to a case-insensitive dictionary
                _cachedRules = new Dictionary<string, ModRule>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in tempRules)
                {
                    string keyLower = entry.Key.ToLowerInvariant();
                    _cachedRules[keyLower] = entry.Value;
                    Console.WriteLine($"[DEBUG] Added rule for mod: {keyLower}, HasLoadBottom: {entry.Value.LoadBottom != null}");
                }

                _rulesLoaded = true;
                Console.WriteLine($"[DEBUG] Successfully deserialized rules. Found {_cachedRules.Count} rule entries.");
                return _cachedRules;
            }
            catch (Exception ex)
            {
                // Log error
                Console.WriteLine($"[ERROR] Error loading mod rules: {ex.Message}");
                _rulesLoaded = true;
                return _cachedRules;
            }
        }

        public ModRule GetRulesForMod(string packageId)
        {
            Console.WriteLine($"[DEBUG] JsonModRulesRepository.GetRulesForMod called for packageId: '{packageId}'");
            var rules = GetAllRules(); // This will use the cached rules if already loaded

            // Convert packageId to lowercase for consistent lookup
            string packageIdLower = packageId.ToLowerInvariant();

            if (rules.TryGetValue(packageIdLower, out var rule))
            {
                Console.WriteLine($"[DEBUG] Found rules for packageId '{packageId}' (lookup key: '{packageIdLower}').");
                if (rule.LoadBottom != null)
                {
                    Console.WriteLine($"[DEBUG] Found LoadBottom={rule.LoadBottom.Value} for packageId '{packageId}'");
                }
                return rule;
            }
            else
            {
                Console.WriteLine($"[DEBUG] No rules found for packageId '{packageId}' (lookup key: '{packageIdLower}'). Returning default ModRule.");
                return new ModRule();
            }
        }
    }
}
