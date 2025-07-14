using System;
using System.Threading;
using System.Threading.Tasks;

public interface IDataUpdateService
{
    /// <summary>
    /// Checks the remote repository for a new manifest and downloads updates if necessary.
    /// </summary>
    Task CheckForAndApplyUpdatesAsync(IProgress<DataUpdateProgress> progress, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the full local path to a specific data file.
    /// </summary>
    /// <param name="fileName">The name of the file (e.g., "rules.json").</param>
    /// <returns>The full path to the cached file.</returns>
    string GetDataFilePath(string fileName);
}