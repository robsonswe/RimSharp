#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.ModInfo
{
    /// <summary>
    /// Dictionary-driven packageId → Steam Workshop resolver.
    ///
    /// Layering: this lives in Infrastructure/Mods/ModInfo so both the shared
    /// dictionary service and the workshop-downloader Steam API client are
    /// reachable. The app is a single assembly and Infrastructure already
    /// references Features.WorkshopDownloader.Services elsewhere (e.g.
    /// ModListIOService), so this introduces no new dependency edge or cycle.
    /// </summary>
    public sealed class ModInfoResolver : IModInfoResolver
    {
        // Steam's GetPublishedFileDetails endpoint accepts at most 300 ids per call.
        private const int MaxBatchSize = 300;

        private readonly IModDictionaryService _dictionary;
        private readonly ISteamApiClient _steamApiClient;

        public ModInfoResolver(
            IModDictionaryService dictionary,
            ISteamApiClient steamApiClient)
        {
            _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
            _steamApiClient = steamApiClient ?? throw new ArgumentNullException(nameof(steamApiClient));
        }

        public async Task<IReadOnlyDictionary<string, IReadOnlyList<ModInfoResolutionCandidate>>> ResolveAsync(
            IEnumerable<string> packageIds,
            IEnumerable<string>? excludePackageIds = null,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, IReadOnlyList<ModInfoResolutionCandidate>>(StringComparer.OrdinalIgnoreCase);
            if (packageIds == null) return result;

            var excluded = new HashSet<string>(
                excludePackageIds?.Where(p => !string.IsNullOrWhiteSpace(p)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            // Normalize the input: drop blanks, exclusions and case-insensitive dupes.
            var requested = new List<string>();
            var seenPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var packageId in packageIds)
            {
                if (string.IsNullOrWhiteSpace(packageId)) continue;
                if (excluded.Contains(packageId)) continue;
                if (!seenPackages.Add(packageId)) continue;
                requested.Add(packageId);
            }

            if (requested.Count == 0) return result;

            // Collect candidate pfids. GetEntryByPackageId carries the existing
            // version → published → source-priority policy, so its result is
            // placed first; the remaining GetAllEntriesByPackageId candidates
            // follow in their natural order.
            var pfids = new List<string>();
            var seenPfids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidatesByPackage = new Dictionary<string, List<ModDictionaryEntry>>(StringComparer.OrdinalIgnoreCase);

            foreach (var packageId in requested)
            {
                var allEntries = _dictionary.GetAllEntriesByPackageId(packageId) ?? new List<ModDictionaryEntry>();
                var best = _dictionary.GetEntryByPackageId(packageId);

                var ordered = new List<ModDictionaryEntry>();
                if (best != null && !string.IsNullOrWhiteSpace(best.SteamId))
                {
                    ordered.Add(best);
                }

                foreach (var entry in allEntries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.SteamId)) continue;
                    if (best != null && string.Equals(entry.SteamId, best.SteamId, StringComparison.OrdinalIgnoreCase)) continue;
                    ordered.Add(entry);
                }

                // Dedup steam ids case-insensitively within this package.
                var seenHere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var deduped = ordered.Where(e => seenHere.Add(e.SteamId)).ToList();
                candidatesByPackage[packageId] = deduped;

                foreach (var entry in deduped)
                {
                    if (seenPfids.Add(entry.SteamId)) pfids.Add(entry.SteamId);
                }
            }

            // Enrich all pfids in one batched call per chunk (Steam limit 300).
            // No pfids means no network call at all.
            var apiDetails = new Dictionary<string, SteamPublishedFileDetails>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pfids.Count; i += MaxBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = pfids.Skip(i).Take(MaxBatchSize).ToList();
                var response = await _steamApiClient
                    .GetPublishedFileDetailsBatchAsync(chunk, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var detail in response?.Response?.PublishedFileDetails ?? Enumerable.Empty<SteamPublishedFileDetails>())
                {
                    if (detail == null || string.IsNullOrWhiteSpace(detail.PublishedFileId)) continue;
                    if (!apiDetails.ContainsKey(detail.PublishedFileId))
                    {
                        apiDetails[detail.PublishedFileId] = detail;
                    }
                }
            }

            // Persona names are resolved once per distinct creator across the
            // whole call. Failures are swallowed so author enrichment can never
            // break resolution.
            var personaCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var packageId in requested)
            {
                var candidates = new List<ModInfoResolutionCandidate>();
                foreach (var entry in candidatesByPackage[packageId])
                {
                    apiDetails.TryGetValue(entry.SteamId, out var detail);
                    var name = !string.IsNullOrWhiteSpace(detail?.Title) ? detail!.Title
                        : !string.IsNullOrWhiteSpace(entry.SteamName) ? entry.SteamName
                        : !string.IsNullOrWhiteSpace(entry.AboutName) ? entry.AboutName
                        : !string.IsNullOrWhiteSpace(entry.Name) ? entry.Name
                        : packageId;

                    string? author = null;
                    if (detail != null
                        && detail.Result == (int)SteamApiResultCode.OK
                        && !string.IsNullOrWhiteSpace(detail.Creator)
                        && string.IsNullOrWhiteSpace(entry.Authors))
                    {
                        if (!personaCache.TryGetValue(detail.Creator, out author))
                        {
                            author = null;
                            try
                            {
                                author = await _steamApiClient
                                    .GetPersonaNameAsync(detail.Creator, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                            catch (Exception)
                            {
                                // Best-effort: an unavailable persona endpoint
                                // simply leaves the author unresolved.
                                author = null;
                            }
                            personaCache[detail.Creator] = author;
                        }
                    }

                    candidates.Add(new ModInfoResolutionCandidate
                    {
                        SteamId = entry.SteamId,
                        DisplayName = name,
                        Published = entry.Published,
                        ApiDetails = detail,
                        DictionaryEntry = entry,
                        Author = author
                    });
                }

                result[packageId] = candidates;
            }

            return result;
        }
    }
}
