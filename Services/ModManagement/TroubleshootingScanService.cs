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
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Services.Workshop;

namespace RimSharp.Services.ModManagement
{
    /// <summary>
    /// Service for running fast, read-only troubleshooting scans.
    /// Implements the wizard scan checklist from the UX spec (section 7).
    /// </summary>
    public class TroubleshootingScanService : ITroubleshootingScanService
    {
        private readonly GameInstance _gameInstance;
        private readonly ILoggerService _logger;
        private readonly IPathService _pathService;
        private readonly ISteamSubscriptionService _steamSubscriptionService;

        // Test seams for gating checks
        internal static Func<bool>? TestIsSteamInstallationOverride { get; set; }
        internal static Func<bool>? TestIsWindowsOverride { get; set; }
        internal static Func<string>? TestGameVersionOverride { get; set; }
        internal static Func<IEnumerable<Process>>? TestGetProcessesOverride { get; set; }

        public TroubleshootingScanService(
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

        /// <inheritdoc />
        public async Task<IReadOnlyList<ScanFinding>> RunScanAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("Troubleshooting scan started", nameof(TroubleshootingScanService));

            var findings = new List<ScanFinding>();
            var stopwatch = Stopwatch.StartNew();

            // Determine gate states
            var isSteamInstallation = TestIsSteamInstallationOverride?.Invoke() ?? _gameInstance.IsSteamInstallation;
            var isWindows = TestIsWindowsOverride?.Invoke() ?? RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var gameVersion = TestGameVersionOverride?.Invoke() ?? _pathService.GetGameVersion() ?? "Unknown";
            var unityCrashHandlerRelevant = IsUnityCrashHandlerRelevant(gameVersion);

            try
            {
                // Check 1: Game path & config path resolve
                findings.Add(CheckPathsResolve());

                // Check 2: Steam installation detected
                findings.Add(CheckSteamInstallation(isSteamInstallation));

                // Check 3: Game version read
                findings.Add(CheckGameVersion(gameVersion));

                // Check 4: Player.log / Player-prev.log size
                findings.Add(await CheckLogFileSizesAsync(cancellationToken));

                // Check 5: Log signature scan via LogIssueDetector
                findings.AddRange(await CheckLogSignaturesAsync(cancellationToken));

                // Check 6: Running RimWorld processes
                findings.AddRange(CheckRunningProcesses(isWindows, unityCrashHandlerRelevant));

                // Check 7: Config folder attributes (Windows only)
                if (isWindows)
                {
                    findings.Add(await CheckConfigFolderAttributesAsync(cancellationToken));
                }

                // Check 8: Mod assembly files present vs referenced
                findings.Add(await CheckModAssembliesAsync(cancellationToken));

                // Check 9: Mod_*.xml files integrity
                findings.Add(await CheckModSettingsXmlAsync(cancellationToken));

                // Check 10: Path contains non-ASCII chars
                findings.Add(CheckPathCompatibility());

                // Steam-gated checks (skipped silently when not Steam)
                if (isSteamInstallation)
                {
                    // Check 11: Workshop content folder present + 0-byte mods
                    findings.Add(await CheckWorkshopModsAsync(cancellationToken));

                    // Check 12: appworkshop_294100.acf vs filesystem cross-reference
                    findings.Add(await CheckOrphanedWorkshopModsAsync(cancellationToken));

                    // Check 13: steamapps/downloading + workshop staging dirs
                    findings.Add(await CheckStuckDownloadsAsync(cancellationToken));
                }

                // Check 14: ModsConfig.xml integrity
                findings.Add(await CheckModsConfigIntegrityAsync(cancellationToken));

                // Check 15: Saves folder size
                findings.Add(await CheckSavesFolderSizeAsync(cancellationToken));

                stopwatch.Stop();
                _logger.LogInfo($"Troubleshooting scan completed in {stopwatch.ElapsedMilliseconds}ms with {findings.Count} findings", nameof(TroubleshootingScanService));

                return findings;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("Troubleshooting scan cancelled", nameof(TroubleshootingScanService));
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Troubleshooting scan failed: {ex.Message}", nameof(TroubleshootingScanService));
                throw;
            }
        }

        private ScanFinding CheckPathsResolve()
        {
            var issues = new List<string>();
            if (string.IsNullOrEmpty(_gameInstance.GameFolder) || !Directory.Exists(_gameInstance.GameFolder))
                issues.Add("Game folder not configured or missing");
            if (string.IsNullOrEmpty(_pathService.GetConfigPath()) || !Directory.Exists(_pathService.GetConfigPath()))
                issues.Add("Config folder not configured or missing");

            return new ScanFinding
            {
                CheckId = "paths_resolve",
                Passed = issues.Count == 0,
                Severity = issues.Count > 0 ? ScanSeverity.Error : ScanSeverity.Info,
                Title = "Game and config paths",
                Detail = issues.Count > 0 ? string.Join("; ", issues) : "All paths resolved successfully",
                SuggestedActionId = issues.Count > 0 ? "configure_paths" : null
            };
        }

        private ScanFinding CheckSteamInstallation(bool isSteamInstallation)
        {
            return new ScanFinding
            {
                CheckId = "steam_installation",
                Passed = true, // Not a failure, just informational
                Severity = ScanSeverity.Info,
                Title = "Steam installation",
                Detail = isSteamInstallation ? "Steam installation detected" : "Non-Steam installation (Steam Utilities hidden)",
                SuggestedActionId = null
            };
        }

        private ScanFinding CheckGameVersion(string gameVersion)
        {
            return new ScanFinding
            {
                CheckId = "game_version",
                Passed = true,
                Severity = ScanSeverity.Info,
                Title = "Game version",
                Detail = $"Detected: {gameVersion}",
                SuggestedActionId = null,
                Metadata = new Dictionary<string, object> { ["version"] = gameVersion }
            };
        }

        private async Task<ScanFinding> CheckLogFileSizesAsync(CancellationToken cancellationToken)
        {
            var configPath = _pathService.GetConfigPath();
            if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
            {
                return new ScanFinding
                {
                    CheckId = "log_file_sizes",
                    Passed = true,
                    Severity = ScanSeverity.Info,
                    Title = "Log file sizes",
                    Detail = "Config folder not accessible"
                };
            }

            const long Threshold = 50 * 1024 * 1024; // 50 MB
            var largeLogs = new List<string>();

            foreach (var fileName in new[] { "Player.log", "Player-prev.log" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filePath = Path.Combine(configPath, fileName);
                if (File.Exists(filePath))
                {
                    var size = new FileInfo(filePath).Length;
                    if (size > Threshold)
                    {
                        largeLogs.Add($"{fileName} ({size / (1024 * 1024)} MB)");
                    }
                }
            }

            return new ScanFinding
            {
                CheckId = "log_file_sizes",
                Passed = largeLogs.Count == 0,
                Severity = largeLogs.Count > 0 ? ScanSeverity.Warning : ScanSeverity.Info,
                Title = "Log file sizes",
                Detail = largeLogs.Count > 0 ? $"Large logs: {string.Join(", ", largeLogs)}" : "All logs under 50 MB threshold",
                SuggestedActionId = largeLogs.Count > 0 ? "clean_log_files" : null,
                Metadata = new Dictionary<string, object> { ["largeLogs"] = largeLogs }
            };
        }

        private async Task<IReadOnlyList<ScanFinding>> CheckLogSignaturesAsync(CancellationToken cancellationToken)
        {
            // This would integrate with the existing LogIssueDetector pipeline
            // For now, return empty - the UI layer handles log analysis
            await Task.CompletedTask;
            return Array.Empty<ScanFinding>();
        }

        private IReadOnlyList<ScanFinding> CheckRunningProcesses(bool isWindows, bool unityCrashHandlerRelevant)
        {
            var findings = new List<ScanFinding>();
            var zombieProcesses = new List<string>();

            try
            {
                var processes = TestGetProcessesOverride?.Invoke() ?? Process.GetProcesses();
                var processNames = GameExecutableNames.GetProcessNames();

                foreach (var process in processes)
                {
                    try
                    {
                        var name = process.ProcessName;
                        if (processNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        {
                            zombieProcesses.Add($"{name} (PID: {process.Id})");
                        }
                        else if (isWindows && unityCrashHandlerRelevant && name.Equals("UnityCrashHandler64", StringComparison.OrdinalIgnoreCase))
                        {
                            zombieProcesses.Add($"{name} (PID: {process.Id})");
                        }
                    }
                    catch { /* Access denied on some processes */ }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to enumerate processes: {ex.Message}", nameof(TroubleshootingScanService));
            }

            findings.Add(new ScanFinding
            {
                CheckId = "running_processes",
                Passed = zombieProcesses.Count == 0,
                Severity = zombieProcesses.Count > 0 ? ScanSeverity.Warning : ScanSeverity.Info,
                Title = "Running RimWorld processes",
                Detail = zombieProcesses.Count > 0 ? $"Found: {string.Join(", ", zombieProcesses)}" : "No zombie processes detected",
                SuggestedActionId = zombieProcesses.Count > 0 ? "kill_zombie_processes" : null,
                Metadata = new Dictionary<string, object> { ["processes"] = zombieProcesses }
            });

            return findings;
        }

        private async Task<ScanFinding> CheckConfigFolderAttributesAsync(CancellationToken cancellationToken)
        {
            var configPath = _pathService.GetConfigPath();
            if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
            {
                return new ScanFinding
                {
                    CheckId = "config_folder_attributes",
                    Passed = true,
                    Severity = ScanSeverity.Info,
                    Title = "Config folder attributes (OneDrive/cloud)",
                    Detail = "Config folder not accessible"
                };
            }

            var issues = new List<string>();
            try
            {
                var dirInfo = new DirectoryInfo(configPath);
                var attrs = dirInfo.Attributes;

                if (attrs.HasFlag(FileAttributes.ReparsePoint))
                    issues.Add("Reparse point detected (possible OneDrive junction)");
                if (attrs.HasFlag(FileAttributes.Offline))
                    issues.Add("Offline attribute set (cloud-only/dehydrated)");
                if (attrs.HasFlag(FileAttributes.ReadOnly))
                    issues.Add("Read-only attribute set on directory");

                // Check files
                var files = dirInfo.GetFiles("*.*", SearchOption.TopDirectoryOnly).Take(20);
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fileAttrs = file.Attributes;
                    if (fileAttrs.HasFlag(FileAttributes.ReparsePoint))
                        issues.Add($"{file.Name}: Reparse point");
                    if (fileAttrs.HasFlag(FileAttributes.Offline))
                        issues.Add($"{file.Name}: Offline (cloud-only)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to check config folder attributes: {ex.Message}", nameof(TroubleshootingScanService));
            }

            return new ScanFinding
            {
                CheckId = "config_folder_attributes",
                Passed = issues.Count == 0,
                Severity = issues.Count > 0 ? ScanSeverity.Warning : ScanSeverity.Info,
                Title = "Config folder attributes (OneDrive/cloud)",
                Detail = issues.Count > 0 ? string.Join("; ", issues) : "No cloud lock attributes detected",
                SuggestedActionId = issues.Count > 0 ? "check_file_permissions" : null,
                Metadata = new Dictionary<string, object> { ["issues"] = issues }
            };
        }

        private async Task<ScanFinding> CheckModAssembliesAsync(CancellationToken cancellationToken)
        {
            var missingAssemblies = new List<string>();
            var checkedMods = 0;

            try
            {
                // Check local mods
                if (!string.IsNullOrEmpty(_gameInstance.LocalFolder) && Directory.Exists(_gameInstance.LocalFolder))
                {
                    var modDirs = new DirectoryInfo(_gameInstance.LocalFolder).GetDirectories();
                    foreach (var modDir in modDirs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        checkedMods++;
                        var assemblies = modDir.GetFiles("*.dll", SearchOption.AllDirectories);
                        foreach (var asm in assemblies)
                        {
                            try
                            {
                                using var fs = File.OpenRead(asm.FullName);
                                if (fs.Length == 0)
                                    missingAssemblies.Add($"{modDir.Name}/{asm.Name}: Zero-byte");
                            }
                            catch (UnauthorizedAccessException)
                            {
                                missingAssemblies.Add($"{modDir.Name}/{asm.Name}: Access denied");
                            }
                            catch (IOException)
                            {
                                missingAssemblies.Add($"{modDir.Name}/{asm.Name}: I/O error");
                            }
                        }
                    }
                }

                // Check workshop mods
                if (_gameInstance.IsSteamInstallation && !string.IsNullOrEmpty(_gameInstance.WorkshopFolder) && Directory.Exists(_gameInstance.WorkshopFolder))
                {
                    var modDirs = new DirectoryInfo(_gameInstance.WorkshopFolder).GetDirectories();
                    foreach (var modDir in modDirs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        checkedMods++;
                        var assemblies = modDir.GetFiles("*.dll", SearchOption.AllDirectories);
                        foreach (var asm in assemblies)
                        {
                            try
                            {
                                using var fs = File.OpenRead(asm.FullName);
                                if (fs.Length == 0)
                                    missingAssemblies.Add($"workshop/{modDir.Name}/{asm.Name}: Zero-byte");
                            }
                            catch (UnauthorizedAccessException)
                            {
                                missingAssemblies.Add($"workshop/{modDir.Name}/{asm.Name}: Access denied");
                            }
                            catch (IOException)
                            {
                                missingAssemblies.Add($"workshop/{modDir.Name}/{asm.Name}: I/O error");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to check mod assemblies: {ex.Message}", nameof(TroubleshootingScanService));
            }

            return new ScanFinding
            {
                CheckId = "mod_assemblies",
                Passed = missingAssemblies.Count == 0,
                Severity = missingAssemblies.Count > 0 ? ScanSeverity.Error : ScanSeverity.Info,
                Title = "Mod assemblies",
                Detail = missingAssemblies.Count > 0 ? $"{missingAssemblies.Count} missing/inaccessible assemblies in {checkedMods} mods" : $"All assemblies accessible in {checkedMods} mods",
                SuggestedActionId = missingAssemblies.Count > 0 ? "scan_missing_assemblies" : null,
                Metadata = new Dictionary<string, object> { ["missing"] = missingAssemblies, ["checkedMods"] = checkedMods }
            };
        }

        private async Task<ScanFinding> CheckModSettingsXmlAsync(CancellationToken cancellationToken)
        {
            var configPath = _pathService.GetConfigPath();
            if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
            {
                return new ScanFinding
                {
                    CheckId = "mod_settings_xml",
                    Passed = true,
                    Severity = ScanSeverity.Info,
                    Title = "Mod settings XML integrity",
                    Detail = "Config folder not accessible"
                };
            }

            var corruptFiles = new List<string>();
            var checkedFiles = 0;

            try
            {
                var configDir = new DirectoryInfo(configPath);
                var modSettingsFiles = configDir.GetFiles("Mod_*.xml");

                foreach (var file in modSettingsFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    checkedFiles++;

                    try
                    {
                        if (file.Length == 0)
                        {
                            corruptFiles.Add($"{file.Name}: Zero-byte");
                            continue;
                        }

                        using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                        var buffer = new byte[Math.Min(file.Length, 1024)];
                        await fs.ReadExactlyAsync(buffer, cancellationToken);
                        if (buffer.Any(b => b == 0))
                        {
                            corruptFiles.Add($"{file.Name}: NUL bytes");
                            continue;
                        }

                        XDocument.Load(file.FullName);
                    }
                    catch (System.Xml.XmlException ex)
                    {
                        corruptFiles.Add($"{file.Name}: Malformed XML - {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        corruptFiles.Add($"{file.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to check mod settings XML: {ex.Message}", nameof(TroubleshootingScanService));
            }

            return new ScanFinding
            {
                CheckId = "mod_settings_xml",
                Passed = corruptFiles.Count == 0,
                Severity = corruptFiles.Count > 0 ? ScanSeverity.Error : ScanSeverity.Info,
                Title = "Mod settings XML integrity",
                Detail = corruptFiles.Count > 0 ? $"{corruptFiles.Count} corrupt files of {checkedFiles} checked" : $"All {checkedFiles} Mod_*.xml files valid",
                SuggestedActionId = corruptFiles.Count > 0 ? "verify_mod_settings_xml" : null,
                Metadata = new Dictionary<string, object> { ["corruptFiles"] = corruptFiles, ["checkedFiles"] = checkedFiles }
            };
        }

        private ScanFinding CheckPathCompatibility()
        {
            var issues = new List<string>();
            var checkedPaths = new List<string>();

            var pathsToCheck = new Dictionary<string, string>
            {
                ["GameFolder"] = _gameInstance.GameFolder ?? "",
                ["ConfigFolder"] = _pathService.GetConfigPath() ?? "",
                ["LocalFolder"] = _gameInstance.LocalFolder ?? "",
                ["WorkshopFolder"] = _gameInstance.WorkshopFolder ?? ""
            };

            foreach (var (name, path) in pathsToCheck)
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                checkedPaths.Add($"{name}: {path}");

                for (int i = 0; i < path.Length; i++)
                {
                    if (path[i] > 127)
                    {
                        issues.Add($"{name} contains non-ASCII character U+{(int)path[i]:X4}");
                        break;
                    }
                }
            }

            return new ScanFinding
            {
                CheckId = "path_compatibility",
                Passed = issues.Count == 0,
                Severity = issues.Count > 0 ? ScanSeverity.Error : ScanSeverity.Info,
                Title = "Path compatibility",
                Detail = issues.Count > 0 ? string.Join("; ", issues) : "All paths ASCII-compatible",
                SuggestedActionId = issues.Count > 0 ? "check_path_compatibility" : null,
                Metadata = new Dictionary<string, object> { ["issues"] = issues, ["checkedPaths"] = checkedPaths }
            };
        }

        private async Task<ScanFinding> CheckWorkshopModsAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_gameInstance.WorkshopFolder) || !Directory.Exists(_gameInstance.WorkshopFolder))
            {
                return new ScanFinding
                {
                    CheckId = "workshop_mods",
                    Passed = true,
                    Severity = ScanSeverity.Info,
                    Title = "Workshop mods",
                    Detail = "Workshop folder not accessible"
                };
            }

            var zeroByteMods = new List<string>();
            var totalMods = 0;

            try
            {
                var modDirs = new DirectoryInfo(_gameInstance.WorkshopFolder).GetDirectories();
                foreach (var modDir in modDirs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    totalMods++;

                    // Check for 0-byte content (no files or all zero-byte)
                    var files = modDir.GetFiles("*.*", SearchOption.AllDirectories);
                    if (files.Length == 0)
                    {
                        zeroByteMods.Add($"{modDir.Name}: Empty directory");
                    }
                    else
                    {
                        var allZeroByte = files.All(f => f.Length == 0);
                        if (allZeroByte)
                        {
                            zeroByteMods.Add($"{modDir.Name}: All files zero-byte");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to check workshop mods: {ex.Message}", nameof(TroubleshootingScanService));
            }

            return new ScanFinding
            {
                CheckId = "workshop_mods",
                Passed = zeroByteMods.Count == 0,
                Severity = zeroByteMods.Count > 0 ? ScanSeverity.Warning : ScanSeverity.Info,
                Title = "Workshop mods",
                Detail = zeroByteMods.Count > 0 ? $"{zeroByteMods.Count} stuck/0-byte mods of {totalMods} total" : $"{totalMods} workshop mods appear healthy",
                SuggestedActionId = zeroByteMods.Count > 0 ? "reset_steam_workshop_mods" : null,
                Metadata = new Dictionary<string, object> { ["zeroByteMods"] = zeroByteMods, ["totalMods"] = totalMods }
            };
        }

        private async Task<ScanFinding> CheckOrphanedWorkshopModsAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_gameInstance.WorkshopFolder) || !Directory.Exists(_gameInstance.WorkshopFolder))
            {
                return new ScanFinding
                {
                    CheckId = "orphaned_workshop_mods",
                    Passed = true,
                    Severity = ScanSeverity.Info,
                    Title = "Orphaned workshop mods (ACF vs filesystem)",
                    Detail = "Workshop folder not accessible"
                };
            }

            var orphanedMods = new List<string>();

            try
            {
                // Find ACF file: workshop_folder/../../appworkshop_294100.acf
                // Workshop folder is: steamapps/workshop/content/294100
                // ACF is at: steamapps/workshop/appworkshop_294100.acf
                // Need to go up 2 levels: 294100 -> content -> workshop
                var workshopDir = new DirectoryInfo(_gameInstance.WorkshopFolder);
                var acfPath = Path.Combine(workshopDir.Parent?.Parent?.FullName ?? "", "appworkshop_294100.acf");

                if (!File.Exists(acfPath))
                {
                    return new ScanFinding
                    {
                        CheckId = "orphaned_workshop_mods",
                        Passed = true,
                        Severity = ScanSeverity.Info,
                        Title = "Orphaned workshop mods (ACF vs filesystem)",
                        Detail = "ACF file not found"
                    };
                }

                // Parse ACF using existing parser
                var parser = new RimSharp.Infrastructure.Workshop.AcfFileParser();
                if (parser.TryParse(acfPath, out var acfData, out _))
                {
                    var installedIds = acfData.Items.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var modDirs = new DirectoryInfo(_gameInstance.WorkshopFolder).GetDirectories();
                    var onDiskIds = modDirs.Where(d => ulong.TryParse(d.Name, out _)).Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

                    // Find PFIDs in ACF but not on disk
                    foreach (var id in installedIds)
                    {
                        if (!onDiskIds.Contains(id))
                        {
                            orphanedMods.Add(id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to check orphaned workshop mods: {ex.Message}", nameof(TroubleshootingScanService));
            }

            return new ScanFinding
            {
                CheckId = "orphaned_workshop_mods",
                Passed = orphanedMods.Count == 0,
                Severity = orphanedMods.Count > 0 ? ScanSeverity.Warning : ScanSeverity.Info,
                Title = "Orphaned workshop mods (ACF vs filesystem)",
                Detail = orphanedMods.Count > 0 ? $"{orphanedMods.Count} ghost mods in ACF" : "ACF matches filesystem",
                SuggestedActionId = orphanedMods.Count > 0 ? "clean_orphaned_workshop_mods" : null,
                Metadata = new Dictionary<string, object> { ["orphanedMods"] = orphanedMods }
            };
        }

        private async Task<ScanFinding> CheckStuckDownloadsAsync(CancellationToken cancellationToken)
        {
            var stuckPaths = new List<string>();

            try
            {
                // Find steamapps root from workshop folder
                // Workshop folder is: steamapps/workshop/content/294100
                // Need to go up 3 levels: 294100 -> content -> workshop -> steamapps
                if (!string.IsNullOrEmpty(_gameInstance.WorkshopFolder))
                {
                    var workshopDir = new DirectoryInfo(_gameInstance.WorkshopFolder);
                    var steamAppsDir = workshopDir.Parent?.Parent?.Parent; // 294100 -> content -> workshop -> steamapps

                    if (steamAppsDir != null && steamAppsDir.Exists)
                    {
                        // Check steamapps/downloading
                        var downloadingDir = new DirectoryInfo(Path.Combine(steamAppsDir.FullName, "downloading"));
                        if (downloadingDir.Exists && downloadingDir.GetFileSystemInfos().Length > 0)
                        {
                            stuckPaths.Add($"steamapps/downloading: {downloadingDir.GetFileSystemInfos().Length} items");
                        }

                        // Check workshop/downloads
                        var workshopDownloads = new DirectoryInfo(Path.Combine(steamAppsDir.FullName, "workshop", "downloads"));
                        if (workshopDownloads.Exists && workshopDownloads.GetFileSystemInfos().Length > 0)
                        {
                            stuckPaths.Add($"workshop/downloads: {workshopDownloads.GetFileSystemInfos().Length} items");
                        }

                        // Check workshop/temp/294100
                        var workshopTemp = new DirectoryInfo(Path.Combine(steamAppsDir.FullName, "workshop", "temp", "294100"));
                        if (workshopTemp.Exists && workshopTemp.GetFileSystemInfos().Length > 0)
                        {
                            stuckPaths.Add($"workshop/temp/294100: {workshopTemp.GetFileSystemInfos().Length} items");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to check stuck downloads: {ex.Message}", nameof(TroubleshootingScanService));
            }

            return new ScanFinding
            {
                CheckId = "stuck_downloads",
                Passed = stuckPaths.Count == 0,
                Severity = stuckPaths.Count > 0 ? ScanSeverity.Warning : ScanSeverity.Info,
                Title = "Stuck downloads / staging corruption",
                Detail = stuckPaths.Count > 0 ? string.Join("; ", stuckPaths) : "No stuck download staging files",
                SuggestedActionId = stuckPaths.Count > 0 ? "clear_download_cache" : null,
                Metadata = new Dictionary<string, object> { ["stuckPaths"] = stuckPaths }
            };
        }

        private async Task<ScanFinding> CheckModsConfigIntegrityAsync(CancellationToken cancellationToken)
        {
            var configPath = _pathService.GetConfigPath();
            if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
            {
                return new ScanFinding
                {
                    CheckId = "mods_config_integrity",
                    Passed = false,
                    Severity = ScanSeverity.Error,
                    Title = "ModsConfig.xml integrity",
                    Detail = "Config folder not accessible"
                };
            }

            var modsConfigPath = Path.Combine(configPath, "ModsConfig.xml");
            if (!File.Exists(modsConfigPath))
            {
                return new ScanFinding
                {
                    CheckId = "mods_config_integrity",
                    Passed = false,
                    Severity = ScanSeverity.Error,
                    Title = "ModsConfig.xml integrity",
                    Detail = "ModsConfig.xml not found",
                    SuggestedActionId = "import_mod_list"
                };
            }

            try
            {
                var doc = XDocument.Load(modsConfigPath);
                var root = doc.Root;
                if (root == null || root.Name != "ModsConfigData")
                {
                    return new ScanFinding
                    {
                        CheckId = "mods_config_integrity",
                        Passed = false,
                        Severity = ScanSeverity.Error,
                        Title = "ModsConfig.xml integrity",
                        Detail = "Invalid XML structure",
                        SuggestedActionId = "import_mod_list"
                    };
                }

                var activeMods = root.Element("activeMods")?.Elements("li").Select(x => x.Value).ToList() ?? new List<string>();
                var hasCore = activeMods.Any(id => id.Equals("ludeon.rimworld", StringComparison.OrdinalIgnoreCase));

                return new ScanFinding
                {
                    CheckId = "mods_config_integrity",
                    Passed = hasCore,
                    Severity = hasCore ? ScanSeverity.Info : ScanSeverity.Warning,
                    Title = "ModsConfig.xml integrity",
                    Detail = hasCore ? $"Valid ({activeMods.Count} active mods)" : "Core mod (ludeon.rimworld) missing from activeMods",
                    SuggestedActionId = hasCore ? null : "import_mod_list",
                    Metadata = new Dictionary<string, object> { ["activeModCount"] = activeMods.Count, ["hasCore"] = hasCore }
                };
            }
            catch (Exception ex)
            {
                return new ScanFinding
                {
                    CheckId = "mods_config_integrity",
                    Passed = false,
                    Severity = ScanSeverity.Error,
                    Title = "ModsConfig.xml integrity",
                    Detail = $"Parse error: {ex.Message}",
                    SuggestedActionId = "import_mod_list"
                };
            }
        }

        private async Task<ScanFinding> CheckSavesFolderSizeAsync(CancellationToken cancellationToken)
        {
            var configPath = _pathService.GetConfigPath();
            if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
            {
                return new ScanFinding
                {
                    CheckId = "saves_folder_size",
                    Passed = true,
                    Severity = ScanSeverity.Info,
                    Title = "Saves folder size",
                    Detail = "Config folder not accessible"
                };
            }

            const long WarningThreshold = 500 * 1024 * 1024; // 500 MB
            var savesPath = Path.Combine(configPath, "Saves");

            if (!Directory.Exists(savesPath))
            {
                return new ScanFinding
                {
                    CheckId = "saves_folder_size",
                    Passed = true,
                    Severity = ScanSeverity.Info,
                    Title = "Saves folder size",
                    Detail = "Saves folder not found"
                };
            }

            long totalSize = 0;
            int saveCount = 0;

            try
            {
                var files = new DirectoryInfo(savesPath).GetFiles("*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    totalSize += file.Length;
                }
                saveCount = files.Length;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to calculate saves folder size: {ex.Message}", nameof(TroubleshootingScanService));
            }

            var sizeMB = totalSize / (1024 * 1024);
            var overThreshold = totalSize > WarningThreshold;

            return new ScanFinding
            {
                CheckId = "saves_folder_size",
                Passed = !overThreshold,
                Severity = overThreshold ? ScanSeverity.Warning : ScanSeverity.Info,
                Title = "Saves folder size",
                Detail = overThreshold ? $"{sizeMB} MB ({saveCount} files) - exceeds 500 MB Steam Cloud quota warning" : $"{sizeMB} MB ({saveCount} files)",
                SuggestedActionId = overThreshold ? "analyze_saves_quota" : null,
                Metadata = new Dictionary<string, object> { ["sizeBytes"] = totalSize, ["sizeMB"] = sizeMB, ["fileCount"] = saveCount }
            };
        }

        private bool IsUnityCrashHandlerRelevant(string gameVersion)
        {
            // RimWorld ≤1.6 (Unity) has UnityCrashHandler64; 1.7+ (Waylith) does not
            if (Version.TryParse(gameVersion, out var version))
            {
                return version.Major < 1 || (version.Major == 1 && version.Minor < 7);
            }
            // If version parsing fails, assume it might be relevant (safer to check)
            return true;
        }
    }
}