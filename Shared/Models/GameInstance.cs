using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimSharp.Infrastructure.Configuration;

namespace RimSharp.Shared.Models
{
    /// <summary>
    /// Data model for a RimWorld game instance with Steam support.
    /// </summary>
    public class GameInstance
    {
        /// <summary>
        /// Display name for this instance.
        /// </summary>
        public string Name { get; set; } = "RimWorld";

        /// <summary>
        /// Path to the game installation folder (contains Data/, About.xml, etc.).
        /// </summary>
        public string? GameFolder { get; set; }

        /// <summary>
        /// Path to the config folder (contains ModsConfig.xml, Prefs.xml, etc.).
        /// </summary>
        public string? ConfigFolder { get; set; }

        /// <summary>
        /// Path to the local mods folder (typically game_folder/Mods).
        /// </summary>
        public string? LocalFolder { get; set; }

        /// <summary>
        /// Path to the Steam Workshop mods folder (steamapps/workshop/content/294100).
        /// Only used when Steam integration is enabled.
        /// </summary>
        public string? WorkshopFolder { get; set; }

        /// <summary>
        /// Whether this is a Steam installation. If true, WorkshopFolder must be set.
        /// </summary>
        public bool IsSteamInstallation { get; set; }

        /// <summary>
        /// Whether Steam client integration is enabled. If true, allows Steam API calls
        /// for workshop subscription/unsubscription and protocol launches.
        /// </summary>
        public bool SteamClientIntegration { get; set; }

        /// <summary>
        /// Launch the game via Steam protocol (steam://rungameid/294100) to enable Steam overlay.
        /// Requires SteamClientIntegration to be true.
        /// </summary>
        public bool LaunchViaSteamProtocol { get; set; }

        /// <summary>
        /// Additional arguments to pass when launching the game.
        /// </summary>
        public string LaunchArguments { get; set; } = "";

        /// <summary>
        /// Path to SteamCMD installation (for SteamCMD mod downloads).
        /// </summary>
        public string SteamCmdPath { get; set; } = "";

        /// <summary>
        /// Whether to automatically clear SteamCMD depot cache after downloads.
        /// </summary>
        public bool SteamCmdAutoClearCache { get; set; } = true;

        /// <summary>
        /// Whether to ignore SteamCMD errors during mod operations.
        /// </summary>
        public bool SteamCmdIgnoreErrors { get; set; } = false;

        /// <summary>
        /// Validate the instance configuration and auto-fix inconsistencies.
        /// </summary>
        public void ValidateAndFix()
        {
            // Fix: Steam protocol requires Steam client integration
            if (LaunchViaSteamProtocol && !SteamClientIntegration)
            {
                Console.WriteLine("Warning: Steam protocol launch is enabled but Steam client integration is disabled. Disabling...");
                LaunchViaSteamProtocol = false;
            }

            // Validate Steam integration prerequisites
            if (IsSteamInstallation)
            {
                if (string.IsNullOrEmpty(WorkshopFolder))
                {
                    Console.WriteLine("Warning: Steam installation detected but workshop folder is not configured.");
                    IsSteamInstallation = false;
                    SteamClientIntegration = false;
                }
                else if (!Directory.Exists(WorkshopFolder))
                {
                    Console.WriteLine($"Warning: Steam workshop folder does not exist: {WorkshopFolder}");
                    IsSteamInstallation = false;
                    SteamClientIntegration = false;
                }
                else if (!ValidateWorkshopAcfFile(WorkshopFolder))
                {
                    Console.WriteLine($"Warning: Steam workshop ACF file not found at: {WorkshopFolder}");
                    IsSteamInstallation = false;
                    SteamClientIntegration = false;
                }
            }

            // Clear orphaned workshop folder when integration is disabled
            if (!IsSteamInstallation && !string.IsNullOrEmpty(WorkshopFolder) && Directory.Exists(WorkshopFolder))
            {
                Console.WriteLine("Warning: Steam client integration is disabled but workshop folder is still configured. Clearing workshop folder...");
                WorkshopFolder = "";
            }
        }

        /// <summary>
        /// Validate that the appworkshop_294100.acf file exists for the Steam Workshop location.
        /// </summary>
        private bool ValidateWorkshopAcfFile(string workshopLocation)
        {
            try
            {
                if (string.IsNullOrEmpty(workshopLocation)) return false;

                var dir = new DirectoryInfo(workshopLocation);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "appworkshop_294100.acf")))
                        return true;

                    if (string.Equals(dir.Name, "workshop", StringComparison.OrdinalIgnoreCase))
                        return File.Exists(Path.Combine(dir.FullName, "appworkshop_294100.acf"));

                    dir = dir.Parent;
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Get all mod search paths in priority order (Local and Workshop folders).
        /// Data/ is excluded here as Core/DLCs are handled separately by LoadCoreMods.
        /// </summary>
        public List<string> GetModSearchPaths()
        {
            var paths = new List<string>();

            // Include local mods folder
            if (!string.IsNullOrEmpty(LocalFolder) && Directory.Exists(LocalFolder))
            {
                paths.Add(LocalFolder);
            }

            // Include workshop folder if Steam is enabled
            if (IsSteamInstallation && !string.IsNullOrEmpty(WorkshopFolder) && Directory.Exists(WorkshopFolder))
            {
                paths.Add(WorkshopFolder);
            }

            return paths;
        }
    }
}