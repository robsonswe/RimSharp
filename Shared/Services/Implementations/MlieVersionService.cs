using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RimSharp.Infrastructure.Logging;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Shared.Services.Implementations
{
    public class MlieVersionService : IMlieVersionService
    {
        private const string MlieModFolderId = "2599504692";
        private const string MlieFileName = "ModIdsToFix.xml";

        private readonly IPathService _pathService;
        private readonly IDataUpdateService _dataUpdateService;
        private readonly ILoggerService _logger;
        private Dictionary<string, List<string>>? _mlieVersionsCache;
        private bool _isInitialized = false;
        private readonly object _lock = new object();

        public MlieVersionService(IPathService pathService, IDataUpdateService dataUpdateService, ILoggerService logger)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _dataUpdateService = dataUpdateService ?? throw new ArgumentNullException(nameof(dataUpdateService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Dictionary<string, List<string>> GetMlieVersions()
        {
            if (_isInitialized && _mlieVersionsCache != null)
            {
                return _mlieVersionsCache;
            }

            lock (_lock)
            {
                if (_isInitialized && _mlieVersionsCache != null)
                {
                    return _mlieVersionsCache;
                }

                _logger.LogInfo("Initializing MlieVersionService cache.", nameof(MlieVersionService));
                _mlieVersionsCache = LoadMlieVersions();
                _isInitialized = true;
                _logger.LogInfo($"MlieVersionService cache initialized. Found compatibility info for {_mlieVersionsCache!.Count} mods.", nameof(MlieVersionService));
                return _mlieVersionsCache!;
            }
        }

        private Dictionary<string, List<string>> LoadMlieVersions()
        {
            var results = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            // Priority union: the locally installed mod first, then the
            // repository snapshot synced by the data updater. Both share the
            // same per-version XML layout; a packageId known to both sources
            // gets the additive union of its version lists. (No curated layer
            // exists yet; Data/Versions/Custom/<ver>/ModIdsToFix.xml would
            // parse with the same walker if one is added later.)
            LoadFromModFolder(results);
            LoadFromRepositorySnapshot(results);

            return results;
        }

        private void LoadFromModFolder(Dictionary<string, List<string>> results)
        {
            var modsPath = _pathService.GetModsPath();

            if (string.IsNullOrEmpty(modsPath) || !Directory.Exists(modsPath))
            {
                _logger.LogWarning($"Mods path not found or not set ('{modsPath}'). Cannot load Mlie version data from the mod folder.", nameof(MlieVersionService));
                return;
            }

            var mlieModPath = Path.Combine(modsPath, MlieModFolderId);
            int count = LoadFromVersionDirectories(mlieModPath, results);
            _logger.LogDebug($"Loaded {count} packageId entries from the Mlie mod folder '{mlieModPath}'.", nameof(MlieVersionService));
        }

        private void LoadFromRepositorySnapshot(Dictionary<string, List<string>> results)
        {
            try
            {
                var snapshotRoot = Path.GetDirectoryName(
                    _dataUpdateService.GetExternalFilePath("Versions", "NoVersionWarning", MlieFileName))!;
                int count = LoadFromVersionDirectories(snapshotRoot, results);
                _logger.LogDebug($"Loaded {count} packageId entries from the 'No Version Warning' repository snapshot '{snapshotRoot}'.", nameof(MlieVersionService));
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Failed to load the 'No Version Warning' repository snapshot.", nameof(MlieVersionService));
            }
        }

        /// <summary>
        /// Walks <paramref name="rootPath"/>'s version directories (e.g. "1.4")
        /// and merges every ModIdsToFix.xml entry into
        /// <paramref name="results"/>, additively (union of version lists).
        /// Shared by the locally installed Mlie mod and the repository
        /// snapshot; a missing root is a logged skip and per-file parse
        /// failures never discard the remaining files.
        /// </summary>
        private int LoadFromVersionDirectories(string rootPath, Dictionary<string, List<string>> results)
        {
            int count = 0;

            try
            {
                if (!Directory.Exists(rootPath))
                {
                    _logger.LogInfo($"Mlie version source not found at '{rootPath}'. Skipping.", nameof(MlieVersionService));
                    return 0;
                }

                var versionDirs = Directory.GetDirectories(rootPath)
                                           .Where(d => Regex.IsMatch(Path.GetFileName(d), @"^\d+\.\d+$"));

                foreach (var versionDir in versionDirs)
                {
                    var versionString = Path.GetFileName(versionDir);
                    var filePath = Path.Combine(versionDir, MlieFileName);

                    if (!File.Exists(filePath))
                    {
                        continue;
                    }

                    try
                    {
                        _logger.LogDebug($"Parsing Mlie file: '{filePath}' for version '{versionString}'.", nameof(MlieVersionService));
                        XDocument doc = XDocument.Load(filePath);
                        var packageIds = doc.Root?.Elements("li")
                                            .Select(li => li.Value?.Trim())
                                            .Where(id => !string.IsNullOrEmpty(id))
                                            .ToList();

                        if (packageIds == null || !packageIds.Any())
                        {
                            continue;
                        }

                        foreach (var packageId in packageIds)
                        {
                            string lowerPackageId = packageId!.ToLowerInvariant();
                            if (!results.TryGetValue(lowerPackageId, out var versionList))
                            {
                                versionList = new List<string>();
                                results[lowerPackageId] = versionList;
                            }
                            if (!versionList.Contains(versionString, StringComparer.OrdinalIgnoreCase))
                            {
                                versionList.Add(versionString);
                            }
                            count++;
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
                _logger.LogException(uaEx, $"Permission error accessing Mlie version directory '{rootPath}' or subdirectories", nameof(MlieVersionService));
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, $"Unexpected error processing Mlie version directory '{rootPath}'.", nameof(MlieVersionService));
            }

            return count;
        }
    }
}
