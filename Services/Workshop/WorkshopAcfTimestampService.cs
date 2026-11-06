#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RimSharp.Infrastructure.Workshop;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Services.Workshop
{
    /// <summary>
    /// Applies locally-recorded Workshop item timestamps onto scanned mods.
    ///
    /// Source of truth is the workshop ACF manifest (appworkshop_294100.acf) next
    /// to the workshop content folder — one layout covers both mod kinds:
    ///   - Steam client: <steamapps>\workshop\content\294100  →  ..\appworkshop_294100.acf
    ///   - SteamCMD:     <prefix>\steam\steamapps\workshop\content\294100  →  ..\appworkshop_294100.acf
    ///
    /// The parsed map is kept in a small JSON cache (workshopacf_cache.json) keyed
    /// by the source ACF path and its LastWriteTimeUtc, so repeated scans don't
    /// re-parse the (potentially large) manifest. Any change to the ACF file
    /// invalidates the cache; a manifest that can't be parsed yields empty data.
    /// </summary>
    public class WorkshopAcfTimestampService : IWorkshopAcfTimestampService
    {
        private readonly IAcfFileParser _acfParser;
        private readonly ILoggerService _logger;
        private readonly string _cacheFilePath;

        public WorkshopAcfTimestampService(
            IAcfFileParser acfParser,
            ILoggerService logger,
            string? cacheDirectory = null)
        {
            _acfParser = acfParser ?? throw new ArgumentNullException(nameof(acfParser));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheFilePath = Path.Combine(cacheDirectory ?? AppContext.BaseDirectory, CacheFileName);
        }

        public const string CacheFileName = "workshopacf_cache.json";

        // ------------------------------------------------------------------ //
        //  Public surface                                                     //
        // ------------------------------------------------------------------ //

        public IReadOnlyDictionary<string, long> GetTimestamps(string? workshopFolder)
        {
            var acfPath = ResolveAcfPath(workshopFolder);
            if (acfPath == null)
            {
                return new Dictionary<string, long>();
            }

            if (!File.Exists(acfPath))
            {
                // Normal for non-Steam setups — not a warning.
                _logger.LogDebug($"No workshop ACF manifest at '{acfPath}'.", nameof(WorkshopAcfTimestampService));
                return new Dictionary<string, long>();
            }

            var cache = LoadCache();
            var currentAcfMtimeTicks = File.GetLastWriteTimeUtc(acfPath).Ticks;
            if (cache != null &&
                string.Equals(cache.AcfPath, acfPath, StringComparison.OrdinalIgnoreCase) &&
                cache.AcfLastWriteUtcTicks == currentAcfMtimeTicks &&
                cache.Items is { Count: > 0 })
            {
                return cache.Items;
            }

            if (!_acfParser.TryParse(acfPath, out var data, out var error) ||
                data.Items.Count == 0)
            {
                _logger.LogWarning(
                    $"Workshop ACF manifest could not be used: {error ?? "no items found"}.",
                    nameof(WorkshopAcfTimestampService));
                SaveCache(cache, acfPath, empty: true); // never reuse it next scan
                return new Dictionary<string, long>();
            }

            var stamps = data.Items.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ResolvedTimeUpdated,
                StringComparer.Ordinal);

            SaveCache(cache, acfPath, empty: false, stamps);
            _logger.LogDebug(
                $"Parsed {stamps.Count} workshop item timestamps from '{acfPath}'.",
                nameof(WorkshopAcfTimestampService));

            return stamps;
        }

        public void ApplyToMods(IEnumerable<ModItem> mods, string? workshopFolder)
        {
            if (mods == null)
            {
                return;
            }

            var stamps = GetTimestamps(workshopFolder);
            var applied = 0;

            foreach (var mod in mods)
            {
                if (string.IsNullOrEmpty(mod.SteamId))
                {
                    continue; // Non-workshop mods keep null — no Steam record to consult
                }

                if (mod.SteamId.Length > 18 || !long.TryParse(mod.SteamId, out _))
                {
                    _logger.LogDebug(
                        $"Skipping ACF timestamp lookup for non-numeric SteamId '{mod.SteamId}'.",
                        nameof(WorkshopAcfTimestampService));
                    mod.AcfTimeUpdated = null;
                    continue;
                }

                mod.AcfTimeUpdated =
                    stamps.TryGetValue(mod.SteamId, out var stamp) && stamp > 0
                        ? stamp
                        : null;
                applied++;
            }

            _logger.LogDebug(
                $"ACF timestamps applied to {applied} Steam-carrying mods " +
                $"({stamps.Count} manifest records, workshop folder '{workshopFolder ?? "(none)"}').",
                nameof(WorkshopAcfTimestampService));
        }

        // ------------------------------------------------------------------ //
        //  Layout + cache plumbing                                            //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// appworkshop_294100.acf sits two levels above the workshop content folder
        /// (workshop\content\294100 → workshop\appworkshop_294100.acf) in both the
        /// Steam client and SteamCMD layouts.
        /// </summary>
        private static string? ResolveAcfPath(string? workshopFolder)
        {
            if (string.IsNullOrWhiteSpace(workshopFolder))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(Path.Combine(workshopFolder, "..", "..", "appworkshop_294100.acf"));
            }
            catch (Exception)
            {
                return null; // Malformed / rooted-relative path — treat as "no manifest"
            }
        }

        /// <summary>DTO persisted as a single JSON file next to the app config.</summary>
        private sealed class AcfCacheDto
        {
            public string? AcfPath { get; set; }
            /// <summary>LastWriteTimeUtc of the source ACF, stored as .NET ticks to
            /// round-trip exactly (no culture/timezone parsing involved).</summary>
            public long AcfLastWriteUtcTicks { get; set; }
            public Dictionary<string, long> Items { get; set; } = new();
        }

        private AcfCacheDto? LoadCache()
        {
            try
            {
                if (!File.Exists(_cacheFilePath))
                {
                    return null;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(_cacheFilePath));
                var root = doc.RootElement;
                var dto = new AcfCacheDto();

                if (root.TryGetProperty("AcfPath", out var acfPathNode) && acfPathNode.ValueKind == JsonValueKind.String)
                {
                    dto.AcfPath = acfPathNode.GetString();
                }
                if (root.TryGetProperty("AcfLastWriteUtcTicks", out var stampNode) &&
                    stampNode.ValueKind == JsonValueKind.Number &&
                    stampNode.TryGetInt64(out var stampTicks))
                {
                    dto.AcfLastWriteUtcTicks = stampTicks;
                }
                if (root.TryGetProperty("Items", out var itemsNode) && itemsNode.ValueKind == JsonValueKind.Object)
                {
                    foreach (var item in itemsNode.EnumerateObject())
                    {
                        if (item.Value.ValueKind == JsonValueKind.Number && item.Value.TryGetInt64(out var value))
                        {
                            dto.Items[item.Name] = value;
                        }
                    }
                }

                return dto;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Discarding unreadable ACF timestamp cache: {ex.Message}", nameof(WorkshopAcfTimestampService));
                return null;
            }
        }

        private void SaveCache(AcfCacheDto? previous, string acfPath, bool empty, IReadOnlyDictionary<string, long>? stamps = null)
        {
            try
            {
                var dto = new AcfCacheDto
                {
                    AcfPath = acfPath,
                    AcfLastWriteUtcTicks = File.GetLastWriteTimeUtc(acfPath).Ticks,
                    Items = empty ? new Dictionary<string, long>() : new Dictionary<string, long>(stamps ?? new Dictionary<string, long>())
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_cacheFilePath, JsonSerializer.Serialize(dto, options));
            }
            catch (Exception ex)
            {
                // Cache write failure is cosmetic (next scan just re-parses); the
                // previous cache, if any, becomes invalid only on ACF change anyway.
                if (previous != null)
                {
                    _logger.LogDebug($"Could not persist ACF timestamp cache: {ex.Message}", nameof(WorkshopAcfTimestampService));
                }
                else
                {
                    _logger.LogWarning($"Could not persist ACF timestamp cache: {ex.Message}", nameof(WorkshopAcfTimestampService));
                }
            }
        }
    }
}
