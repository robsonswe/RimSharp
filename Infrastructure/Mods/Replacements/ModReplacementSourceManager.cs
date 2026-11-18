#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Infrastructure.Data;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.Replacements
{
    public class ModReplacementSourceManager : IModReplacementSourceManager
    {
        private readonly ModReplacementSourceRegistry _registry;
        private readonly IModReplacementService _replacementService;
        private readonly IDataUpdateService _dataUpdateService;
        private readonly IPathService _pathService;
        private readonly GameInstance? _gameInstance;
        private readonly ILoggerService _logger;

        public ModReplacementSourceManager(
            ModReplacementSourceRegistry registry,
            IModReplacementService replacementService,
            IDataUpdateService dataUpdateService,
            IPathService pathService,
            ILoggerService logger,
            GameInstance? gameInstance = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _replacementService = replacementService ?? throw new ArgumentNullException(nameof(replacementService));
            _dataUpdateService = dataUpdateService ?? throw new ArgumentNullException(nameof(dataUpdateService));
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gameInstance = gameInstance;
        }

        public ModReplacementSourceRegistry Registry => _registry;

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
                var modFolder = ResolveModFolderPath(source.ModFolderId ?? ModReplacementSourceRegistry.ModFolderId);
                return GetModTimestamp(modFolder);
            }

            if (source.Type == ModRuleSourceType.LocalFile)
            {
                return GetFileTimestamp(source.LocalPath);
            }

            if (string.Equals(source.Id, ModReplacementSourceRegistry.CuratedSourceId, StringComparison.OrdinalIgnoreCase))
            {
                var curatedPath = _dataUpdateService.GetExternalFilePath("Replacements", "RimSharp", "replacements.json");
                return GetFileTimestamp(curatedPath);
            }

            // Remote repo snapshot
            var snapshotPath = _dataUpdateService.GetExternalFilePath("Replacements", source.Id, "replacements.json");
            if (!File.Exists(snapshotPath) && (source.Id == "community-replacements" || source.Id == "UseThisInstead"))
            {
                var alt = _dataUpdateService.GetExternalFilePath("Replacements", "UseThisInstead", "replacements.json");
                if (File.Exists(alt)) snapshotPath = alt;
            }

            return GetFileTimestamp(snapshotPath);
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
                    if (DateTime.TryParseExact(raw, "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
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
                    var cleaned = Regex.Replace(raw.Replace("@", "").Trim(), @"\s+", " ");
                    cleaned = Regex.Replace(cleaned, @"\b(am|pm)\b", m => m.Value.ToUpperInvariant());
                    string[] formats = { "dd MMM, yyyy h:mmtt", "dd MMM, yyyy hh:mmtt", "d MMM, yyyy h:mmtt" };
                    if (DateTime.TryParseExact(cleaned, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    {
                        return new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds();
                    }
                }
                catch { }
            }

            // 3. Fallback to replacements.json / About.xml / folder LastWriteTimeUtc
            var replacementsFile = Path.Combine(modRootFolder, "replacements.json");
            if (File.Exists(replacementsFile))
            {
                return new DateTimeOffset(File.GetLastWriteTimeUtc(replacementsFile)).ToUnixTimeSeconds();
            }

            var aboutXml = Path.Combine(aboutFolder, "About.xml");
            if (File.Exists(aboutXml))
            {
                return new DateTimeOffset(File.GetLastWriteTimeUtc(aboutXml)).ToUnixTimeSeconds();
            }

            return new DateTimeOffset(Directory.GetLastWriteTimeUtc(modRootFolder)).ToUnixTimeSeconds();
        }

        private static long GetFileTimestamp(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return 0;

            try
            {
                using var stream = File.OpenRead(path);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("timestamp", out var tsProp) && tsProp.TryGetInt64(out var ts) && ts > 0)
                {
                    return ts;
                }
            }
            catch
            {
                // Not JSON or no embedded timestamp; fallback to file write time
            }

            return new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();
        }

        public void CommitRegistryChanges()
        {
            _registry.Save();
            _replacementService.Invalidate();
            _logger.LogInfo("Mod replacement source registry changed; invalidated replacement cache.", nameof(ModReplacementSourceManager));
        }

        public async Task RefreshAllAsync(CancellationToken cancellationToken = default)
        {
            await _dataUpdateService.RefreshAllReplacementSourcesAsync(cancellationToken).ConfigureAwait(false);
            _replacementService.Invalidate();
        }

        public async Task RefreshSourceAsync(string sourceId, CancellationToken cancellationToken = default)
        {
            await _dataUpdateService.RefreshReplacementSourceAsync(sourceId, cancellationToken).ConfigureAwait(false);
            _replacementService.Invalidate();
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
    }
}
