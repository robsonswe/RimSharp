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

        public ConfigService()
        {
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.cfg");
            LoadConfig();
        }

        public void LoadConfig()
        {
            _configValues.Clear();

            if (!File.Exists(_configPath))
            {
                // Create default config file
                SetConfigValue("game_folder", string.Empty);
                SetConfigValue("config_folder", string.Empty);
                SetConfigValue("mods_folder", string.Empty);
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
                        _configValues[parts[0].Trim()] = parts[1].Trim();
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
                File.WriteAllLines(_configPath, _configValues.Select(kv => $"{kv.Key}={kv.Value}"));
            }
            catch (IOException)
            {
                // Handle file write errors gracefully
            }
        }

        public string GetConfigValue(string key) => 
            _configValues.TryGetValue(key, out var value) ? value : string.Empty;

        public void SetConfigValue(string key, string value) => 
            _configValues[key] = value;
    }
}