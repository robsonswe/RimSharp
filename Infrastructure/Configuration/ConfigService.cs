#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Configuration
{
    public class ConfigService : IConfigService
    {
        private readonly string _configPath;
        private readonly Dictionary<string, string> _configValues = new();

        public ConfigService(string? configPath = null)
        {
            _configPath = configPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.cfg");
            LoadConfig();
        }

        public void LoadConfig()
        {
            _configValues.Clear();

            if (!File.Exists(_configPath))
            {
                // Create default config file (without mods_folder)
                SetConfigValue("game_folder", string.Empty);
                SetConfigValue("config_folder", string.Empty);
                // SetConfigValue("mods_folder", string.Empty); // REMOVED
                SaveConfig();
                return;
            }

            try
            {
                foreach (var line in File.ReadLines(_configPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                        continue;

                    var parts = line.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim();

                        // Only load relevant keys (optional, but cleaner)
                        if (key == "game_folder" || key == "config_folder")
                        {
                             _configValues[key] = value;
                        }
                        // Ignore "mods_folder" if found in an old config
                    }
                }
            }
            catch (IOException)
            {
                // If file can't be read, use empty values
                _configValues.Clear();
            }
        }

        public void SaveConfig()
        {
            try
            {
                // Only save the keys we actually manage now
                var linesToSave = new List<string>();
                if (_configValues.TryGetValue("game_folder", out var gameFolder))
                {
                    linesToSave.Add($"game_folder={gameFolder}");
                }
                 if (_configValues.TryGetValue("config_folder", out var configFolder))
                {
                    linesToSave.Add($"config_folder={configFolder}");
                }
                // Do not save "mods_folder"

                File.WriteAllLines(_configPath, linesToSave);
            }
            catch (IOException)
            {
                // Handle file write errors gracefully
                Console.WriteLine($"Error saving config file: {_configPath}"); // Basic error logging
            }
        }

        public string GetConfigValue(string key) =>
            _configValues.TryGetValue(key, out var value) ? value : string.Empty;

        public void SetConfigValue(string key, string value)
        {
             // Only allow setting relevant keys (prevents mods_folder being re-added accidentally)
            if (key == "game_folder" || key == "config_folder")
            {
                 _configValues[key] = value ?? string.Empty; // Ensure value is not null
            }
        }
    }
}
