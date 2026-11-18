#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Infrastructure.Data;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.Versions
{
    public class NoVersionWarningSourceManager : INoVersionWarningSourceManager
    {
        private readonly NoVersionWarningSourceRegistry _registry;
        private readonly IMlieVersionService _mlieVersionService;
        private readonly IDataUpdateService _dataUpdateService;
        private readonly IPathService _pathService;
        private readonly IModService? _modService;
        private readonly GameInstance? _gameInstance;
        private readonly ILoggerService _logger;

        public NoVersionWarningSourceManager(
            NoVersionWarningSourceRegistry registry,
            IMlieVersionService mlieVersionService,
            IDataUpdateService dataUpdateService,
            IPathService pathService,
            ILoggerService logger,
            IModService? modService = null,
            GameInstance? gameInstance = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _mlieVersionService = mlieVersionService ?? throw new ArgumentNullException(nameof(mlieVersionService));
            _dataUpdateService = dataUpdateService ?? throw new ArgumentNullException(nameof(dataUpdateService));
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _modService = modService;
            _gameInstance = gameInstance;
        }

        public NoVersionWarningSourceRegistry Registry => _registry;

        public long? EffectiveTimestamp
        {
            get
            {
                var timestamps = SourceTimestamps;
                long max = 0;
                foreach (var source in _registry.GetEnabledSources())
                {
                    if (timestamps.TryGetValue(source.Id, out var ts) && ts > max)
                    {
                        max = ts;
                    }
                }
                return max > 0 ? max : null;
            }
        }

        public IReadOnlyDictionary<string, long> SourceTimestamps
        {
            get
            {
                var dict = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (var source in _registry.Sources)
                {
                    var ts = ResolveSourceTimestamp(source);
                    if (ts > 0)
                    {
                        dict[source.Id] = ts;
                    }
                }
                return dict;
            }
        }

        private long ResolveSourceTimestamp(ModRuleSourceRecord source)
        {
            if (source.Type == ModRuleSourceType.Mod)
            {
                var modFolder = ResolveModFolderPath(source.ModFolderId ?? NoVersionWarningSourceRegistry.ModFolderId);
                return GetModTimestamp(modFolder);
            }

            if (source.Type == ModRuleSourceType.LocalFile)
            {
                return GetDirectoryOrFileTimestamp(source.LocalPath);
            }

            // Remote repo tree snapshot
            var snapshotFile = _dataUpdateService.GetExternalFilePath("Versions", source.Id, "ModIdsToFix.xml");
            var snapshotRoot = Path.GetDirectoryName(snapshotFile);
            if ((string.IsNullOrEmpty(snapshotRoot) || !Directory.Exists(snapshotRoot)) &&
                (source.Id == "community-noversionwarning" || source.Id == "NoVersionWarning"))
            {
                var altFile = _dataUpdateService.GetExternalFilePath("Versions", "NoVersionWarning", "ModIdsToFix.xml");
                var altRoot = Path.GetDirectoryName(altFile);
                if (!string.IsNullOrEmpty(altRoot) && Directory.Exists(altRoot)) snapshotRoot = altRoot;
            }

            return GetDirectoryOrFileTimestamp(snapshotRoot);
        }

        private static long GetModTimestamp(string? modRootFolder)
        {
            if (string.IsNullOrWhiteSpace(modRootFolder) || !Directory.Exists(modRootFolder))
            {
                return 0;
            }

            // 1. Check About/timestamp.txt (written by RimSharp's download processor: dd/MM/yyyy HH:mm:ss)
            var aboutFolder = Path.Combine(modRootFolder, "About");
            var timestampFile = Path.Combine(aboutFolder, "timestamp.txt");
            if (File.Exists(timestampFile))
            {
                try
                {
                    var raw = File.ReadAllText(timestampFile).Trim();
                    if (DateTime.TryParseExact(raw, "dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
                    {
                        return new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds();
                    }
                }
                catch { }
            }

            // 2. Check DateStamp in mod root (written by RimSharp / Steam downloader)
            var dateStampFile = Path.Combine(modRootFolder, "DateStamp");
            if (File.Exists(dateStampFile))
            {
                try
                {
                    var raw = File.ReadAllText(dateStampFile).Trim();
                    var cleaned = System.Text.RegularExpressions.Regex.Replace(raw.Replace("@", "").Trim(), @"\s+", " ");
                    cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\b(am|pm)\b", m => m.Value.ToUpperInvariant());
                    string[] formats = { "dd MMM, yyyy h:mmtt", "dd MMM, yyyy hh:mmtt", "d MMM, yyyy h:mmtt" };
                    if (DateTime.TryParseExact(cleaned, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
                    {
                        return new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds();
                    }
                }
                catch { }
            }

            // 3. Fallback to About.xml or newest XML/subfolder LastWriteTimeUtc
            var aboutXml = Path.Combine(aboutFolder, "About.xml");
            if (File.Exists(aboutXml))
            {
                return new DateTimeOffset(File.GetLastWriteTimeUtc(aboutXml)).ToUnixTimeSeconds();
            }

            return GetDirectoryOrFileTimestamp(modRootFolder);
        }

        private static long GetDirectoryOrFileTimestamp(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return 0;

            if (File.Exists(path))
            {
                return new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();
            }

            if (Directory.Exists(path))
            {
                try
                {
                    var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        var newest = files.Select(File.GetLastWriteTimeUtc).Max();
                        return new DateTimeOffset(newest).ToUnixTimeSeconds();
                    }
                }
                catch { }

                return new DateTimeOffset(Directory.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();
            }

            return 0;
        }

        public void CommitRegistryChanges()
        {
            _registry.Save();
            _mlieVersionService.Invalidate();
            _logger.LogInfo("NoVersionWarning source registry changed; invalidated version cache.", nameof(NoVersionWarningSourceManager));
            if (_modService != null)
            {
                _ = ReapplyAsync(nameof(CommitRegistryChanges));
            }
        }

        public async Task RefreshAllAsync(CancellationToken cancellationToken = default)
        {
            await _dataUpdateService.RefreshAllNoVersionWarningSourcesAsync(cancellationToken).ConfigureAwait(false);
            _mlieVersionService.Invalidate();
            if (_modService != null)
            {
                await _modService.ReapplyRulesAsync().ConfigureAwait(false);
            }
        }

        public async Task RefreshSourceAsync(string sourceId, CancellationToken cancellationToken = default)
        {
            await _dataUpdateService.RefreshNoVersionWarningSourceAsync(sourceId, cancellationToken).ConfigureAwait(false);
            _mlieVersionService.Invalidate();
            if (_modService != null)
            {
                await _modService.ReapplyRulesAsync().ConfigureAwait(false);
            }
        }

        public bool IsModInstalled(string? modFolderId)
        {
            return !string.IsNullOrEmpty(ResolveModFolderPath(modFolderId));
        }

        private string? ResolveModFolderPath(string? modFolderId)
        {
            if (string.IsNullOrWhiteSpace(modFolderId)) return null;

            var modsPath = _pathService.GetModsPath();
            if (!string.IsNullOrEmpty(modsPath))
            {
                var candidate = Path.Combine(modsPath, modFolderId);
                if (Directory.Exists(candidate)) return candidate;
            }

            if (_gameInstance != null)
            {
                foreach (var searchPath in _gameInstance.GetModSearchPaths())
                {
                    var candidate = Path.Combine(searchPath, modFolderId);
                    if (Directory.Exists(candidate)) return candidate;
                }
            }

            return null;
        }

        private async Task ReapplyAsync(string caller)
        {
            try
            {
                if (_modService != null)
                {
                    await _modService.ReapplyRulesAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, $"Version re-merge after {caller} failed.", nameof(NoVersionWarningSourceManager));
            }
        }
    }
}
