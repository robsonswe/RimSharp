using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RimSharp.Infrastructure.Logging;
using RimSharp.Infrastructure.Mods.Versions;
using RimSharp.Shared.Models;
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
        private readonly NoVersionWarningSourceRegistry? _sourceRegistry;
        private Dictionary<string, List<ExternalVersionInfo>>? _externalVersionsCache;
        private bool _isInitialized = false;
        private readonly object _lock = new object();

        public MlieVersionService(
            IPathService pathService,
            IDataUpdateService dataUpdateService,
            ILoggerService logger,
            NoVersionWarningSourceRegistry? sourceRegistry = null)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _dataUpdateService = dataUpdateService ?? throw new ArgumentNullException(nameof(dataUpdateService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sourceRegistry = sourceRegistry;
        }

        public void Invalidate()
        {
            lock (_lock)
            {
                _externalVersionsCache = null;
                _isInitialized = false;
            }
        }

        public Dictionary<string, List<ExternalVersionInfo>> GetExternalVersions()
        {
            if (_isInitialized && _externalVersionsCache != null)
            {
                return _externalVersionsCache;
            }

            lock (_lock)
            {
                if (_isInitialized && _externalVersionsCache != null)
                {
                    return _externalVersionsCache;
                }

                _logger.LogInfo("Initializing MlieVersionService cache.", nameof(MlieVersionService));
                _externalVersionsCache = LoadExternalVersions();
                _isInitialized = true;
                _logger.LogInfo($"MlieVersionService cache initialized. Found compatibility info for {_externalVersionsCache!.Count} mods.", nameof(MlieVersionService));
                return _externalVersionsCache!;
            }
        }

        public Dictionary<string, List<string>> GetMlieVersions()
        {
            var attributed = GetExternalVersions();
            return attributed.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Select(v => v.Version).ToList(),
                StringComparer.OrdinalIgnoreCase);
        }

        private Dictionary<string, List<ExternalVersionInfo>> LoadExternalVersions()
        {
            var results = new Dictionary<string, List<ExternalVersionInfo>>(StringComparer.OrdinalIgnoreCase);

            if (_sourceRegistry != null)
            {
                foreach (var source in _sourceRegistry.GetEnabledSources())
                {
                    // Display name of the contributing source; fall back to its Id.
                    var sourceName = !string.IsNullOrWhiteSpace(source.Name) ? source.Name : source.Id;

                    if (source.Type == ModRuleSourceType.Mod)
                    {
                        LoadFromModFolder(sourceName, results);
                    }
                    else if (source.Type == ModRuleSourceType.RepoFile && !string.IsNullOrWhiteSpace(source.Url))
                    {
                        LoadFromRepositorySnapshot(source.Id, sourceName, results);
                    }
                    else if (source.Type == ModRuleSourceType.LocalFile && !string.IsNullOrWhiteSpace(source.LocalPath))
                    {
                        int count = LoadFromVersionDirectories(source.LocalPath, sourceName, results);
                        _logger.LogDebug($"Loaded {count} packageId entries from local path '{source.LocalPath}'.", nameof(MlieVersionService));
                    }
                }
            }
            else
            {
                // No registry (e.g. legacy callers/tests): keep the old two-source
                // behaviour with the registry's default display names.
                LoadFromModFolder(NoVersionWarningSourceRegistry.ModSourceName, results);
                LoadFromRepositorySnapshot("NoVersionWarning", NoVersionWarningSourceRegistry.CommunitySourceName, results);
            }

            return results;
        }

        private void LoadFromModFolder(string sourceName, Dictionary<string, List<ExternalVersionInfo>> results)
        {
            var modsPath = _pathService.GetModsPath();

            if (string.IsNullOrEmpty(modsPath) || !Directory.Exists(modsPath))
            {
                _logger.LogWarning($"Mods path not found or not set ('{modsPath}'). Cannot load Mlie version data from the mod folder.", nameof(MlieVersionService));
                return;
            }

            var mlieModPath = Path.Combine(modsPath, MlieModFolderId);
            int count = LoadFromVersionDirectories(mlieModPath, sourceName, results);
            _logger.LogDebug($"Loaded {count} packageId entries from the Mlie mod folder '{mlieModPath}'.", nameof(MlieVersionService));
        }

        private void LoadFromRepositorySnapshot(string sourceId, string sourceName, Dictionary<string, List<ExternalVersionInfo>> results)
        {
            try
            {
                var snapshotRoot = Path.GetDirectoryName(
                    _dataUpdateService.GetExternalFilePath("Versions", sourceId, MlieFileName))!;
                if (!Directory.Exists(snapshotRoot) && (sourceId == "community-noversionwarning" || sourceId == "NoVersionWarning"))
                {
                    var altRoot = Path.GetDirectoryName(
                        _dataUpdateService.GetExternalFilePath("Versions", "NoVersionWarning", MlieFileName))!;
                    if (Directory.Exists(altRoot)) snapshotRoot = altRoot;
                }

                int count = LoadFromVersionDirectories(snapshotRoot, sourceName, results);
                _logger.LogDebug($"Loaded {count} packageId entries from the 'No Version Warning' repository snapshot '{snapshotRoot}'.", nameof(MlieVersionService));
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, $"Failed to load the 'No Version Warning' repository snapshot for '{sourceId}'.", nameof(MlieVersionService));
            }
        }

        /// <summary>
        /// Walks <paramref name="rootPath"/>'s version directories (e.g. "1.4")
        /// and merges every ModIdsToFix.xml entry into <paramref name="results"/>,
        /// additively (union of version lists). Each entry records
        /// <paramref name="sourceName"/>; when a package id + version was already
        /// contributed by an earlier source, that first attribution is kept.
        /// Shared by the locally installed Mlie mod and the repository snapshot;
        /// a missing root is a logged skip and per-file parse failures never
        /// discard the remaining files.
        /// </summary>
        private int LoadFromVersionDirectories(string rootPath, string sourceName, Dictionary<string, List<ExternalVersionInfo>> results)
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
                                versionList = new List<ExternalVersionInfo>();
                                results[lowerPackageId] = versionList;
                            }
                            // First source in registry order to declare this
                            // package id + version keeps the attribution.
                            if (!versionList.Any(v => string.Equals(v.Version, versionString, StringComparison.OrdinalIgnoreCase)))
                            {
                                versionList.Add(new ExternalVersionInfo(versionString, sourceName));
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
