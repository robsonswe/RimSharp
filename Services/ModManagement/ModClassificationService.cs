using System;
using System.IO;
using RimSharp.Shared.Models;

namespace RimSharp.Services.ModManagement
{
    /// <summary>
    /// Service to classify mods by their source based on their location and content.
    /// Based on RimSort's _set_mod_type logic.
    /// </summary>
    public class ModClassificationService
    {
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
        public ModType ClassifyMod(string modPath, ModType existingModType = ModType.Zipped)
        {
            if (string.IsNullOrEmpty(modPath) || !Directory.Exists(modPath))
            {
                return existingModType;
            }

            var modDir = new DirectoryInfo(modPath);

            // First, check if it's in the game Data/ folder (core/DLC)
            if (!string.IsNullOrEmpty(_instance.GameFolder))
            {
                var dataPath = Path.Combine(_instance.GameFolder, "Data");
                if (modDir.FullName.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                {
                    return ClassifyDataMod(modPath);
                }
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
                return ModType.Zipped; // Default to Zipped for local mods not in known locations
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
                    var content = File.ReadAllText(aboutXml);
                    if (content.Contains("ludeon.rimworld", StringComparison.OrdinalIgnoreCase))
                    {
                        return ModType.Expansion; // Expansion type for DLC
                    }
                    return ModType.Core;
                }
                catch (Exception)
                {
                    // Continue to other checks
                }
            }

            return ModType.Expansion;
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
                        return ModType.WorkshopL; // Your existing WorkshopL type for SteamCMD mods
                    }
                }
                catch (Exception)
                {
                    // Continue to other checks
                }
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
        /// Get display name for a mod type.
        /// </summary>
        public static string GetTypeName(ModType type)
        {
            return type switch
            {
                ModType.Core => "Core Game",
                ModType.Expansion => "DLC",
                ModType.Workshop => "Steam Workshop",
                ModType.WorkshopL => "SteamCMD",
                ModType.Git => "Git",
                ModType.Zipped => "Local",
                _ => "Unknown"
            };
        }
    }
}