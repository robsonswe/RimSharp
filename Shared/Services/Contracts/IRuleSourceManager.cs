using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Infrastructure.Mods.Rules;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Coordinates the rule database source registry: persists registry edits,
    /// triggers source refreshes and performs the live re-merge (cache
    /// invalidation + re-application onto the loaded mods) so registry changes
    /// take effect without an app restart.
    /// </summary>
    public interface IRuleSourceManager
    {
        /// <summary>The shared, priority-ordered source registry.</summary>
        ModRuleSourceRegistry Registry { get; }

        /// <summary>
        /// Newest upstream timestamp across the enabled sources ("rules as of"),
        /// or null when no source provides one.
        /// </summary>
        long? EffectiveTimestamp { get; }

        /// <summary>
        /// Per-source upstream timestamp (unix seconds) for every source that
        /// provides one, keyed by source Id. Delegates to the repository.
        /// </summary>
        IReadOnlyDictionary<string, long> SourceTimestamps { get; }

        /// <summary>
        /// Persists the current registry state and re-merges the rules onto the
        /// loaded mods (a removed/disabled source's contributions disappear).
        /// </summary>
        void CommitRegistryChanges();

        /// <summary>
        /// Fetches every enabled repoFile source, then re-merges.
        /// </summary>
        Task RefreshAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Fetches one source, then re-merges.
        /// </summary>
        Task RefreshSourceAsync(string sourceId, CancellationToken cancellationToken = default);
    }
}