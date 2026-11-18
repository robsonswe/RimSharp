#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Shared.Models;

namespace RimSharp.Infrastructure.Mods.ModInfo
{
    /// <summary>
    /// Resolves packageId-only mod references into ordered Steam Workshop
    /// candidates. Dictionary entries supply the candidate published-file ids;
    /// the Steam API enriches them with live titles. A packageId with no
    /// dictionary candidates resolves to an empty list (unknown, not "does not
    /// exist" — the database can go stale), so callers keep their existing
    /// behavior for that case.
    /// </summary>
    public interface IModInfoResolver
    {
        /// <summary>
        /// Resolves a batch of package ids in one pass. All candidate pfids are
        /// enriched through a single batched Steam API call (chunked at the
        /// endpoint's 300-id limit).
        /// </summary>
        /// <param name="packageIds">Package ids to resolve. Duplicates (case-insensitive) are ignored.</param>
        /// <param name="excludePackageIds">
        /// Package ids already installed locally. They are omitted from the
        /// result so the caller can resolve them from its own installed mods.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// A case-insensitive map with one entry per requested (non-excluded)
        /// package id. The value is the ordered candidate list, empty when the
        /// dictionary has no candidates for that package id.
        /// </returns>
        Task<IReadOnlyDictionary<string, IReadOnlyList<ModInfoResolutionCandidate>>> ResolveAsync(
            IEnumerable<string> packageIds,
            IEnumerable<string>? excludePackageIds = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One resolved Steam Workshop candidate for a package id.
    /// </summary>
    public sealed class ModInfoResolutionCandidate
    {
        /// <summary>Candidate Steam Workshop published-file id.</summary>
        public string SteamId { get; init; } = string.Empty;

        /// <summary>
        /// Best available display name: live Steam API title, then the
        /// dictionary's SteamName / AboutName, then the package id.
        /// </summary>
        public string DisplayName { get; init; } = string.Empty;

        /// <summary>Published flag from the dictionary entry.</summary>
        public bool Published { get; init; }

        /// <summary>Raw Steam API details when the API returned this pfid.</summary>
        public SteamPublishedFileDetails? ApiDetails { get; init; }

        /// <summary>The dictionary entry this candidate came from.</summary>
        public ModDictionaryEntry DictionaryEntry { get; init; } = new();
    }
}
