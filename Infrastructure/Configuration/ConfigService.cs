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
        // Keys that are persisted to config.cfg. game_folder/config_folder are
        // user-selected game paths; steamcmd_prefix and logs_directory let
        // users redirect SteamCMD data and log output. The AI keys feed the
        // provider clients (see IAIProviderClient implementations).
        private static readonly string[] PersistedKeys =
        {
            "game_folder",
            "config_folder",
            "steamcmd_prefix",
            "logs_directory",
            "openrouter_api_key",
            "ai_model",
        };

        private readonly string _configPath;
        private readonly Dictionary<string, string> _configValues = new();

        public ConfigService(string? configPath = null)
        {
            _configPath = configPath ?? AppDataPaths.GetConfigFilePath();
            EnsureConfigDirectoryExists();
            LoadConfig();
        }

        private void EnsureConfigDirectoryExists()
        {
            try
            {
                var directory = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            catch
            {
                // Best effort; SaveConfig reports write failures.
            }
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

                        if (IsPersistedKey(key))
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
                foreach (var key in PersistedKeys)
                {
                    if (_configValues.TryGetValue(key, out var value))
                    {
                        linesToSave.Add($"{key}={value}");
                    }
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
            if (IsPersistedKey(key))
            {
                 _configValues[key] = value ?? string.Empty;
            }
        }

        private static bool IsPersistedKey(string key) =>
            Array.IndexOf(PersistedKeys, key) >= 0;
    }
}
