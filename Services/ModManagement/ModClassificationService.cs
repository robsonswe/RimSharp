using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Services.ModManagement
{
    /// <summary>
    /// Service to classify mods by their source based on their location and content.
    /// Based on the researched community approach for mod type classification.
    /// </summary>
    public class ModClassificationService
    {
        /// <summary>The one packageId that means the game itself.</summary>
        private const string CorePackageId = "Ludeon.RimWorld";

        private readonly GameInstance _instance;

        public ModClassificationService(GameInstance instance)
        {
            _instance = instance;
        }

        /// <summary>
        /// Classify a mod based on its path and content.
        /// </summary>
        /// <param name="modPath">Path to the mod directory</param>
        /// <param name="existingModType">Existing ModType to preserve if no better classification is found</param>
        /// <returns>ModType classification</returns>
        public ModType ClassifyMod(string modPath, ModType existingModType = ModType.Untracked)
        {
            if (string.IsNullOrEmpty(modPath) || !Directory.Exists(modPath))
            {
                return existingModType;
            }

            var modDir = new DirectoryInfo(modPath);

            // First, check if it's in the game Data/ folder (core/DLC) — bundle-aware
            if (!string.IsNullOrEmpty(_instance.GameFolder))
            {
                var dataPaths = GetGameDataPaths(_instance.GameFolder);
                foreach (var dataPath in dataPaths)
                {
                    if (modDir.FullName.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return ClassifyDataMod(modPath);
                    }
                }
            }

            // Everything below this line is outside the game's Data folder, where
            // Core and Expansion are meaningless — neither type ever lives in the
            // mods folder or the Workshop folder. ModType.Core is also the enum
            // default (0), so a freshly parsed folder arrives "looking" Core;
            // normalize it (and a stray Expansion) to Untracked so a marker-less
            // mod can never display as a game component.
            if (existingModType is ModType.Core or ModType.Expansion)
            {
                existingModType = ModType.Untracked;
            }

            // Check if it's in the workshop folder
            if (!string.IsNullOrEmpty(_instance.WorkshopFolder) && 
                modDir.FullName.StartsWith(_instance.WorkshopFolder, StringComparison.OrdinalIgnoreCase))
            {
                return ModType.Workshop;
            }

            // Check if it's in the local folder
            if (!string.IsNullOrEmpty(_instance.LocalFolder) && 
                modDir.FullName.StartsWith(_instance.LocalFolder, StringComparison.OrdinalIgnoreCase))
            {
                return ClassifyLocalMod(modPath, existingModType);
            }

            // If not in any known location, check if it's a valid RimWorld mod
            if (IsValidRimWorldMod(modPath))
            {
                return ModType.Untracked; // Default to Untracked for local mods not in known locations
            }

            return existingModType; // Return existing type if no better classification found
        }

        /// <summary>
        /// Classify a mod in the Data/ folder (core or DLC).
        /// </summary>
        private ModType ClassifyDataMod(string modPath)
        {
            var modDirName = new DirectoryInfo(modPath).Name;
            if (string.Equals(modDirName, "Core", StringComparison.OrdinalIgnoreCase))
            {
                return ModType.Core;
            }

            var aboutXml = Path.Combine(modPath, "About", "About.xml");
            if (!File.Exists(aboutXml))
            {
                aboutXml = Path.Combine(modPath, "About.xml");
            }

            if (File.Exists(aboutXml))
            {
                try
                {
                    var packageId = ReadPackageId(aboutXml);
                    if (string.Equals(packageId, CorePackageId, StringComparison.OrdinalIgnoreCase))
                    {
                        return ModType.Core;
                    }
                    // Every other entry shipped in Data/ is official content (the
                    // DLC packageIds are Ludeon.RimWorld.*). Official content that
                    // matches neither is still never Core.
                    return ModType.Expansion;
                }
                catch (Exception)
                {
                    // Continue to other checks
                }
            }

            return ModType.Expansion;
        }

        private static string? ReadPackageId(string aboutXml)
        {
            var doc = XDocument.Load(aboutXml);
            return doc.Root?.Element("packageId")?.Value?.Trim();
        }

        /// <summary>
        /// Classify a mod in the local folder.
        /// </summary>
        private ModType ClassifyLocalMod(string modPath, ModType existingModType)
        {
            // Check for SteamCMD indicator (folder name matches PublishedFileId and has About/PublishedFileId.txt)
            var publishedFileIdPath = Path.Combine(modPath, "About", "PublishedFileId.txt");
            if (File.Exists(publishedFileIdPath))
            {
                try
                {
                    var publishedFileId = File.ReadAllText(publishedFileIdPath).Trim();
                    if (!string.IsNullOrEmpty(publishedFileId) && 
                        new DirectoryInfo(modPath).Name == publishedFileId)
                    {
                        return ModType.Tracked; // App-downloaded workshop copy tracked for updates
                    }
                }
                catch (Exception)
                {
                    // Continue to other checks
                }
            }

            // App-downloaded workshop copy: the download pipeline caches Workshop
            // metadata as RimSharp.json in the mod root, and the SteamId inside
            // ties the folder to a Workshop item.
            var appMetadataPath = Path.Combine(modPath, WorkshopDescriptionCache.FileName);
            if (File.Exists(appMetadataPath) && HasWorkshopSteamId(appMetadataPath))
            {
                return ModType.Tracked;
            }

            // The download pipeline also leaves its own timestamps behind —
            // DateStamp in the mod root and About/timestamp.txt, both written by
            // ModService.CreateTimestampFilesAsync and by nothing else. Either
            // file alone proves the folder came through this app's downloads.
            // (About/PublishStamp.txt is NOT one of ours: it is author-published
            // content that ships with the item from any source.)
            if (File.Exists(Path.Combine(modPath, "DateStamp")) ||
                File.Exists(Path.Combine(modPath, "About", "timestamp.txt")))
            {
                return ModType.Tracked;
            }

            // Last resort: this app and SteamCMD install Workshop items into
            // directories named after the PublishedFileId, so a folder whose
            // name is solely a number in the local mods folder is a workshop
            // copy even if every marker file was stripped (av, sync tools).
            // Checked before .git because Workshop items also ship .git dirs.
            var folderName = new DirectoryInfo(modPath).Name;
            if (long.TryParse(folderName, out _))
            {
                return ModType.Tracked;
            }

            // Check for Git repository
            try
            {
                if (Directory.Exists(Path.Combine(modPath, ".git")))
                {
                    return ModType.Git; // Your existing Git type
                }
            }
            catch (Exception)
            {
                // Ignore Git detection errors
            }

            return existingModType; // Return existing type if no specific classification found
        }

        private static bool HasWorkshopSteamId(string metadataPath)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(metadataPath));
                return doc.RootElement.TryGetProperty("SteamId", out var id) &&
                       id.ValueKind == JsonValueKind.String &&
                       !string.IsNullOrWhiteSpace(id.GetString());
            }
            catch (Exception)
            {
                // Unreadable metadata is not a marker.
                return false;
            }
        }

        /// <summary>
        /// Check if a directory looks like a valid RimWorld mod.
        /// </summary>
        private bool IsValidRimWorldMod(string modPath)
        {
            // Check for About.xml
            var aboutXml = Path.Combine(modPath, "About.xml");
            if (File.Exists(aboutXml))
            {
                return true;
            }

            // Check for common mod files
            var modFiles = Directory.GetFiles(modPath, "*.xml", SearchOption.TopDirectoryOnly);
            if (modFiles.Length > 0)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the candidate Data folder paths for a game folder, in priority order.
        /// Bundle-aware on macOS: <game>/Contents/Data, <game>/Contents/Resources/Data, <game>/Data.
        /// Non-bundle: just <game>/Data.
        /// Mirrors PathService.GetGameDataPathFor (the canonical derivation) locally because
        /// the layering leaves ModManagement without an IPathService dependency.
        /// </summary>
        private static IEnumerable<string> GetGameDataPaths(string gameFolder)
        {
            if (string.IsNullOrEmpty(gameFolder))
                yield break;

            var fileName = Path.GetFileName(gameFolder);
            if (!string.IsNullOrEmpty(fileName) &&
                fileName.EndsWith(".app", StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(gameFolder))
            {
                yield return Path.Combine(gameFolder, "Contents", "Data");
                yield return Path.Combine(gameFolder, "Contents", "Resources", "Data");
                yield return Path.Combine(gameFolder, "Data");
            }
            else
            {
                yield return Path.Combine(gameFolder, "Data");
            }
        }

        /// <summary>
        /// Get display name for a mod type.
        /// </summary>
        public static string GetTypeName(ModType type)
        {
            return type switch
            {
                ModType.Core => "Core Game",
                ModType.Expansion => "DLC",
                ModType.Workshop => "Steam Workshop",
                ModType.Tracked => "Tracked",
                ModType.Git => "Git",
                ModType.Untracked => "Untracked",
                _ => "Unknown"
            };
        }
    }
}