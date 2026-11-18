using System;
using System.Threading;
using System.Threading.Tasks;

public interface IDataUpdateService
{
    /// <summary>

    /// </summary>
    Task CheckForAndApplyUpdatesAsync(IProgress<DataUpdateProgress> progress, CancellationToken cancellationToken);

    /// <summary>
    /// Path of an externally synced source snapshot, e.g.
    /// GetExternalFilePath("Replacements", "UseThisInstead", "replacements.json")
    /// -> <DataDirectory>/Data/Replacements/UseThisInstead/replacements.json.
    /// </summary>
    string GetExternalFilePath(string category, string source, string fileName);

    /// <summary>
    /// Syncs every enabled repoFile rule database source from the registry
    /// (conditional GET with ETag/304). The curated RimSharpDB entry is
    /// skipped — the SHA-gated curated sync owns its file — and localFile
    /// sources are never fetched. Each source's failure is isolated.
    /// </summary>
    Task SyncRuleSourcesAsync(IProgress<DataUpdateProgress>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches and validates a single new rule source before it is added to the
    /// registry. Throws <see cref="InvalidOperationException"/> with a friendly
    /// message when the download fails or its content is invalid.
    /// </summary>
    Task SyncNewRuleSourceAsync(string sourceId, string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches and validates a single new ModInfo source before it is added to
    /// the registry. Throws on download or validation failure.
    /// </summary>
    Task SyncNewModInfoSourceAsync(string sourceId, string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches and validates a single new replacement source before it is added
    /// to the registry. Honors the .gz decompression path. Throws on download
    /// or validation failure.
    /// </summary>
    Task SyncNewReplacementSourceAsync(string sourceId, string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches and validates a single new NoVersionWarning source (its
    /// per-version ModIdsToFix.xml tree) before it is added to the registry.
    /// Throws on download or validation failure.
    /// </summary>
    Task SyncNewNoVersionWarningSourceAsync(string sourceId, string url, CancellationToken cancellationToken = default);
}
