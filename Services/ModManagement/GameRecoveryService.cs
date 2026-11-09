using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using RimSharp.Features.ModManager.ViewModels.Actions;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Services.Workshop;

namespace RimSharp.Services.ModManagement
{
    /// <summary>
    /// Service for game file recovery and troubleshooting actions.
    /// </summary>
    public class GameRecoveryService : IGameRecoveryService
    {
        private readonly GameInstance _gameInstance;
        private readonly ILoggerService _logger;
        private readonly IPathService _pathService;
        private readonly ISteamSubscriptionService _steamSubscriptionService;

        public GameRecoveryService(
            GameInstance gameInstance,
            ILoggerService logger,
            IPathService pathService,
            ISteamSubscriptionService steamSubscriptionService)
        {
            _gameInstance = gameInstance ?? throw new ArgumentNullException(nameof(gameInstance));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _steamSubscriptionService = steamSubscriptionService ?? throw new ArgumentNullException(nameof(steamSubscriptionService));
        }

        /// <summary>
        /// Test seam: override steam:// protocol launching so tests never
        /// touch the real Steam client. When null (default), the real
        /// protocol launcher is used.
        /// </summary>
        internal static Func<string, Task<bool>>? TestProtocolLaunchOverride { get; set; }

        /// <summary>
        /// Launches a steam:// URL. Tests substitute the real launcher via
        /// TestProtocolLaunchOverride; production code always launches for real.
        /// </summary>
        private async Task<bool> LaunchSteamUrlAsync(string url)
        {
            if (TestProtocolLaunchOverride != null)
            {
                return await TestProtocolLaunchOverride(url).ConfigureAwait(false);
            }

            return Core.Services.SteamProtocolLauncher.TryOpen(url, _logger);
        }

        /// <inheritdoc />
        public async Task<GameRecoveryResult> ResetGameFilesAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("ResetGameFilesAsync started", nameof(GameRecoveryService));

            if (!_gameInstance.IsSteamInstallation)
            {
                _logger.LogWarning("ResetGameFilesAsync: Not a Steam installation", nameof(GameRecoveryService));
                return GameRecoveryResult.NotAvailable("Requires Steam installation");
            }

            if (string.IsNullOrEmpty(_gameInstance.GameFolder) || !Directory.Exists(_gameInstance.GameFolder))
            {
                _logger.LogError("ResetGameFilesAsync: Game folder not found", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Game folder not found");
            }

            try
            {
                var gameDir = new DirectoryInfo(_gameInstance.GameFolder);
                var modsDir = new DirectoryInfo(Path.Combine(_gameInstance.GameFolder, "Mods"));
                var modsTempDir = new DirectoryInfo(Path.Combine(_gameInstance.GameFolder, "Mods_temp"));

                // Move Mods folder to Mods_temp if it exists
                if (modsDir.Exists)
                {
                    if (modsTempDir.Exists)
                    {
                        modsTempDir.Delete(true);
                    }
                    modsDir.MoveTo(modsTempDir.FullName);
                    _logger.LogInfo($"Moved Mods folder to {modsTempDir.FullName}", nameof(GameRecoveryService));
                }

                // Delete all other files and folders in game directory
                int deletedCount = 0;
                foreach (var item in gameDir.GetFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (item.Attributes.HasFlag(FileAttributes.Directory))
                        {
                            ((DirectoryInfo)item).Delete(true);
                        }
                        else
                        {
                            item.Delete();
                        }
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to delete {item.FullName}: {ex.Message}", nameof(GameRecoveryService));
                    }
                }

                // Restore Mods folder from Mods_temp
                if (modsTempDir.Exists)
                {
                    var restoredModsDir = new DirectoryInfo(Path.Combine(_gameInstance.GameFolder, "Mods"));
                    modsTempDir.MoveTo(restoredModsDir.FullName);
                    _logger.LogInfo("Restored Mods folder from Mods_temp", nameof(GameRecoveryService));
                }

                // Trigger Steam reinstall
                var launched = await _steamSubscriptionService.VerifyGameFilesAsync();
                if (!launched)
                {
                    _logger.LogWarning("Failed to launch Steam verify/reinstall", nameof(GameRecoveryService));
                    return GameRecoveryResult.Fail($"Deleted {deletedCount} items, but failed to trigger Steam reinstall");
                }

                _logger.LogInfo("ResetGameFilesAsync completed successfully", nameof(GameRecoveryService));
                return GameRecoveryResult.Ok($"Game files reset. Deleted {deletedCount} items. Steam reinstall triggered.", deletedCount);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("ResetGameFilesAsync cancelled", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Operation cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError($"ResetGameFilesAsync failed: {ex.Message}", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail($"Reset failed: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<GameRecoveryResult> ResetSteamWorkshopModsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("ResetSteamWorkshopModsAsync started", nameof(GameRecoveryService));

            if (!_gameInstance.IsSteamInstallation)
            {
                _logger.LogWarning("ResetSteamWorkshopModsAsync: Not a Steam installation", nameof(GameRecoveryService));
                return GameRecoveryResult.NotAvailable("Requires Steam installation");
            }

            if (string.IsNullOrEmpty(_gameInstance.WorkshopFolder) || !Directory.Exists(_gameInstance.WorkshopFolder))
            {
                _logger.LogError("ResetSteamWorkshopModsAsync: Workshop folder not found", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Workshop folder not found");
            }

            try
            {
                var workshopDir = new DirectoryInfo(_gameInstance.WorkshopFolder);
                var modDirs = workshopDir.GetDirectories();
                var modIds = modDirs.Where(d => ulong.TryParse(d.Name, out _)).Select(d => d.Name).ToList();

                // Delete all workshop mod directories
                int deletedCount = 0;
                foreach (var dir in modDirs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        dir.Delete(true);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to delete workshop mod {dir.Name}: {ex.Message}", nameof(GameRecoveryService));
                    }
                }

                // Trigger Steam validate to redownload
                var launched = await _steamSubscriptionService.VerifyGameFilesAsync();
                if (!launched)
                {
                    _logger.LogWarning("Failed to launch Steam verify for workshop mods", nameof(GameRecoveryService));
                    return GameRecoveryResult.Fail($"Deleted {deletedCount} workshop mods, but failed to trigger Steam verify");
                }

                // Also trigger workshop download for each mod (best effort)
                foreach (var modId in modIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var downloadUrl = $"steam://workshop_download_item/294100/{modId}";
                        await LaunchSteamUrlAsync(downloadUrl);
                        await Task.Delay(100, cancellationToken); // Small delay between launches
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to launch workshop download for {modId}: {ex.Message}", nameof(GameRecoveryService));
                    }
                }

                _logger.LogInfo("ResetSteamWorkshopModsAsync completed successfully", nameof(GameRecoveryService));
                return GameRecoveryResult.Ok($"Workshop mods reset. Deleted {deletedCount} mods. Steam verify and redownload triggered.", deletedCount);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("ResetSteamWorkshopModsAsync cancelled", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Operation cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError($"ResetSteamWorkshopModsAsync failed: {ex.Message}", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail($"Reset failed: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<GameRecoveryResult> ResetModConfigurationsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("ResetModConfigurationsAsync started", nameof(GameRecoveryService));

            var configPath = _pathService.GetConfigPath();
            if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
            {
                _logger.LogError("ResetModConfigurationsAsync: Config folder not found", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Config folder not found");
            }

            try
            {
                var configDir = new DirectoryInfo(configPath);
                var protectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "ModsConfig.xml",
                    "Prefs.xml"
                };

                int deletedCount = 0;
                var deletedFiles = new List<string>();

                foreach (var file in configDir.GetFiles())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (protectedFiles.Contains(file.Name))
                    {
                        _logger.LogDebug($"Protected file skipped: {file.Name}", nameof(GameRecoveryService));
                        continue;
                    }

                    try
                    {
                        file.Delete();
                        deletedCount++;
                        deletedFiles.Add(file.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to delete config file {file.Name}: {ex.Message}", nameof(GameRecoveryService));
                    }
                }

                _logger.LogInfo("ResetModConfigurationsAsync completed successfully", nameof(GameRecoveryService));
                return GameRecoveryResult.Ok($"Mod configurations reset. Deleted {deletedCount} config files (ModsConfig.xml and Prefs.xml preserved).", deletedCount, new Dictionary<string, object> { ["deletedFiles"] = deletedFiles });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("ResetModConfigurationsAsync cancelled", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Operation cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError($"ResetModConfigurationsAsync failed: {ex.Message}", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail($"Reset failed: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<GameRecoveryResult> ResetGameConfigurationsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("ResetGameConfigurationsAsync started", nameof(GameRecoveryService));

            var configPath = _pathService.GetConfigPath();
            if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
            {
                _logger.LogError("ResetGameConfigurationsAsync: Config folder not found", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Config folder not found");
            }

            try
            {
                var targetFiles = new[] { "ModsConfig.xml", "Prefs.xml", "KeyPrefs.xml" };
                int deletedCount = 0;
                var deletedFiles = new List<string>();

                foreach (var fileName in targetFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var filePath = Path.Combine(configPath, fileName);
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            File.Delete(filePath);
                            deletedCount++;
                            deletedFiles.Add(fileName);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to delete {fileName}: {ex.Message}", nameof(GameRecoveryService));
                        }
                    }
                }

                _logger.LogInfo("ResetGameConfigurationsAsync completed successfully", nameof(GameRecoveryService));
                return GameRecoveryResult.Ok($"Game configurations reset. Deleted {deletedCount} files (will regenerate on next launch).", deletedCount, new Dictionary<string, object> { ["deletedFiles"] = deletedFiles });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("ResetGameConfigurationsAsync cancelled", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Operation cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError($"ResetGameConfigurationsAsync failed: {ex.Message}", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail($"Reset failed: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<GameRecoveryResult> CleanLogFilesAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("CleanLogFilesAsync started", nameof(GameRecoveryService));

            var configPath = _pathService.GetConfigPath();
            if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
            {
                _logger.LogError("CleanLogFilesAsync: Config folder not found", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Config folder not found");
            }

            try
            {
                const long SizeThreshold = 50 * 1024 * 1024; // 50 MB
                var logFiles = new[] { "Player.log", "Player-prev.log" };
                int processedCount = 0;
                var processedFiles = new List<string>();

                foreach (var fileName in logFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var filePath = Path.Combine(configPath, fileName);
                    if (!File.Exists(filePath))
                        continue;

                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length > SizeThreshold)
                    {
                        try
                        {
                            // Truncate the file (keep last 1MB for recent context)
                            const long keepBytes = 1024 * 1024; // 1 MB
                            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                            if (fs.Length > keepBytes)
                            {
                                fs.Seek(-keepBytes, SeekOrigin.End);
                                var buffer = new byte[keepBytes];
                                await fs.ReadExactlyAsync(buffer, cancellationToken);
                                fs.SetLength(0);
                                await fs.WriteAsync(buffer, cancellationToken);
                            }
                            processedCount++;
                            processedFiles.Add($"{fileName} (truncated from {fileInfo.Length:N0} bytes)");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to truncate {fileName}: {ex.Message}", nameof(GameRecoveryService));
                            // Fallback: delete the file
                            try
                            {
                                File.Delete(filePath);
                                processedCount++;
                                processedFiles.Add($"{fileName} (deleted, was {fileInfo.Length:N0} bytes)");
                            }
                            catch (Exception ex2)
                            {
                                _logger.LogError($"Failed to delete {fileName}: {ex2.Message}", nameof(GameRecoveryService));
                            }
                        }
                    }
                    else
                    {
                        processedFiles.Add($"{fileName} ({fileInfo.Length:N0} bytes, under threshold)");
                    }
                }

                _logger.LogInfo("CleanLogFilesAsync completed successfully", nameof(GameRecoveryService));
                return GameRecoveryResult.Ok($"Log files cleaned. Processed {processedCount} large log files.", processedCount, new Dictionary<string, object> { ["processedFiles"] = processedFiles });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("CleanLogFilesAsync cancelled", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Operation cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError($"CleanLogFilesAsync failed: {ex.Message}", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail($"Clean failed: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<GameRecoveryResult> ResetDisplayResolutionAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("ResetDisplayResolutionAsync started", nameof(GameRecoveryService));

            var configPath = _pathService.GetConfigPath();
            if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
            {
                _logger.LogError("ResetDisplayResolutionAsync: Config folder not found", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Config folder not found");
            }

            var prefsPath = Path.Combine(configPath, "Prefs.xml");
            if (!File.Exists(prefsPath))
            {
                _logger.LogWarning("ResetDisplayResolutionAsync: Prefs.xml not found", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Prefs.xml not found");
            }

            try
            {
                var doc = XDocument.Load(prefsPath);
                var root = doc.Root;
                if (root == null)
                {
                    return GameRecoveryResult.Fail("Prefs.xml has no root element");
                }

                // Display-related keys to reset (wiki-style Screenmanager keys + common variants)
                // Actual Prefs.xml uses underscores in element names (spaces are not valid in XML names)
                var displayKeys = new Dictionary<string, string>
                {
                    { "screenWidth", "1280" },
                    { "screenHeight", "720" },
                    { "fullscreen", "False" },
                    { "uiScale", "1.0" },
                    { "windowPositionX", "0" },
                    { "windowPositionY", "0" },
                    // Wiki-style keys (with underscores - actual format in Prefs.xml)
                    { "Screenmanager_Is_Fullscreen_mode", "0" },
                    { "Screenmanager_Resolution_Width", "1280" },
                    { "Screenmanager_Resolution_Height", "720" },
                    { "Screenmanager_Resolution_Width_HD", "1280" },
                    { "Screenmanager_Resolution_Height_HD", "720" }
                };

                int modifiedCount = 0;
                var modifiedKeys = new List<string>();

                foreach (var kvp in displayKeys)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var element = root.Element(kvp.Key);
                    if (element != null)
                    {
                        element.Value = kvp.Value;
                        modifiedCount++;
                        modifiedKeys.Add(kvp.Key);
                    }
                }

                if (modifiedCount > 0)
                {
                    doc.Save(prefsPath);
                    _logger.LogInfo($"ResetDisplayResolutionAsync: Modified {modifiedCount} display keys", nameof(GameRecoveryService));
                    // Log all keys found in Prefs.xml for debugging
                    var allKeys = root.Elements().Select(e => e.Name.LocalName).ToList();
                    _logger.LogDebug($"Prefs.xml keys found: {string.Join(", ", allKeys)}", nameof(GameRecoveryService));
                }
                else
                {
                    _logger.LogWarning("ResetDisplayResolutionAsync: No display keys found to modify", nameof(GameRecoveryService));
                    var allKeys = root.Elements().Select(e => e.Name.LocalName).ToList();
                    _logger.LogDebug($"Prefs.xml keys found: {string.Join(", ", allKeys)}", nameof(GameRecoveryService));
                }

                _logger.LogInfo("ResetDisplayResolutionAsync completed successfully", nameof(GameRecoveryService));
                return GameRecoveryResult.Ok($"Display settings reset. Modified {modifiedCount} keys.", modifiedCount, new Dictionary<string, object> { ["modifiedKeys"] = modifiedKeys });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("ResetDisplayResolutionAsync cancelled", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Operation cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError($"ResetDisplayResolutionAsync failed: {ex.Message}", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail($"Reset failed: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<GameRecoveryResult> ClearModRuntimeCachesAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("ClearModRuntimeCachesAsync started", nameof(GameRecoveryService));

            var clearedPaths = new List<string>();
            int clearedCount = 0;

            try
            {
                // 1. PrepatcherCache in game root
                if (!string.IsNullOrEmpty(_gameInstance.GameFolder))
                {
                    var prepatcherCache = Path.Combine(_gameInstance.GameFolder, "PrepatcherCache");
                    if (Directory.Exists(prepatcherCache))
                    {
                        try
                        {
                            Directory.Delete(prepatcherCache, true);
                            clearedCount++;
                            clearedPaths.Add(prepatcherCache);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to delete PrepatcherCache: {ex.Message}", nameof(GameRecoveryService));
                        }
                    }
                }

                // 2. %TEMP%/Prepatcher
                var tempPrepatcher = Path.Combine(Path.GetTempPath(), "Prepatcher");
                if (Directory.Exists(tempPrepatcher))
                {
                    try
                    {
                        Directory.Delete(tempPrepatcher, true);
                        clearedCount++;
                        clearedPaths.Add(tempPrepatcher);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to delete %TEMP%/Prepatcher: {ex.Message}", nameof(GameRecoveryService));
                    }
                }

                // 3. HugsLib in config folder
                var configPath = _pathService.GetConfigPath();
                if (!string.IsNullOrEmpty(configPath))
                {
                    var hugsLibPath = Path.Combine(configPath, "HugsLib");
                    if (Directory.Exists(hugsLibPath))
                    {
                        try
                        {
                            Directory.Delete(hugsLibPath, true);
                            clearedCount++;
                            clearedPaths.Add(hugsLibPath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to delete HugsLib folder: {ex.Message}", nameof(GameRecoveryService));
                        }
                    }
                }

                _logger.LogInfo("ClearModRuntimeCachesAsync completed successfully", nameof(GameRecoveryService));
                return GameRecoveryResult.Ok($"Mod runtime caches cleared. Removed {clearedCount} cache directories.", clearedCount, new Dictionary<string, object> { ["clearedPaths"] = clearedPaths });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("ClearModRuntimeCachesAsync cancelled", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Operation cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError($"ClearModRuntimeCachesAsync failed: {ex.Message}", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail($"Clear failed: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<GameRecoveryResult> CheckFilePermissionsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("CheckFilePermissionsAsync started", nameof(GameRecoveryService));

            var issues = new List<string>();
            var checkedPaths = new List<string>();

            try
            {
                // Check game folder
                if (!string.IsNullOrEmpty(_gameInstance.GameFolder) && Directory.Exists(_gameInstance.GameFolder))
                {
                    checkedPaths.Add(_gameInstance.GameFolder);
                    CheckDirectoryPermissions(_gameInstance.GameFolder, issues, cancellationToken);
                }

                // Check config folder
                var configPath = _pathService.GetConfigPath();
                if (!string.IsNullOrEmpty(configPath) && Directory.Exists(configPath))
                {
                    checkedPaths.Add(configPath);
                    CheckDirectoryPermissions(configPath, issues, cancellationToken);
                }

                // Check mods folder
                if (!string.IsNullOrEmpty(_gameInstance.LocalFolder) && Directory.Exists(_gameInstance.LocalFolder))
                {
                    checkedPaths.Add(_gameInstance.LocalFolder);
                    CheckDirectoryPermissions(_gameInstance.LocalFolder, issues, cancellationToken);
                }

                // Check workshop folder (if Steam)
                if (_gameInstance.IsSteamInstallation && !string.IsNullOrEmpty(_gameInstance.WorkshopFolder) && Directory.Exists(_gameInstance.WorkshopFolder))
                {
                    checkedPaths.Add(_gameInstance.WorkshopFolder);
                    CheckDirectoryPermissions(_gameInstance.WorkshopFolder, issues, cancellationToken);
                }

                var message = issues.Count > 0
                    ? $"Found {issues.Count} permission/cloud lock issues."
                    : "No permission or cloud lock issues detected.";

                _logger.LogInfo("CheckFilePermissionsAsync completed", nameof(GameRecoveryService));
                return GameRecoveryResult.Ok(message, issues.Count, new Dictionary<string, object>
                {
                    ["issues"] = issues,
                    ["checkedPaths"] = checkedPaths
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("CheckFilePermissionsAsync cancelled", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Operation cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError($"CheckFilePermissionsAsync failed: {ex.Message}", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail($"Check failed: {ex.Message}");
            }
        }

        private void CheckDirectoryPermissions(string path, List<string> issues, CancellationToken cancellationToken)
        {
            try
            {
                var dirInfo = new DirectoryInfo(path);
                var attrs = dirInfo.Attributes;

                if (attrs.HasFlag(FileAttributes.ReparsePoint))
                {
                    issues.Add($"{path}: Reparse point detected (possible OneDrive/cloud sync junction)");
                }
                if (attrs.HasFlag(FileAttributes.Offline))
                {
                    issues.Add($"{path}: Offline attribute set (file may be cloud-only/dehydrated)");
                }
                if (attrs.HasFlag(FileAttributes.ReadOnly))
                {
                    issues.Add($"{path}: Read-only attribute set on directory");
                }

                // Check a few files in the directory
                var files = dirInfo.GetFiles("*.*", SearchOption.TopDirectoryOnly).Take(10);
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fileAttrs = file.Attributes;
                    if (fileAttrs.HasFlag(FileAttributes.ReparsePoint))
                    {
                        issues.Add($"{file.FullName}: Reparse point detected");
                    }
                    if (fileAttrs.HasFlag(FileAttributes.Offline))
                    {
                        issues.Add($"{file.FullName}: Offline attribute set (cloud-only)");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to check permissions for {path}: {ex.Message}", nameof(GameRecoveryService));
            }
        }

        /// <inheritdoc />
        public async Task<GameRecoveryResult> ScanMissingAssembliesAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("ScanMissingAssembliesAsync started", nameof(GameRecoveryService));

            var missingAssemblies = new List<string>();
            var checkedMods = new List<string>();

            try
            {
                // Check local mods folder
                if (!string.IsNullOrEmpty(_gameInstance.LocalFolder) && Directory.Exists(_gameInstance.LocalFolder))
                {
                    var modDirs = new DirectoryInfo(_gameInstance.LocalFolder).GetDirectories();
                    foreach (var modDir in modDirs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        checkedMods.Add(modDir.Name);
                        var assemblies = modDir.GetFiles("*.dll", SearchOption.AllDirectories);
                        foreach (var asm in assemblies)
                        {
                            // Check if file is accessible (not quarantined/zero-byte)
                            try
                            {
                                using var fs = File.OpenRead(asm.FullName);
                                if (fs.Length == 0)
                                {
                                    missingAssemblies.Add($"{modDir.Name}/{asm.Name}: Zero-byte assembly (possible AV quarantine)");
                                }
                            }
                            catch (UnauthorizedAccessException)
                            {
                                missingAssemblies.Add($"{modDir.Name}/{asm.Name}: Access denied (possible AV quarantine)");
                            }
                            catch (IOException)
                            {
                                missingAssemblies.Add($"{modDir.Name}/{asm.Name}: I/O error reading assembly");
                            }
                        }
                    }
                }

                // Check workshop mods folder
                if (_gameInstance.IsSteamInstallation && !string.IsNullOrEmpty(_gameInstance.WorkshopFolder) && Directory.Exists(_gameInstance.WorkshopFolder))
                {
                    var modDirs = new DirectoryInfo(_gameInstance.WorkshopFolder).GetDirectories();
                    foreach (var modDir in modDirs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        checkedMods.Add($"workshop/{modDir.Name}");
                        var assemblies = modDir.GetFiles("*.dll", SearchOption.AllDirectories);
                        foreach (var asm in assemblies)
                        {
                            try
                            {
                                using var fs = File.OpenRead(asm.FullName);
                                if (fs.Length == 0)
                                {
                                    missingAssemblies.Add($"workshop/{modDir.Name}/{asm.Name}: Zero-byte assembly (possible AV quarantine)");
                                }
                            }
                            catch (UnauthorizedAccessException)
                            {
                                missingAssemblies.Add($"workshop/{modDir.Name}/{asm.Name}: Access denied (possible AV quarantine)");
                            }
                            catch (IOException)
                            {
                                missingAssemblies.Add($"workshop/{modDir.Name}/{asm.Name}: I/O error reading assembly");
                            }
                        }
                    }
                }

                var message = missingAssemblies.Count > 0
                    ? $"Found {missingAssemblies.Count} missing/inaccessible assemblies across {checkedMods.Count} mods."
                    : "All mod assemblies accessible.";

                _logger.LogInfo("ScanMissingAssembliesAsync completed", nameof(GameRecoveryService));
                return GameRecoveryResult.Ok(message, missingAssemblies.Count, new Dictionary<string, object>
                {
                    ["missingAssemblies"] = missingAssemblies,
                    ["checkedMods"] = checkedMods
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("ScanMissingAssembliesAsync cancelled", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Operation cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError($"ScanMissingAssembliesAsync failed: {ex.Message}", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail($"Scan failed: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<GameRecoveryResult> VerifyModSettingsXmlAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("VerifyModSettingsXmlAsync started", nameof(GameRecoveryService));

            var configPath = _pathService.GetConfigPath();
            if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
            {
                _logger.LogError("VerifyModSettingsXmlAsync: Config folder not found", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Config folder not found");
            }

            var corruptFiles = new List<string>();
            var checkedFiles = new List<string>();

            try
            {
                var configDir = new DirectoryInfo(configPath);
                var modSettingsFiles = configDir.GetFiles("Mod_*.xml");

                foreach (var file in modSettingsFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    checkedFiles.Add(file.Name);

                    try
                    {
                        if (file.Length == 0)
                        {
                            corruptFiles.Add($"{file.Name}: Zero-byte file");
                            continue;
                        }

                        // Check for NUL bytes
                        using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                        var buffer = new byte[Math.Min(file.Length, 1024)];
                        await fs.ReadExactlyAsync(buffer, cancellationToken);
                        if (buffer.Any(b => b == 0))
                        {
                            corruptFiles.Add($"{file.Name}: Contains NUL bytes");
                            continue;
                        }

                        // Try to parse as XML
                        var doc = XDocument.Load(file.FullName);
                    }
                    catch (System.Xml.XmlException ex)
                    {
                        corruptFiles.Add($"{file.Name}: Malformed XML - {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        corruptFiles.Add($"{file.Name}: Error reading - {ex.Message}");
                    }
                }

                var message = corruptFiles.Count > 0
                    ? $"Found {corruptFiles.Count} corrupt Mod_*.xml files out of {checkedFiles.Count} checked."
                    : $"All {checkedFiles.Count} Mod_*.xml files are valid.";

                _logger.LogInfo("VerifyModSettingsXmlAsync completed", nameof(GameRecoveryService));
                return GameRecoveryResult.Ok(message, corruptFiles.Count, new Dictionary<string, object>
                {
                    ["corruptFiles"] = corruptFiles,
                    ["checkedFiles"] = checkedFiles
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("VerifyModSettingsXmlAsync cancelled", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Operation cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError($"VerifyModSettingsXmlAsync failed: {ex.Message}", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail($"Verify failed: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<GameRecoveryResult> CheckPathCompatibilityAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("CheckPathCompatibilityAsync started", nameof(GameRecoveryService));

            var issues = new List<string>();
            var checkedPaths = new List<string>();

            try
            {
                var pathsToCheck = new Dictionary<string, string>
                {
                    ["GameFolder"] = _gameInstance.GameFolder ?? "",
                    ["ConfigFolder"] = _pathService.GetConfigPath() ?? "",
                    ["LocalFolder"] = _gameInstance.LocalFolder ?? "",
                    ["WorkshopFolder"] = _gameInstance.WorkshopFolder ?? ""
                };

                foreach (var (name, path) in pathsToCheck)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(path))
                        continue;

                    checkedPaths.Add($"{name}: {path}");

                    // Check for non-ASCII characters
                    for (int i = 0; i < path.Length; i++)
                    {
                        if (path[i] > 127)
                        {
                            issues.Add($"{name} contains non-ASCII character '{path[i]}' (U+{(int)path[i]:X4}) at position {i}");
                            break; // Report once per path
                        }
                    }

                    // Check for problematic characters
                    if (path.Contains('[') || path.Contains(']') || path.Contains('{') || path.Contains('}'))
                    {
                        issues.Add($"{name} contains bracket characters which can cause issues with some tools");
                    }
                }

                var message = issues.Count > 0
                    ? $"Found {issues.Count} path compatibility issues."
                    : "All paths are ASCII-compatible.";

                _logger.LogInfo("CheckPathCompatibilityAsync completed", nameof(GameRecoveryService));
                return GameRecoveryResult.Ok(message, issues.Count, new Dictionary<string, object>
                {
                    ["issues"] = issues,
                    ["checkedPaths"] = checkedPaths
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("CheckPathCompatibilityAsync cancelled", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail("Operation cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError($"CheckPathCompatibilityAsync failed: {ex.Message}", nameof(GameRecoveryService));
                return GameRecoveryResult.Fail($"Check failed: {ex.Message}");
            }
        }
    }
}