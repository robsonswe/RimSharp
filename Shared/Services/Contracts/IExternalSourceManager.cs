#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Common contract for external data source managers (Rules, ModInfo, Replacements,
    /// NoVersionWarning). Manages the source registry commit path, manual refreshes,
    /// and checking if local mods are installed.
    /// </summary>
    public interface IExternalSourceManager
    {
        /// <summary>
        /// Newest upstream timestamp across the enabled sources ("rules as of"),
        /// or null when no source provides one.
        /// </summary>
        long? EffectiveTimestamp => null;

        /// <summary>
        /// Per-source upstream timestamp (unix seconds) for every source that
        /// provides one, keyed by source Id.
        /// </summary>
        IReadOnlyDictionary<string, long> SourceTimestamps => new Dictionary<string, long>();

        /// <summary>
        /// Persists the current registry state and invalidates caches / re-applies data.
        /// </summary>
        void CommitRegistryChanges();

        /// <summary>
        /// Fetches every enabled remote source, then invalidates caches.
        /// </summary>
        Task RefreshAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Fetches one source, then invalidates caches.
        /// </summary>
        Task RefreshSourceAsync(string sourceId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks whether a mod with the given folder/workshop ID is installed on disk.
        /// </summary>
        bool IsModInstalled(string? modFolderId) => false;
    }
}
