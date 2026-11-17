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
}
