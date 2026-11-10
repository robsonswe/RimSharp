using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Models;

namespace RimSharp.Services.ModManagement
{
    /// <summary>
    /// Service for mod configuration management (export, import, clear, restore, sanitize, etc.).
    /// </summary>
    public interface IModConfigurationService
    {
        /// <summary>
        /// Export current mod list to a user-selected XML file (mirrors ModsConfig.xml format).
        /// </summary>
        Task<ModConfigurationResult> ExportModListAsync(string? filePath = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Import mod list from a user-selected XML file (accepts ModsConfig-format XML).
        /// </summary>
        Task<ModConfigurationResult> ImportModListAsync(string? filePath = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Clear all mods: delete Mods folder contents, reset ModsConfig.xml to vanilla, backup original.
        /// </summary>
        Task<ModConfigurationResult> ClearAllModsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Restore ModsConfig.xml from rolling backups.
        /// </summary>
        Task<ModConfigurationResult> RestoreModsConfigFromBackupAsync(string backupFileName, CancellationToken cancellationToken = default);

        /// <summary>
        /// List available rolling backups for ModsConfig.xml.
        /// </summary>
        Task<IReadOnlyList<ModConfigBackupInfo>> ListModConfigBackupsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sanitize Core & DLC load order (enforce hierarchy, ensure installed DLCs in knownExpansions).
        /// </summary>
        Task<ModConfigurationResult> SanitizeCoreDlcOrderAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Detect duplicate package IDs between local Mods/ and workshop content/.
        /// </summary>
        Task<ModConfigurationResult> DetectDuplicatePackageIdsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Analyze saves folder size and Steam Cloud quota.
        /// </summary>
        Task<ModConfigurationResult> AnalyzeSavesQuotaAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result of a mod configuration action.
    /// </summary>
    public sealed class ModConfigurationResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public int? Count { get; init; }
        public IReadOnlyDictionary<string, object>? Details { get; init; }

        public static ModConfigurationResult Ok(string message, int? count = null, IReadOnlyDictionary<string, object>? details = null)
            => new() { Success = true, Message = message, Count = count, Details = details };

        public static ModConfigurationResult Fail(string message, IReadOnlyDictionary<string, object>? details = null)
            => new() { Success = false, Message = message, Details = details };

        public static ModConfigurationResult Cancelled(string message = "Operation cancelled")
            => new() { Success = false, Message = message };
    }

    /// <summary>
    /// Information about a ModsConfig.xml backup file.
    /// </summary>
    public sealed class ModConfigBackupInfo
    {
        public string FileName { get; init; } = string.Empty;
        public string FullPath { get; init; } = string.Empty;
        public DateTime CreatedUtc { get; init; }
        public long SizeBytes { get; init; }
        public bool IsRollingBackup { get; init; } // true for backup_YYYYMMDD_HHMMSS, false for .xml.backup
    }
}