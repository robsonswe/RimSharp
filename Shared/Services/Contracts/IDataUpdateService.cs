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
    /// Manual refresh of a single rule source (settings "Update now").
    /// </summary>
    Task RefreshRuleSourceAsync(string sourceId, CancellationToken cancellationToken);

    /// <summary>
    /// Manual refresh of every enabled repoFile rule source (settings
    /// "Update all").
    /// </summary>
    Task RefreshAllRuleSourcesAsync(CancellationToken cancellationToken);
}