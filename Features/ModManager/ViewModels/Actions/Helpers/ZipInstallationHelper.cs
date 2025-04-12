using RimSharp.Shared.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    // Mark the class as partial
    public partial class ModActionsViewModel
    {
        private bool IsValidZipFile(string path)
        {
            try
            {
                if (!File.Exists(path) || !Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    return false;
                using (var archive = ZipFile.OpenRead(path))
                {
                    return archive.Entries.Any();
                }
            }
            catch { return false; }
        }

        private async Task<ModItem> ValidateModZip(ZipArchive archive, CancellationToken ct = default)
        {
            var aboutEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Equals("About/About.xml", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.Equals("About\\About.xml", StringComparison.OrdinalIgnoreCase));

            if (aboutEntry != null)
            {
                return await ParseAboutXmlFromZip(aboutEntry, ct);
            }

            var rootFolder = GetRootModFolder(archive); // Uses helper below
            if (rootFolder == null) return null;

            var rootFolderName = rootFolder.FullName; // e.g. MyMod/
            var aboutPathInFolder1 = $"{rootFolderName}About/About.xml";
            var aboutPathInFolder2 = $"{rootFolderName}About\\About.xml";

            aboutEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Equals(aboutPathInFolder1, StringComparison.OrdinalIgnoreCase) ||
                e.FullName.Equals(aboutPathInFolder2, StringComparison.OrdinalIgnoreCase));

            return aboutEntry != null ? await ParseAboutXmlFromZip(aboutEntry, ct) : null;
        }

        private async Task<ModItem> ParseAboutXmlFromZip(ZipArchiveEntry aboutEntry, CancellationToken ct = default)
        {
            try
            {
                using (var stream = aboutEntry.Open())
                using (var reader = new StreamReader(stream))
                {
                    ct.ThrowIfCancellationRequested();
                    var content = await reader.ReadToEndAsync(); // ReadToEndAsync doesn't take CT directly in older frameworks
                    ct.ThrowIfCancellationRequested();

                    // Uses helper from GitHubInstallationHelper (or could be duplicated/moved to a common XML helper)
                    return ParseAboutXmlFromString(content);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to parse About.xml from zip entry '{aboutEntry.FullName}': {ex.Message}");
                return null;
            }
        }

        private ZipArchiveEntry GetRootModFolder(ZipArchive archive)
        {
             var rootFolders = archive.Entries
                .Where(e => !string.IsNullOrEmpty(e.FullName) &&
                       !e.FullName.Replace('\\', '/').TrimEnd('/').Contains('/') && // Only top-level
                        e.FullName.EndsWith("/")) // Must be a directory entry
                .ToList();

            // Debug log for troubleshooting
            Debug.WriteLine($"Found {rootFolders.Count} root folders in zip: {string.Join(", ", rootFolders.Select(f => f.FullName))}");

            return rootFolders.Count == 1 ? rootFolders[0] : null;
        }


        private void ExtractMod(ZipArchive archive, string targetDir, ZipArchiveEntry rootFolderEntry, CancellationToken ct)
        {
            Directory.CreateDirectory(targetDir);
            string rootFolderName = rootFolderEntry?.FullName; // e.g., "MyModFolder/"

            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(entry.Name)) continue; // Skip directory entries

                string relativePath;
                if (rootFolderEntry == null)
                {
                    relativePath = entry.FullName;
                }
                else
                {
                    if (!entry.FullName.StartsWith(rootFolderName, StringComparison.OrdinalIgnoreCase)) continue;
                    relativePath = entry.FullName.Substring(rootFolderName.Length);
                }

                relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(relativePath)) continue; // Skip empty paths (e.g., the root folder entry itself if processed)

                string destinationPath = Path.Combine(targetDir, relativePath);
                var dirPath = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(dirPath) && !Directory.Exists(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }

                entry.ExtractToFile(destinationPath, overwrite: true); // Consider adding overwrite confirmation/logic
            }
        }
    }
}