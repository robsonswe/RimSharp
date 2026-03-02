using System;
using System.Threading;
using System.Threading.Tasks;

public interface IDataUpdateService
{
    /// <summary>

    /// </summary>
    Task CheckForAndApplyUpdatesAsync(IProgress<DataUpdateProgress> progress, CancellationToken cancellationToken);

    /// <summary>

    /// </summary>
    /// <param name="fileName">The name of the file (e.g., "rules.json").</param>
    /// <returns>The full path to the cached file.</returns>
    string GetDataFilePath(string fileName);
}
