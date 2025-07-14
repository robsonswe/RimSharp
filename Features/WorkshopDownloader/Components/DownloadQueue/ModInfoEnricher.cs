#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts; // For IModListManager

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
            // Initial population can happen here or lazily on first use
            // RefreshLocalModLookup();
        }

        /// <summary>
        /// Refreshes the internal lookup dictionary of local mods by their Steam ID.
        /// Should be called when the local mod list might have changed.
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
                _localModLookupBySteamId.Clear(); // Ensure lookup is empty on error
            }
        }

        /// <summary>
        /// Enriches a single DownloadItem with information from the local mod lookup.
        /// </summary>
        /// <param name="item">The DownloadItem to enrich.</param>
        public void EnrichDownloadItem(DownloadItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.SteamId)) return;

            // Ensure the lookup is populated if it hasn't been already
            if (!_localModLookupBySteamId.Any())
            {
                 RefreshLocalModLookup();
            }

            if (_localModLookupBySteamId.TryGetValue(item.SteamId, out var localMod))
            {
                // Debug.WriteLine($"[ModInfoEnricher] Enriching item '{item.Name}' ({item.SteamId}): Found local match.");
                item.IsInstalled = true;
                item.LocalDateStamp = localMod.DateStamp;
                item.IsActive = _modListManager.IsModActive(localMod);
                item.IsLocallyOutdatedRW = localMod.IsOutdatedRW;
                item.InstalledVersions = localMod.SupportedVersions;
                item.IsFavorite = localMod.IsFavorite;
            }
            else
            {
                // Debug.WriteLine($"[ModInfoEnricher] Enriching item '{item.Name}' ({item.SteamId}): No local match found.");
                item.ClearLocalInfo(); // Use helper to reset properties
            }
        }

        /// <summary>
        /// Refreshes the local mod lookup and enriches all items in the provided collection.
        /// </summary>
        /// <param name="items">The collection of DownloadItems to enrich.</param>
        public void EnrichAllDownloadItems(IEnumerable<DownloadItem> items)
        {
            RefreshLocalModLookup(); // Ensure lookup is fresh before enriching all
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