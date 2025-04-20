#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text; // Required for Encoding
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models; // Assuming DownloadItem is here

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

            // Ensure the directory exists
            Directory.CreateDirectory(scriptDirectory);

            // --- Build Script Content ---
            scriptBuilder.AppendLine($"force_install_dir \"{installDir}\"");
            scriptBuilder.AppendLine("login anonymous");

            string downloadCommand = $"workshop_download_item {RimworldAppId}";
            string validateSuffix = validate ? " validate" : "";

            foreach (var item in items ?? Enumerable.Empty<DownloadItem>())
            {
                if (item != null && !string.IsNullOrWhiteSpace(item.SteamId))
                {
                    // Use Trim() just in case there's leading/trailing whitespace on the ID
                    scriptBuilder.AppendLine($"{downloadCommand} {item.SteamId.Trim()}{validateSuffix}");
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            scriptBuilder.AppendLine("quit");
            // --- End Build Script Content ---


            // --- Write Script File ---
            // Create a UTF8Encoding instance specifying *false* for emitting the BOM
            var utf8WithoutBom = new System.Text.UTF8Encoding(false);

            // Use the encoding instance without BOM when writing the file
            await File.WriteAllTextAsync(scriptPath, scriptBuilder.ToString(), utf8WithoutBom, cancellationToken);
            // --- End Write Script File ---

            return scriptPath;
        }
    }
}