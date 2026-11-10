#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace RimSharp.Features.Troubleshooting.ViewModels
{
    /// <summary>
    /// Static descriptor for a user-facing symptom in the troubleshooting dialog.
    /// Maps a plain-language problem to the scan check IDs, related action IDs, and possible causes.
    /// </summary>
    public sealed class TroubleshootingSymptomDescriptor
    {
        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public IReadOnlyList<string> CheckIds { get; }
        public IReadOnlyList<string> RelatedActionIds { get; }
        public IReadOnlyList<string> Causes { get; }
        public bool RequiresSteam { get; }

        public TroubleshootingSymptomDescriptor(
            string id,
            string title,
            string description,
            IReadOnlyList<string> checkIds,
            IReadOnlyList<string> relatedActionIds,
            IReadOnlyList<string> causes,
            bool requiresSteam = false)
        {
            Id = id;
            Title = title;
            Description = description;
            CheckIds = checkIds;
            RelatedActionIds = relatedActionIds;
            Causes = causes;
            RequiresSteam = requiresSteam;
        }
    }

    /// <summary>
    /// Central registry of all troubleshooting symptoms.
    /// Mapping is conservative: each symptom lists the checks most directly relevant.
    /// Deviations from the ideal mapping are documented inline.
    /// </summary>
    public static class TroubleshootingSymptoms
    {
        public static readonly IReadOnlyList<TroubleshootingSymptomDescriptor> All = new[]
        {
            // "Game won't start or crashes on launch"
            // Checks: paths_resolve, game_version, running_processes, config_folder_attributes (Windows only),
            // log_signatures, mod_assemblies
            // Note: log_signatures check currently returns empty in the scan service (stubbed).
            //       Honesty-table log-signature problems sit under this symptom.
            new TroubleshootingSymptomDescriptor(
                id: "crash_on_launch",
                title: "Game won't start or crashes on launch",
                description: "RimWorld fails to start, crashes immediately, or shows a Unity crash dialog.",
                checkIds: new[]
                {
                    "paths_resolve",
                    "game_version",
                    "running_processes",
                    "config_folder_attributes",
                    "log_signatures",
                    "mod_assemblies"
                },
                relatedActionIds: new[]
                {
                    "reset_game_files",
                    "reset_mod_configurations",
                    "reset_game_configurations",
                    "kill_zombie_processes",
                    "check_file_permissions",
                    "scan_missing_assemblies",
                    "verify_mod_settings_xml",
                    "check_path_compatibility"
                },
                causes: new[]
                {
                    "A corrupted mod assembly blocked the game from starting",
                    "A pending OS update broke a dependency the game relies on",
                    "A leftover zombie process from a previous crash is holding a lock",
                    "A Steam file integrity issue left game files incomplete",
                    "An outdated or unloadable mod is preventing startup"
                },
                requiresSteam: false),

            // "A mod isn't loading or shows errors"
            // Checks: workshop_mods, mod_assemblies, mods_config_integrity
            new TroubleshootingSymptomDescriptor(
                id: "mod_not_loading",
                title: "A mod isn't loading or shows errors",
                description: "A specific mod fails to load, shows red errors in the mod list, or appears missing.",
                checkIds: new[]
                {
                    "workshop_mods",
                    "mod_assemblies",
                    "mods_config_integrity"
                },
                relatedActionIds: new[]
                {
                    "reset_steam_workshop_mods",
                    "scan_missing_assemblies",
                    "verify_mod_settings_xml",
                    "sanitize_core_dlc_order"
                },
                causes: new[]
                {
                    "The mod's assembly file is missing, zero-byte, or inaccessible",
                    "The mod declares a dependency that isn't installed or is the wrong version",
                    "The mod's About.xml or Mod_*.xml settings file is malformed",
                    "A duplicate mod with the same package ID is conflicting",
                    "The mod was built for a different game version and is incompatible"
                },
                requiresSteam: false),

            // "Steam downloads are stuck or never finish"
            // Checks: steam_installation, stuck_downloads, workshop_mods
            // Related: ClearDownloadCache, PurgeWorkshopTemp
            new TroubleshootingSymptomDescriptor(
                id: "steam_downloads_stuck",
                title: "Steam downloads are stuck or never finish",
                description: "Workshop mods or game updates sit in 'downloading' forever or show network errors.",
                checkIds: new[]
                {
                    "steam_installation",
                    "stuck_downloads",
                    "workshop_mods"
                },
                relatedActionIds: new[]
                {
                    "clear_download_cache",
                    "purge_workshop_temp",
                    "verify_game_files",
                    "repair_library"
                },
                causes: new[]
                {
                    "A corrupted download cache is blocking new downloads",
                    "The Steam client lost connection to the content servers",
                    "A workshop staging directory has leftover partial files",
                    "Disk space ran out during download",
                    "Antivirus or firewall is blocking Steam's download traffic"
                },
                requiresSteam: true),

            // "Installed mods went missing from my Steam install"
            // Checks: steam_installation, orphaned_workshop_mods, workshop_mods
            // Related: CleanOrphanedWorkshopMods
            // HIDDEN for non-Steam users (RequiresSteam = true)
            new TroubleshootingSymptomDescriptor(
                id: "missing_workshop_mods",
                title: "Installed mods went missing from my Steam install",
                description: "Workshop mods show as subscribed in Steam but don't appear in the game's mod list.",
                checkIds: new[]
                {
                    "steam_installation",
                    "orphaned_workshop_mods",
                    "workshop_mods"
                },
                relatedActionIds: new[]
                {
                    "clean_orphaned_workshop_mods",
                    "reset_steam_workshop_mods",
                    "verify_game_files"
                },
                causes: new[]
                {
                    "The appworkshop_294100.acf file has stale entries for mods no longer on disk",
                    "A Steam client update reset the workshop content folder location",
                    "The workshop content folder was moved or deleted outside of Steam",
                    "A download was interrupted and Steam never retried it",
                    "Multiple Steam library folders are confusing the content lookup"
                },
                requiresSteam: true),

            // "Log files are huge"
            // Checks: log_file_sizes
            // Related: CleanLogFiles
            new TroubleshootingSymptomDescriptor(
                id: "huge_log_files",
                title: "Log files are huge",
                description: "Player.log or Player-prev.log has grown very large (hundreds of MB), slowing startup.",
                checkIds: new[]
                {
                    "log_file_sizes"
                },
                relatedActionIds: new[]
                {
                    "clean_log_files"
                },
                causes: new[]
                {
                    "A mod is spamming exceptions or debug output every frame",
                    "The game ran for a very long session without restarting",
                    "A graphics driver issue is causing repeated shader compilation logs",
                    "An audio decode failure is being logged repeatedly",
                    "The log file was never rotated after a previous huge session"
                },
                requiresSteam: false),

            // "Mod settings look broken/wrong"
            // Checks: mod_settings_xml, mods_config_integrity
            // Related: RestoreModsConfigFromBackup, ExportModList
            new TroubleshootingSymptomDescriptor(
                id: "mod_settings_broken",
                title: "Mod settings look broken/wrong",
                description: "Mod settings are reset, corrupted, or changes don't persist between launches.",
                checkIds: new[]
                {
                    "mod_settings_xml",
                    "mods_config_integrity"
                },
                relatedActionIds: new[]
                {
                    "verify_mod_settings_xml",
                    "restore_mods_config_from_backup",
                    "sanitize_core_dlc_order"
                },
                causes: new[]
                {
                    "A Mod_*.xml settings file contains malformed XML or NUL bytes",
                    "The ModsConfig.xml file was corrupted by a crash during write",
                    "A mod's settings schema changed and the old file is incompatible",
                    "File permissions or cloud sync (OneDrive) prevented saving",
                    "A mod wrote invalid data to its settings file"
                },
                requiresSteam: false),

            // "Saves folder is huge / storage concerns"
            // Checks: saves_folder_size (report-only, no fix action)
            new TroubleshootingSymptomDescriptor(
                id: "saves_folder_huge",
                title: "Saves folder is huge / storage concerns",
                description: "The Saves folder is consuming significant disk space or approaching Steam Cloud quota.",
                checkIds: new[]
                {
                    "saves_folder_size"
                },
                relatedActionIds: new[]
                {
                    "analyze_saves_quota"
                },
                causes: new[]
                {
                    "Many large save files accumulated over time",
                    "A mod is embedding large assets (images, maps) into save files",
                    "Steam Cloud quota is nearly full and sync is failing",
                    "Autosave interval is very frequent creating many files",
                    "Old saves from previous game versions were never cleaned up"
                },
                requiresSteam: false),

            // "Errors about mod paths or file names"
            // Checks: path_compatibility, paths_resolve
            new TroubleshootingSymptomDescriptor(
                id: "path_errors",
                title: "Errors about mod paths or file names",
                description: "The game or mods report path-related errors, often due to non-ASCII characters in folder names.",
                checkIds: new[]
                {
                    "path_compatibility",
                    "paths_resolve"
                },
                relatedActionIds: new[]
                {
                    "check_path_compatibility",
                    "reset_game_files"
                },
                causes: new[]
                {
                    "A folder in the game or mod path contains non-ASCII characters",
                    "The game folder was moved to a path with special characters",
                    "A mod's folder name uses characters the game engine can't handle",
                    "A cloud-synced folder (OneDrive) has placeholder/reparse points",
                    "The config folder path exceeds the OS maximum length"
                },
                requiresSteam: false),

            // "Display/resolution is messed up"
            // NO checks; show ResetDisplayResolution directly
            new TroubleshootingSymptomDescriptor(
                id: "display_resolution",
                title: "Display/resolution is messed up",
                description: "The game launches off-screen, at the wrong resolution, or with corrupted display settings.",
                checkIds: new string[0],
                relatedActionIds: new[]
                {
                    "reset_display_resolution"
                },
                causes: new[]
                {
                    "The game saved a resolution your current monitor doesn't support",
                    "A multi-monitor setup changed and the game window is on a disconnected display",
                    "The Prefs.xml display nodes were corrupted by a crash",
                    "A graphics driver update reset the display configuration",
                    "The game launched on a monitor with a different DPI scaling"
                },
                requiresSteam: false),
        };

        /// <summary>
        /// Returns symptoms visible for the given Steam installation status.
        /// </summary>
        public static IReadOnlyList<TroubleshootingSymptomDescriptor> GetVisible(bool isSteamInstallation)
        {
            return All.Where(s => !s.RequiresSteam || isSteamInstallation).ToList();
        }

        /// <summary>
        /// Finds a symptom by ID, or null if not found.
        /// </summary>
        public static TroubleshootingSymptomDescriptor? FindById(string id)
        {
            return All.FirstOrDefault(s => s.Id == id);
        }
    }
}