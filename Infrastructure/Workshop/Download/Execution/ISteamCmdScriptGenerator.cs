#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models; 
namespace RimSharp.Infrastructure.Workshop.Download.Execution
{
    public interface ISteamCmdScriptGenerator
    {
        Task<string> GenerateScriptAsync(
            string scriptDirectory, 
            string uniqueScriptId,  // Unique identifier for the script file name
            string installDir,      // force_install_dir parameter
            IEnumerable<DownloadItem> items,
            bool validate,
            CancellationToken cancellationToken);
    }
}
