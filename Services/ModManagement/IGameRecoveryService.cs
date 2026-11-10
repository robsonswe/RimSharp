using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Models;

namespace RimSharp.Services.ModManagement
{
    /// <summary>
    /// Service for game file recovery and troubleshooting actions.
    /// </summary>
    public interface IGameRecoveryService
    {
        /// <summary>
        /// Reset game files (delete all except Mods folder, trigger Steam reinstall).
        /// </summary>
        Task<GameRecoveryResult> ResetGameFilesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Reset Steam Workshop mods (delete workshop content folder, trigger redownload).
        /// </summary>
        Task<GameRecoveryResult> ResetSteamWorkshopModsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Reset mod configurations (delete config files except ModsConfig.xml and Prefs.xml).
        /// </summary>
        Task<GameRecoveryResult> ResetModConfigurationsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Reset game configurations (delete ModsConfig.xml, Prefs.xml, KeyPrefs.xml).
        /// </summary>
        Task<GameRecoveryResult> ResetGameConfigurationsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Clean huge log files (truncate or delete Player.log / Player-prev.log if > 50 MB).
        /// </summary>
        Task<GameRecoveryResult> CleanLogFilesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Reset display and resolution settings in Prefs.xml.
        /// </summary>
        Task<GameRecoveryResult> ResetDisplayResolutionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Clear mod runtime caches (PrepatcherCache, %TEMP%/Prepatcher, HugsLib).
        /// </summary>
        Task<GameRecoveryResult> ClearModRuntimeCachesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Check file permissions and cloud locks (OneDrive reparse points, etc.).
        /// </summary>
        Task<GameRecoveryResult> CheckFilePermissionsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Scan for missing critical mod assemblies (AV quarantine detection).
        /// </summary>
        Task<GameRecoveryResult> ScanMissingAssembliesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Verify Mod_*.xml settings files integrity.
        /// </summary>
        Task<GameRecoveryResult> VerifyModSettingsXmlAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Check path compatibility (non-ASCII characters).
        /// </summary>
        Task<GameRecoveryResult> CheckPathCompatibilityAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result of a game recovery action.
    /// </summary>
    public sealed class GameRecoveryResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public int? Count { get; init; }
        public IReadOnlyDictionary<string, object>? Details { get; init; }

        public static GameRecoveryResult Ok(string message, int? count = null, IReadOnlyDictionary<string, object>? details = null)
            => new() { Success = true, Message = message, Count = count, Details = details };

        public static GameRecoveryResult Fail(string message, IReadOnlyDictionary<string, object>? details = null)
            => new() { Success = false, Message = message, Details = details };

        public static GameRecoveryResult NotAvailable(string reason)
            => new() { Success = false, Message = $"Not available: {reason}" };
    }
}