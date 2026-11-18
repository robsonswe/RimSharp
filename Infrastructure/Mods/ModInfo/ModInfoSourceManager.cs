#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Infrastructure.Data;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.ModInfo
{
    public class ModInfoSourceManager : IModInfoSourceManager
    {
        private readonly ModInfoSourceRegistry _registry;
        private readonly IModDictionaryService _dictionaryService;
        private readonly IDataUpdateService _dataUpdateService;
        private readonly ILoggerService _logger;

        public ModInfoSourceManager(
            ModInfoSourceRegistry registry,
            IModDictionaryService dictionaryService,
            IDataUpdateService dataUpdateService,
            ILoggerService logger)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _dictionaryService = dictionaryService ?? throw new ArgumentNullException(nameof(dictionaryService));
            _dataUpdateService = dataUpdateService ?? throw new ArgumentNullException(nameof(dataUpdateService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ModInfoSourceRegistry Registry => _registry;

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
                    var path = ResolveSourcePath(source);
                    var ts = GetTimestamp(path);
                    if (ts > 0)
                    {
                        dict[source.Id] = ts;
                    }
                }
                return dict;
            }
        }

        private string? ResolveSourcePath(ModRuleSourceRecord source)
        {
            if (source.Type == ModRuleSourceType.LocalFile)
            {
                return source.LocalPath;
            }

            if (string.Equals(source.Id, ModInfoSourceRegistry.CuratedSourceId, StringComparison.OrdinalIgnoreCase))
            {
                return _dataUpdateService.GetExternalFilePath("ModInfo", "RimSharp", "db.json");
            }

            var fileName = GetFileNameFromUrl(source.Url) ?? "db.json";
            return _dataUpdateService.GetExternalFilePath("ModInfo", source.Id, fileName);
        }

        private static long GetTimestamp(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return 0;

            if (File.Exists(path))
            {
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
                    // Fall back to file write time
                }

                return new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();
            }

            return 0;
        }

        private static string? GetFileNameFromUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

            var fileName = Path.GetFileName(uri.AbsolutePath);
            return string.IsNullOrEmpty(fileName) ? null : Uri.UnescapeDataString(fileName);
        }

        public void CommitRegistryChanges()
        {
            _registry.Save();
            _dictionaryService.Invalidate();
            _logger.LogInfo("ModInfo source registry changed; invalidated dictionary cache.", nameof(ModInfoSourceManager));
        }

        public async Task RefreshAllAsync(CancellationToken cancellationToken = default)
        {
            await _dataUpdateService.RefreshAllModInfoSourcesAsync(cancellationToken).ConfigureAwait(false);
            _dictionaryService.Invalidate();
        }

        public async Task RefreshSourceAsync(string sourceId, CancellationToken cancellationToken = default)
        {
            await _dataUpdateService.RefreshModInfoSourceAsync(sourceId, cancellationToken).ConfigureAwait(false);
            _dictionaryService.Invalidate();
        }

        public bool IsModInstalled(string? modFolderId) => false;
    }
}
