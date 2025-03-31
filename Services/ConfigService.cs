using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RimSharp.Services
{
    public class ConfigService : IConfigService
    {
        private readonly string _configPath;
        private readonly Dictionary<string, string> _configValues;

        public ConfigService()
        {
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.cfg");
            _configValues = new Dictionary<string, string>();
            LoadConfig();
        }

        public void LoadConfig()
        {
            _configValues.Clear();

            if (!File.Exists(_configPath))
            {
                // Create default config file
                SetConfigValue("game_folder", "");
                SetConfigValue("config_folder", "");
                SetConfigValue("mods_folder", "");
                SaveConfig();
                return;
            }

            foreach (var line in File.ReadAllLines(_configPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                
                var parts = line.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    _configValues[parts[0].Trim()] = parts[1].Trim();
                }
            }
        }

        public void SaveConfig()
        {
            var lines = _configValues.Select(kv => $"{kv.Key}={kv.Value}");
            File.WriteAllLines(_configPath, lines);
        }

        public string GetConfigValue(string key)
        {
            return _configValues.TryGetValue(key, out var value) ? value : string.Empty;
        }

        public void SetConfigValue(string key, string value)
        {
            _configValues[key] = value;
        }
    }
}