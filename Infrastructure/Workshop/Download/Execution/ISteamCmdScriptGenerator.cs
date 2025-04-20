#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models; // Assuming DownloadItem is here

namespace RimSharp.Infrastructure.Workshop.Download.Execution
{
    public interface ISteamCmdScriptGenerator
    {
        Task<string> GenerateScriptAsync(
            string scriptDirectory, // Directory where the script should be saved
            string uniqueScriptId,  // Unique identifier for the script file name
            string installDir,      // force_install_dir parameter
            IEnumerable<DownloadItem> items,
            bool validate,
            CancellationToken cancellationToken);
    }
}