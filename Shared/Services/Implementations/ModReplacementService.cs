using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using RimSharp.Infrastructure.Logging; 
using RimSharp.Infrastructure.Data;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Shared.Services.Implementations
{
    public class ModReplacementService : IModReplacementService
    {
        private const string UseThisInsteadModFolderId = "3396308787";
        private const string UseThisInsteadReplacementsSubFolder = "Replacements";
        private const string DatabaseReplacementsFileName = "replacements.json";
        private readonly IDataUpdateService _dataUpdateService;
        private readonly IPathService _pathService;
        private readonly ILoggerService _logger;
        private Dictionary<string, ModReplacementInfo>? _replacementsCache;
        private bool _isInitialized = false;
        private readonly object _lock = new object();

        public ModReplacementService(IPathService pathService, IDataUpdateService dataUpdateService, ILoggerService logger)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _dataUpdateService = dataUpdateService ?? throw new ArgumentNullException(nameof(dataUpdateService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Dictionary<string, ModReplacementInfo> GetAllReplacements()
        {
            if (_isInitialized && _replacementsCache != null)
            {
                return _replacementsCache;
            }

            lock (_lock)
            {
                if (_isInitialized && _replacementsCache != null)
                {
                    return _replacementsCache;
                }

                _logger.LogInfo("Initializing ModReplacementService cache.", nameof(ModReplacementService));
                _replacementsCache = LoadReplacements();
                _isInitialized = true;
                _logger.LogInfo($"ModReplacementService cache initialized. Found {_replacementsCache.Count} unique mod replacement entries.", nameof(ModReplacementService));
                return _replacementsCache;
            }
        }

        public ModReplacementInfo? GetReplacementBySteamId(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId)) return null;

            var cache = GetAllReplacements(); 
            string normalizedId = steamId.ToLowerInvariant();

            cache.TryGetValue(normalizedId, out var replacement);
            return replacement; 
        }

        public ModReplacementInfo? GetReplacementByPackageId(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return null;

            var cache = GetAllReplacements(); 
            string normalizedId = packageId.ToLowerInvariant(); 

            return cache.Values.FirstOrDefault(r =>
                string.IsNullOrEmpty(r.SteamId) &&
                !string.IsNullOrEmpty(r.ModId) &&
                r.ModId.Equals(normalizedId, StringComparison.OrdinalIgnoreCase));
        }

        private Dictionary<string, ModReplacementInfo> LoadReplacements()
        {
            var results = new Dictionary<string, ModReplacementInfo>(StringComparer.OrdinalIgnoreCase);

            int dbCount = LoadFromJson(results);
            _logger.LogDebug($"Loaded {dbCount} replacements from JSON database.", nameof(ModReplacementService));

            int utiCount = LoadFromUseThisInsteadXml(results);
            _logger.LogDebug($"Loaded {utiCount} new replacements from 'Use This Instead' XMLs (duplicates ignored).", nameof(ModReplacementService));

            return results;
        }

        /// <summary>
        /// Loads replacement data from the replacements.json file.
        /// Populates the provided dictionary. Ensures directory and file exist.
        /// </summary>
        /// <returns>The number of items successfully loaded from JSON.</returns>
        private int LoadFromJson(Dictionary<string, ModReplacementInfo> targetDictionary)
        {
            var jsonFilePath = _dataUpdateService.GetDataFilePath(DatabaseReplacementsFileName);
            var dbDirectoryPath = Path.GetDirectoryName(jsonFilePath);
            int count = 0;

            try
            {
                if (dbDirectoryPath != null && !Directory.Exists(dbDirectoryPath))
                {
                    _logger.LogInfo($"Creating replacement database directory: '{dbDirectoryPath}'", nameof(ModReplacementService));
                    Directory.CreateDirectory(dbDirectoryPath);
                }
                if (!File.Exists(jsonFilePath))
                {
                    _logger.LogWarning($"Replacement database file '{jsonFilePath}' not found. Creating with default content.", nameof(ModReplacementService));
                    File.WriteAllText(jsonFilePath, "{\"mods\":{}}");
                    _logger.LogInfo($"Default replacement file created at '{jsonFilePath}'.", nameof(ModReplacementService));
                    return 0; 
                }

                _logger.LogDebug($"Parsing replacement database: '{jsonFilePath}'", nameof(ModReplacementService));
                string jsonContent = File.ReadAllText(jsonFilePath);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var jsonData = JsonSerializer.Deserialize<ReplacementJsonRoot>(jsonContent, options);

                if (jsonData?.Mods != null)
                {
                    foreach (var kvp in jsonData.Mods)
                    {
                        var info = kvp.Value;
                        if (info == null)
                        {
                            _logger.LogWarning($"Skipping null entry in '{jsonFilePath}' for key '{kvp.Key}'.", nameof(ModReplacementService));
                            continue;
                        }

                        string key = info.SteamId?.Trim().ToLowerInvariant()!;
                        if (string.IsNullOrEmpty(key))
                        {
                            key = $"packageid_{info.ModId?.Trim().ToLowerInvariant() ?? Guid.NewGuid().ToString()}";
                        }

                        if (targetDictionary.ContainsKey(key))
                        {
                            _logger.LogWarning($"Duplicate key '{key}' detected in '{jsonFilePath}'. Overwriting previous entry.", nameof(ModReplacementService));
                        }

                        info.ModId = info.ModId?.Trim().ToLowerInvariant() ?? string.Empty;
                        info.ReplacementModId = info.ReplacementModId?.Trim().ToLowerInvariant() ?? string.Empty;
                        info.Source = ReplacementSource.Database;

                        targetDictionary[key] = info;
                        count++;
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.LogException(jsonEx, $"Error parsing JSON file '{jsonFilePath}'.", nameof(ModReplacementService));
            }
            catch (IOException ioEx)
            {
                _logger.LogException(ioEx, $"IO error accessing file '{jsonFilePath}'.", nameof(ModReplacementService));
            }
            catch (UnauthorizedAccessException uaEx)
            {
                _logger.LogException(uaEx, $"Permission error accessing directory '{dbDirectoryPath}' or file '{jsonFilePath}'.", nameof(ModReplacementService));
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, $"Unexpected error loading replacements from '{jsonFilePath}'.", nameof(ModReplacementService));
            }

            return count;
        }

        /// <summary>
        /// Loads replacement data from the 'Use This Instead' mod's XML files.
        /// Populates the provided dictionary, *only* adding entries if the SteamId doesn't already exist.
        /// </summary>
        /// <returns>The number of new items successfully loaded from XMLs.</returns>
        private int LoadFromUseThisInsteadXml(Dictionary<string, ModReplacementInfo> targetDictionary)
        {
            var modsPath = _pathService.GetModsPath();
            int count = 0;

            if (string.IsNullOrEmpty(modsPath) || !Directory.Exists(modsPath))
            {
                _logger.LogWarning($"Mods path not found or not set ('{modsPath}'). Cannot load 'Use This Instead' replacement data.", nameof(ModReplacementService));
                return 0;
            }
            var utiModPath = Path.Combine(modsPath, UseThisInsteadModFolderId);
            if (!Directory.Exists(utiModPath))
            {
                _logger.LogInfo($"'Use This Instead' mod ({UseThisInsteadModFolderId}) not found at '{utiModPath}'. Skipping its replacement data.", nameof(ModReplacementService));
                return 0;
            }

            var replacementsPath = Path.Combine(utiModPath, UseThisInsteadReplacementsSubFolder);
            if (!Directory.Exists(replacementsPath))
            {
                _logger.LogWarning($"Replacements subfolder '{UseThisInsteadReplacementsSubFolder}' not found within '{utiModPath}'. Skipping 'Use This Instead' data.", nameof(ModReplacementService));
                return 0;
            }

            _logger.LogDebug($"Scanning 'Use This Instead' replacements folder: '{replacementsPath}'", nameof(ModReplacementService));

            try
            {
                var xmlFiles = Directory.GetFiles(replacementsPath, "*.xml", SearchOption.TopDirectoryOnly);

                foreach (var filePath in xmlFiles)
                {
                    try
                    {
                        XDocument doc = XDocument.Load(filePath);
                        XElement? root = doc.Root;

                        if (root == null || root.Name != "ModReplacement")
                        {
                            _logger.LogWarning($"Skipping file '{filePath}'. Invalid root element or missing.", nameof(ModReplacementService));
                            continue;
                        }

                        var info = new ModReplacementInfo
                        {
                            Author = root.Element("Author")?.Value?.Trim() ?? string.Empty,
                            ModId = root.Element("ModId")?.Value?.Trim().ToLowerInvariant() ?? string.Empty, 
                            ModName = root.Element("ModName")?.Value?.Trim() ?? string.Empty,
                            SteamId = root.Element("SteamId")?.Value?.Trim() ?? string.Empty,
                            Versions = root.Element("Versions")?.Value?.Trim() ?? string.Empty,
                            ReplacementAuthor = root.Element("ReplacementAuthor")?.Value?.Trim() ?? string.Empty,
                            ReplacementModId = root.Element("ReplacementModId")?.Value?.Trim().ToLowerInvariant() ?? string.Empty, 
                            ReplacementName = root.Element("ReplacementName")?.Value?.Trim() ?? string.Empty,
                            ReplacementSteamId = root.Element("ReplacementSteamId")?.Value?.Trim() ?? string.Empty,
                            ReplacementVersions = root.Element("ReplacementVersions")?.Value?.Trim() ?? string.Empty,
                            Source = ReplacementSource.UseThisInstead
                        };

                        string key = info.SteamId?.Trim().ToLowerInvariant()!;

                        if (string.IsNullOrEmpty(key))
                        {
                            key = $"packageid_{info.ModId ?? Guid.NewGuid().ToString()}";
                        }

                        if (!targetDictionary.ContainsKey(key))
                        {
                            targetDictionary.Add(key, info);
                            count++;
                        }
                    }
                    catch (System.Xml.XmlException xmlEx)
                    {
                        _logger.LogException(xmlEx, $"Error parsing XML file '{filePath}'", nameof(ModReplacementService));
                    }
                    catch (IOException ioEx)
                    {
                        _logger.LogException(ioEx, $"IO error reading file '{filePath}'", nameof(ModReplacementService));
                    }
                }
            }
            catch (UnauthorizedAccessException uaEx)
            {
                _logger.LogException(uaEx, $"Permission error accessing 'Use This Instead' mod directory '{replacementsPath}' or files within.", nameof(ModReplacementService));
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, $"Unexpected error processing 'Use This Instead' replacements folder '{replacementsPath}'.", nameof(ModReplacementService));
            }

            return count;
        }

        private class ReplacementJsonRoot
        {
            public Dictionary<string, ModReplacementInfo>? Mods { get; set; }
        }
    }
}
