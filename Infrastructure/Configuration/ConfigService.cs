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
                SetConfigValue("game_folder", string.Empty);
                SetConfigValue("config_folder", string.Empty);
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

                        if (key == "game_folder" || key == "config_folder")
                        {
                             _configValues[key] = value;
                        }
                    }
                }
            }
            catch (IOException)
            {
                _configValues.Clear();
            }
        }

        public void SaveConfig()
        {
            try
            {
                var linesToSave = new List<string>();
                if (_configValues.TryGetValue("game_folder", out var gameFolder))
                {
                    linesToSave.Add($"game_folder={gameFolder}");
                }
                 if (_configValues.TryGetValue("config_folder", out var configFolder))
                {
                    linesToSave.Add($"config_folder={configFolder}");
                }

                File.WriteAllLines(_configPath, linesToSave);
            }
            catch (IOException)
            {
                Console.WriteLine($"Error saving config file: {_configPath}");
            }
        }

        public string GetConfigValue(string key) =>
            _configValues.TryGetValue(key, out var value) ? value : string.Empty;

        public void SetConfigValue(string key, string value)
        {
            if (key == "game_folder" || key == "config_folder")
            {
                 _configValues[key] = value ?? string.Empty;
            }
        }
    }
}
