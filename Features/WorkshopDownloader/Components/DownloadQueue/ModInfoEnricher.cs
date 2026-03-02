#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.WorkshopDownloader.Components.DownloadQueue
{
    /// <summary>
    /// Helper class responsible for enriching DownloadItem instances
    /// with information about corresponding locally installed mods.
    /// </summary>
    public class ModInfoEnricher
    {
        private readonly IModListManager _modListManager;
        private Dictionary<string, ModItem> _localModLookupBySteamId = new();

        public ModInfoEnricher(IModListManager modListManager)
        {
            _modListManager = modListManager ?? throw new ArgumentNullException(nameof(modListManager));

            // RefreshLocalModLookup();
        }

        /// <summary>

/// </summary>
        public void RefreshLocalModLookup()
        {
            try
            {
                var allMods = _modListManager.GetAllMods();
                Debug.WriteLine($"[ModInfoEnricher] Refreshing local mod lookup. Found {allMods.Count()} total mods reported by manager.");

                _localModLookupBySteamId = allMods
                    .Where(m => !string.IsNullOrEmpty(m.SteamId))
                    .GroupBy(m => m.SteamId, StringComparer.OrdinalIgnoreCase) // Group to handle potential duplicates (take first)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                Debug.WriteLine($"[ModInfoEnricher] Refreshed local mod lookup, stored {_localModLookupBySteamId.Count} mods with SteamIDs.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModInfoEnricher] Error refreshing local mod lookup: {ex.Message}");
                _localModLookupBySteamId.Clear();
            }
        }

        /// <summary>

        /// </summary>
        /// <param name="item">The DownloadItem to enrich.</param>
        public void EnrichDownloadItem(DownloadItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.SteamId)) return;
            if (!_localModLookupBySteamId.Any())
            {
                 RefreshLocalModLookup();
            }

            if (_localModLookupBySteamId.TryGetValue(item.SteamId, out var localMod))
            {
                Debug.WriteLine($"[ModInfoEnricher] Found local match for '{item.Name}' ({item.SteamId})");
                Debug.WriteLine($"  - IsInstalled: {item.IsInstalled} -> true");
                Debug.WriteLine($"  - IsActive: {item.IsActive} -> {_modListManager.IsModActive(localMod)}");
                Debug.WriteLine($"  - IsFavorite: {item.IsFavorite} -> {localMod.IsFavorite}");

                item.IsInstalled = true;
                item.LocalDateStamp = localMod.DateStamp;
                item.IsActive = _modListManager.IsModActive(localMod);
                item.IsLocallySupportedRW = localMod.IsSupportedRW;
                item.InstalledVersions = localMod.SupportedVersions;
                item.IsFavorite = localMod.IsFavorite;

                Debug.WriteLine($"  - After set: IsInstalled={item.IsInstalled}, IsActive={item.IsActive}, IsFavorite={item.IsFavorite}");
            }
            else
            {
                Debug.WriteLine($"[ModInfoEnricher] No local match for '{item.Name}' ({item.SteamId}). Available IDs: {string.Join(", ", _localModLookupBySteamId.Keys.Take(5))}...");
                item.ClearLocalInfo();
            }
        }

        /// <summary>

        /// </summary>
        /// <param name="items">The collection of DownloadItems to enrich.</param>
        public void EnrichAllDownloadItems(IEnumerable<DownloadItem> items)
        {
            RefreshLocalModLookup();
            if (items == null) return;

            Debug.WriteLine($"[ModInfoEnricher] Enriching all {items.Count()} download items using refreshed lookup...");
            foreach (var item in items)
            {
                EnrichDownloadItem(item);
            }
            Debug.WriteLine($"[ModInfoEnricher] Enrichment complete.");
        }
    }
}


