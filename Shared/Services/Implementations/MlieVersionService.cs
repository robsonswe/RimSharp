using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RimSharp.Infrastructure.Logging; // Assuming you have ILoggerService here
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Shared.Services.Implementations
{
    public class MlieVersionService : IMlieVersionService
    {
        private const string MlieModFolderId = "2599504692";
        private const string MlieFileName = "ModIdsToFix.xml";

        private readonly IPathService _pathService;
        private readonly ILoggerService _logger;
        private Dictionary<string, List<string>> _mlieVersionsCache = null;
        private bool _isInitialized = false;
        private readonly object _lock = new object();

        public MlieVersionService(IPathService pathService, ILoggerService logger)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Dictionary<string, List<string>> GetMlieVersions()
        {
            // Double-check locking for thread safety
            if (_isInitialized)
            {
                return _mlieVersionsCache;
            }

            lock (_lock)
            {
                if (_isInitialized) // Check again inside lock
                {
                    return _mlieVersionsCache;
                }

                _logger.LogInfo("Initializing MlieVersionService cache.", nameof(MlieVersionService));
                _mlieVersionsCache = LoadMlieVersions();
                _isInitialized = true;
                _logger.LogInfo($"MlieVersionService cache initialized. Found compatibility info for {_mlieVersionsCache.Count} mods.", nameof(MlieVersionService));
                return _mlieVersionsCache;
            }
        }

        private Dictionary<string, List<string>> LoadMlieVersions()
        {
            var results = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase); // Use case-insensitive keys
            var modsPath = _pathService.GetModsPath();

            if (string.IsNullOrEmpty(modsPath) || !Directory.Exists(modsPath))
            {
                _logger.LogWarning($"Mods path not found or not set ('{modsPath}'). Cannot load Mlie version data.", nameof(MlieVersionService));
                return results; // Return empty
            }

            var mlieModPath = Path.Combine(modsPath, MlieModFolderId);
            if (!Directory.Exists(mlieModPath))
            {
                _logger.LogInfo($"Mlie's Forcing Mod ({MlieModFolderId}) not found at '{mlieModPath}'. Skipping Mlie version data.", nameof(MlieVersionService));
                return results; // Return empty
            }

            _logger.LogDebug($"Scanning Mlie mod folder '{mlieModPath}' for version data.", nameof(MlieVersionService));

            try
            {
                // Find subdirectories that look like versions (e.g., "1.3", "1.4")
                var versionDirs = Directory.GetDirectories(mlieModPath)
                                           .Where(d => Regex.IsMatch(Path.GetFileName(d), @"^\d+\.\d+$")); // Basic check for X.Y format

                foreach (var versionDir in versionDirs)
                {
                    var versionString = Path.GetFileName(versionDir);
                    var filePath = Path.Combine(versionDir, MlieFileName);

                    if (!File.Exists(filePath))
                    {
                        // _logger.LogDebug($"File '{MlieFileName}' not found in '{versionDir}'. Skipping.", nameof(MlieVersionService));
                        continue; // Skip this version folder if the file isn't there
                    }

                    try
                    {
                        _logger.LogDebug($"Parsing Mlie file: '{filePath}' for version '{versionString}'.", nameof(MlieVersionService));
                        XDocument doc = XDocument.Load(filePath);
                        var packageIds = doc.Root?.Elements("li")
                                            .Select(li => li.Value?.Trim())
                                            .Where(id => !string.IsNullOrEmpty(id))
                                            .ToList();

                        if (packageIds != null && packageIds.Any())
                        {
                           // _logger.LogDebug($"Found {packageIds.Count} package IDs in '{filePath}'.", nameof(MlieVersionService));
                            foreach (var packageId in packageIds)
                            {
                                string lowerPackageId = packageId.ToLowerInvariant(); // Normalize to lowercase
                                if (!results.TryGetValue(lowerPackageId, out var versionList))
                                {
                                    versionList = new List<string>();
                                    results[lowerPackageId] = versionList;
                                }

                                // Add version only if not already present for this mod
                                if (!versionList.Contains(versionString, StringComparer.OrdinalIgnoreCase))
                                {
                                    versionList.Add(versionString);
                                   // _logger.LogTrace($"Added Mlie support: {packageId} -> {versionString}", nameof(MlieVersionService));
                                }
                            }
                        }
                        else
                        {
                           // _logger.LogDebug($"No valid package IDs found in '{filePath}'.", nameof(MlieVersionService));
                        }
                    }
                    catch (System.Xml.XmlException xmlEx)
                    {
                        _logger.LogException(xmlEx, $"Error parsing XML file '{filePath}'", nameof(MlieVersionService));
                    }
                    catch (IOException ioEx)
                    {
                        _logger.LogException(ioEx, $"IO error reading file '{filePath}'", nameof(MlieVersionService));
                    }
                }
            }
            catch (UnauthorizedAccessException uaEx)
            {
                _logger.LogException(uaEx, $"Permission error accessing Mlie mod directory '{mlieModPath}' or subdirectories", nameof(MlieVersionService));
            }
            catch (Exception ex) // Catch-all for unexpected errors during directory traversal etc.
            {
                _logger.LogException(ex, $"Unexpected error processing Mlie mod directory '{mlieModPath}'.", nameof(MlieVersionService));
            }

            return results;
        }
    }
}
