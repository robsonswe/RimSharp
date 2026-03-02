#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text; // Required for Encoding
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models; 
namespace RimSharp.Infrastructure.Workshop.Download.Execution
{
    public class SteamCmdScriptGenerator : ISteamCmdScriptGenerator
    {
        private const string RimworldAppId = "294100";

        public async Task<string> GenerateScriptAsync(
            string scriptDirectory,
            string uniqueScriptId,
            string installDir,
            IEnumerable<DownloadItem> items,
            bool validate,
            CancellationToken cancellationToken)
        {
            string scriptPath = Path.Combine(scriptDirectory, $"rimsharp_dl_script_{uniqueScriptId}.txt");
            var scriptBuilder = new StringBuilder();
            Directory.CreateDirectory(scriptDirectory);
            scriptBuilder.AppendLine($"force_install_dir \"{installDir}\"");
            scriptBuilder.AppendLine("login anonymous");

            string downloadCommand = $"workshop_download_item {RimworldAppId}";
            string validateSuffix = validate ? " validate" : "";

            foreach (var item in items ?? Enumerable.Empty<DownloadItem>())
            {
                if (item != null && !string.IsNullOrWhiteSpace(item.SteamId))
                {

                    scriptBuilder.AppendLine($"{downloadCommand} {item.SteamId.Trim()}{validateSuffix}");
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            scriptBuilder.AppendLine("quit");

            var utf8WithoutBom = new System.Text.UTF8Encoding(false);

            await File.WriteAllTextAsync(scriptPath, scriptBuilder.ToString(), utf8WithoutBom, cancellationToken);

            return scriptPath;
        }
    }
}


