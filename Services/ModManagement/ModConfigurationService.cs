using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Services.ModManagement
{
    /// <summary>
    /// Service for mod configuration management (export, import, clear, restore, sanitize, etc.).
    /// </summary>
    public class ModConfigurationService : IModConfigurationService
    {
        private readonly GameInstance _gameInstance;
        private readonly ILoggerService _logger;
        private readonly IPathService _pathService;
        private readonly IFileDialogService _fileDialogService;

        // Rolling backup configuration
        private const int MaxRollingBackups = 10;
        private const string BackupPrefix = "ModsConfig.xml.backup_";
        private const string SimpleBackupSuffix = ".xml.backup";

        // Test seams
        internal static Func<DateTime>? TestUtcNowOverride { get; set; }

        public ModConfigurationService(
            GameInstance gameInstance,
            ILoggerService logger,
            IPathService pathService,
            IFileDialogService fileDialogService)
        {
            _gameInstance = gameInstance ?? throw new ArgumentNullException(nameof(gameInstance));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        }

        /// <inheritdoc />
        public async Task<ModConfigurationResult> ExportModListAsync(string? filePath = null, CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("ExportModListAsync started", nameof(ModConfigurationService));

            try
            {
                var configPath = _pathService.GetConfigPath();
                if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
                {
                    return ModConfigurationResult.Fail("Config folder not found");
                }

                var modsConfigPath = Path.Combine(configPath, "ModsConfig.xml");
                if (!File.Exists(modsConfigPath))
                {
                    return ModConfigurationResult.Fail("ModsConfig.xml not found");
                }

                // If no file path provided, show save dialog
                if (string.IsNullOrEmpty(filePath))
                {
                    var listsDir = Path.Combine(AppDataPaths.DataDirectory, "Lists");
                    Directory.CreateDirectory(listsDir);
                    var defaultName = $"ModList_{DateTime.Now:yyyyMMdd}.xml";

                    var (result, selectedPath) = await _fileDialogService.ShowSaveFileDialogAsync(
                        "Export Mod List",
                        "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                        listsDir,
                        ".xml",
                        defaultName);

                    if (!result || string.IsNullOrEmpty(selectedPath))
                    {
                        return ModConfigurationResult.Cancelled();
                    }
                    filePath = selectedPath;
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Read the existing ModsConfig.xml and write to target
                var doc = XDocument.Load(modsConfigPath);

                // Ensure version is present (default 1.4 if missing)
                var root = doc.Root;
                if (root != null)
                {
                    var versionElement = root.Element("version");
                    if (versionElement == null)
                    {
                        root.AddFirst(new XElement("version", "1.4"));
                    }
                    else if (string.IsNullOrWhiteSpace(versionElement.Value))
                    {
                        versionElement.Value = "1.4";
                    }

                    // Ensure knownExpansions exists
                    if (root.Element("knownExpansions") == null)
                    {
                        var activeMods = root.Element("activeMods");
                        if (activeMods != null)
                        {
                            activeMods.AddAfterSelf(new XElement("knownExpansions"));
                        }
                        else
                        {
                            root.Add(new XElement("knownExpansions"));
                        }
                    }
                }

                await Task.Run(() => doc.Save(filePath!), cancellationToken);

                _logger.LogInfo($"ExportModListAsync completed: {filePath}", nameof(ModConfigurationService));
                return ModConfigurationResult.Ok($"Mod list exported to {Path.GetFileName(filePath!)}", 1, new Dictionary<string, object> { ["filePath"] = filePath! });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("ExportModListAsync cancelled", nameof(ModConfigurationService));
                return ModConfigurationResult.Cancelled();
            }
            catch (Exception ex)
            {
                _logger.LogError($"ExportModListAsync failed: {ex.Message}", nameof(ModConfigurationService));
                return ModConfigurationResult.Fail($"Export failed: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<ModConfigurationResult> ImportModListAsync(string? filePath = null, CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("ImportModListAsync started", nameof(ModConfigurationService));

            try
            {
                var configPath = _pathService.GetConfigPath();
                if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
                {
                    return ModConfigurationResult.Fail("Config folder not found");
                }

                var modsConfigPath = Path.Combine(configPath, "ModsConfig.xml");

                // If no file path provided, show open dialog
                if (string.IsNullOrEmpty(filePath))
                {
                    var listsDir = Path.Combine(AppDataPaths.DataDirectory, "Lists");
                    var (result, selectedPath) = await _fileDialogService.ShowOpenFileDialogAsync(
                        "Import Mod List",
                        "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                        listsDir);

                    if (!result || string.IsNullOrEmpty(selectedPath))
                    {
                        return ModConfigurationResult.Cancelled();
                    }
                    filePath = selectedPath;
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(filePath))
                {
                    return ModConfigurationResult.Fail("Selected file does not exist");
                }

                // Validate XML well-formedness
                XDocument importedDoc;
                try
                {
                    importedDoc = XDocument.Load(filePath);
                }
                catch (System.Xml.XmlException ex)
                {
                    return ModConfigurationResult.Fail($"Invalid XML: {ex.Message}");
                }

                var root = importedDoc.Root;
                if (root == null || root.Name != "ModsConfigData")
                {
                    return ModConfigurationResult.Fail("Not a valid ModsConfig.xml format (missing ModsConfigData root)");
                }

                // Create rolling backup before write
                await CreateRollingBackupAsync(modsConfigPath, cancellationToken);

                // Write the imported document
                await Task.Run(() => importedDoc.Save(modsConfigPath), cancellationToken);

                _logger.LogInfo($"ImportModListAsync completed: {filePath}", nameof(ModConfigurationService));
                return ModConfigurationResult.Ok($"Mod list imported from {Path.GetFileName(filePath)}. Rolling backup created.", 1, new Dictionary<string, object> { ["filePath"] = filePath });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("ImportModListAsync cancelled", nameof(ModConfigurationService));
                return ModConfigurationResult.Cancelled();
            }
            catch (Exception ex)
            {
                _logger.LogError($"ImportModListAsync failed: {ex.Message}", nameof(ModConfigurationService));
                return ModConfigurationResult.Fail($"Import failed: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<ModConfigurationResult> ClearAllModsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("ClearAllModsAsync started", nameof(ModConfigurationService));

            try
            {
                var configPath = _pathService.GetConfigPath();
                if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
                {
                    return ModConfigurationResult.Fail("Config folder not found");
                }

                var modsConfigPath = Path.Combine(configPath, "ModsConfig.xml");
                var modsPath = _gameInstance.LocalFolder ?? Path.Combine(_gameInstance.GameFolder ?? "", "Mods");

                // Create simple backup (.xml.backup) before any changes
                if (File.Exists(modsConfigPath))
                {
                    var simpleBackupPath = modsConfigPath + SimpleBackupSuffix;
                    File.Copy(modsConfigPath, simpleBackupPath, true);
                    _logger.LogInfo($"Created simple backup: {simpleBackupPath}", nameof(ModConfigurationService));
                }

                // Create rolling backup as well
                await CreateRollingBackupAsync(modsConfigPath, cancellationToken);

                // Clear Mods folder contents
                int deletedModsCount = 0;
                if (!string.IsNullOrEmpty(modsPath) && Directory.Exists(modsPath))
                {
                    var modsDir = new DirectoryInfo(modsPath);
                    foreach (var item in modsDir.GetFileSystemInfos())
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
                            deletedModsCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to delete {item.FullName}: {ex.Message}", nameof(ModConfigurationService));
                        }
                    }
                }

                // Reset ModsConfig.xml to vanilla
                var vanillaDoc = CreateVanillaModsConfig();
                await Task.Run(() => vanillaDoc.Save(modsConfigPath), cancellationToken);

                _logger.LogInfo("ClearAllModsAsync completed", nameof(ModConfigurationService));
                return ModConfigurationResult.Ok($"Cleared all mods. Deleted {deletedModsCount} items from Mods folder. ModsConfig.xml reset to vanilla. Backups created.", deletedModsCount, new Dictionary<string, object>
                {
                    ["deletedModsCount"] = deletedModsCount,
                    ["modsConfigPath"] = modsConfigPath,
                    ["simpleBackup"] = modsConfigPath + SimpleBackupSuffix
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("ClearAllModsAsync cancelled", nameof(ModConfigurationService));
                return ModConfigurationResult.Cancelled();
            }
            catch (Exception ex)
            {
                _logger.LogError($"ClearAllModsAsync failed: {ex.Message}", nameof(ModConfigurationService));
                return ModConfigurationResult.Fail($"Clear failed: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<ModConfigurationResult> RestoreModsConfigFromBackupAsync(string backupFileName, CancellationToken cancellationToken = default)
        {
            _logger.LogInfo($"RestoreModsConfigFromBackupAsync started: {backupFileName}", nameof(ModConfigurationService));

            try
            {
                var configPath = _pathService.GetConfigPath();
                if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
                {
                    return ModConfigurationResult.Fail("Config folder not found");
                }

                var modsConfigPath = Path.Combine(configPath, "ModsConfig.xml");
                var backupPath = Path.Combine(configPath, backupFileName);

                if (!File.Exists(backupPath))
                {
                    return ModConfigurationResult.Fail($"Backup file not found: {backupFileName}");
                }

                // Create a backup of current state before restore
                if (File.Exists(modsConfigPath))
                {
                    await CreateRollingBackupAsync(modsConfigPath, cancellationToken);
                }

                // Restore from backup
                await Task.Run(() => File.Copy(backupPath, modsConfigPath, true), cancellationToken);

                _logger.LogInfo($"RestoreModsConfigFromBackupAsync completed: {backupFileName}", nameof(ModConfigurationService));
                return ModConfigurationResult.Ok($"ModsConfig.xml restored from {backupFileName}", 1, new Dictionary<string, object> { ["backupFileName"] = backupFileName });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("RestoreModsConfigFromBackupAsync cancelled", nameof(ModConfigurationService));
                return ModConfigurationResult.Cancelled();
            }
            catch (Exception ex)
            {
                _logger.LogError($"RestoreModsConfigFromBackupAsync failed: {ex.Message}", nameof(ModConfigurationService));
                return ModConfigurationResult.Fail($"Restore failed: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ModConfigBackupInfo>> ListModConfigBackupsAsync(CancellationToken cancellationToken = default)
        {
            var configPath = _pathService.GetConfigPath();
            if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
            {
                return Array.Empty<ModConfigBackupInfo>();
            }

            var backups = new List<ModConfigBackupInfo>();

            try
            {
                var configDir = new DirectoryInfo(configPath);

                // Find rolling backups: ModsConfig.xml.backup_YYYYMMDD_HHMMSS
                var rollingBackups = configDir.GetFiles("ModsConfig.xml.backup_*")
                    .Where(f => Regex.IsMatch(f.Name, @"^ModsConfig\.xml\.backup_\d{8}_\d{6}$"))
                    .OrderByDescending(f => f.LastWriteTimeUtc);

                foreach (var file in rollingBackups)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    backups.Add(new ModConfigBackupInfo
                    {
                        FileName = file.Name,
                        FullPath = file.FullName,
                        CreatedUtc = file.LastWriteTimeUtc,
                        SizeBytes = file.Length,
                        IsRollingBackup = true
                    });
                }

                // Find simple backup: ModsConfig.xml.backup
                var simpleBackup = configDir.GetFiles("ModsConfig.xml" + SimpleBackupSuffix).FirstOrDefault();
                if (simpleBackup != null)
                {
                    backups.Add(new ModConfigBackupInfo
                    {
                        FileName = simpleBackup.Name,
                        FullPath = simpleBackup.FullName,
                        CreatedUtc = simpleBackup.LastWriteTimeUtc,
                        SizeBytes = simpleBackup.Length,
                        IsRollingBackup = false
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to list mod config backups: {ex.Message}", nameof(ModConfigurationService));
            }

            return backups;
        }

        /// <inheritdoc />
        public async Task<ModConfigurationResult> SanitizeCoreDlcOrderAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("SanitizeCoreDlcOrderAsync started", nameof(ModConfigurationService));

            try
            {
                var configPath = _pathService.GetConfigPath();
                if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
                {
                    return ModConfigurationResult.Fail("Config folder not found");
                }

                var modsConfigPath = Path.Combine(configPath, "ModsConfig.xml");
                if (!File.Exists(modsConfigPath))
                {
                    return ModConfigurationResult.Fail("ModsConfig.xml not found");
                }

                // Create rolling backup before modification
                await CreateRollingBackupAsync(modsConfigPath, cancellationToken);

                var doc = XDocument.Load(modsConfigPath);
                var root = doc.Root;
                if (root == null)
                {
                    return ModConfigurationResult.Fail("ModsConfig.xml has no root element");
                }

                // Define the required hierarchy order
                var hierarchy = new[]
                {
                    "ludeon.rimworld",           // Core
                    "ludeon.rimworld.royalty",   // Royalty
                    "ludeon.rimworld.ideology",  // Ideology
                    "ludeon.rimworld.biotech",   // Biotech
                    "ludeon.rimworld.anomaly"    // Anomaly
                };

                var activeModsElement = root.Element("activeMods");
                if (activeModsElement == null)
                {
                    return ModConfigurationResult.Fail("No activeMods element found");
                }

                var currentActiveMods = activeModsElement.Elements("li")
                    .Select(x => x.Value.ToLowerInvariant())
                    .ToList();

                // Build new ordered list: hierarchy items first (if present), then others
                var newActiveMods = new List<string>();
                var remainingMods = new HashSet<string>(currentActiveMods, StringComparer.OrdinalIgnoreCase);

                foreach (var h in hierarchy)
                {
                    if (remainingMods.Contains(h))
                    {
                        newActiveMods.Add(h);
                        remainingMods.Remove(h);
                    }
                }

                // Add remaining mods in their original relative order
                foreach (var mod in currentActiveMods)
                {
                    if (remainingMods.Contains(mod))
                    {
                        newActiveMods.Add(mod);
                    }
                }

                // Ensure knownExpansions contains all installed DLCs
                var knownExpansionsElement = root.Element("knownExpansions");
                if (knownExpansionsElement == null)
                {
                    knownExpansionsElement = new XElement("knownExpansions");
                    root.Add(knownExpansionsElement);
                }

                var knownExpansions = knownExpansionsElement.Elements("li")
                    .Select(x => x.Value.ToLowerInvariant())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Add missing DLCs from hierarchy that are in activeMods
                foreach (var h in hierarchy.Skip(1)) // Skip Core
                {
                    if (newActiveMods.Contains(h) && !knownExpansions.Contains(h))
                    {
                        knownExpansionsElement.Add(new XElement("li", h));
                    }
                }

                // Update activeMods
                activeModsElement.RemoveAll();
                foreach (var modId in newActiveMods)
                {
                    activeModsElement.Add(new XElement("li", modId));
                }

                // Ensure version is 1.4
                var versionElement = root.Element("version");
                if (versionElement == null)
                {
                    root.AddFirst(new XElement("version", "1.4"));
                }
                else if (string.IsNullOrWhiteSpace(versionElement.Value))
                {
                    versionElement.Value = "1.4";
                }

                await Task.Run(() => doc.Save(modsConfigPath), cancellationToken);

                _logger.LogInfo("SanitizeCoreDlcOrderAsync completed", nameof(ModConfigurationService));
                return ModConfigurationResult.Ok($"Core/DLC load order sanitized. Active mods: {newActiveMods.Count}", newActiveMods.Count, new Dictionary<string, object>
                {
                    ["activeMods"] = newActiveMods,
                    ["hierarchyEnforced"] = true
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("SanitizeCoreDlcOrderAsync cancelled", nameof(ModConfigurationService));
                return ModConfigurationResult.Cancelled();
            }
            catch (Exception ex)
            {
                _logger.LogError($"SanitizeCoreDlcOrderAsync failed: {ex.Message}", nameof(ModConfigurationService));
                return ModConfigurationResult.Fail($"Sanitize failed: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<ModConfigurationResult> DetectDuplicatePackageIdsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("DetectDuplicatePackageIdsAsync started", nameof(ModConfigurationService));

            var duplicates = new List<DuplicateModInfo>();

            try
            {
                // Build map of packageId -> list of locations for local mods
                var localMods = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(_gameInstance.LocalFolder) && Directory.Exists(_gameInstance.LocalFolder))
                {
                    var modDirs = new DirectoryInfo(_gameInstance.LocalFolder).GetDirectories();
                    foreach (var modDir in modDirs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var aboutPath = Path.Combine(modDir.FullName, "About", "About.xml");
                        if (File.Exists(aboutPath))
                        {
                            try
                            {
                                var doc = XDocument.Load(aboutPath);
                                var packageId = doc.Root?.Element("packageId")?.Value?.Trim();
                                if (!string.IsNullOrEmpty(packageId))
                                {
                                    if (!localMods.ContainsKey(packageId))
                                        localMods[packageId] = new List<string>();
                                    localMods[packageId].Add($"Mods/{modDir.Name}");
                                }
                            }
                            catch { }
                        }
                    }
                }

                // Build map for workshop mods
                var workshopMods = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                if (_gameInstance.IsSteamInstallation && !string.IsNullOrEmpty(_gameInstance.WorkshopFolder) && Directory.Exists(_gameInstance.WorkshopFolder))
                {
                    var modDirs = new DirectoryInfo(_gameInstance.WorkshopFolder).GetDirectories();
                    foreach (var modDir in modDirs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var aboutPath = Path.Combine(modDir.FullName, "About", "About.xml");
                        if (File.Exists(aboutPath))
                        {
                            try
                            {
                                var doc = XDocument.Load(aboutPath);
                                var packageId = doc.Root?.Element("packageId")?.Value?.Trim();
                                if (!string.IsNullOrEmpty(packageId))
                                {
                                    if (!workshopMods.ContainsKey(packageId))
                                        workshopMods[packageId] = new List<string>();
                                    workshopMods[packageId].Add($"workshop/content/294100/{modDir.Name}");
                                }
                            }
                            catch { }
                        }
                    }
                }

                // Find duplicates: same packageId in both local and workshop
                foreach (var kvp in localMods)
                {
                    if (workshopMods.ContainsKey(kvp.Key))
                    {
                        duplicates.Add(new DuplicateModInfo
                        {
                            PackageId = kvp.Key,
                            LocalPaths = kvp.Value,
                            WorkshopPaths = workshopMods[kvp.Key]
                        });
                    }
                }

                // Also find duplicates within local only
                foreach (var kvp in localMods)
                {
                    if (kvp.Value.Count > 1)
                    {
                        duplicates.Add(new DuplicateModInfo
                        {
                            PackageId = kvp.Key,
                            LocalPaths = kvp.Value,
                            WorkshopPaths = new List<string>()
                        });
                    }
                }

                // And within workshop only
                foreach (var kvp in workshopMods)
                {
                    if (kvp.Value.Count > 1)
                    {
                        duplicates.Add(new DuplicateModInfo
                        {
                            PackageId = kvp.Key,
                            LocalPaths = new List<string>(),
                            WorkshopPaths = kvp.Value
                        });
                    }
                }

                _logger.LogInfo($"DetectDuplicatePackageIdsAsync completed: {duplicates.Count} duplicates found", nameof(ModConfigurationService));
                return ModConfigurationResult.Ok(duplicates.Count > 0
                    ? $"Found {duplicates.Count} duplicate package ID(s)"
                    : "No duplicate package IDs detected",
                    duplicates.Count,
                    new Dictionary<string, object> { ["duplicates"] = duplicates });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("DetectDuplicatePackageIdsAsync cancelled", nameof(ModConfigurationService));
                return ModConfigurationResult.Cancelled();
            }
            catch (Exception ex)
            {
                _logger.LogError($"DetectDuplicatePackageIdsAsync failed: {ex.Message}", nameof(ModConfigurationService));
                return ModConfigurationResult.Fail($"Detect failed: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<ModConfigurationResult> AnalyzeSavesQuotaAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("AnalyzeSavesQuotaAsync started", nameof(ModConfigurationService));

            try
            {
                var configPath = _pathService.GetConfigPath();
                if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
                {
                    return ModConfigurationResult.Fail("Config folder not found");
                }

                var savesPath = Path.Combine(configPath, "Saves");
                if (!Directory.Exists(savesPath))
                {
                    return ModConfigurationResult.Ok("Saves folder not found", 0, new Dictionary<string, object> { ["sizeBytes"] = 0L, ["sizeMB"] = 0, ["fileCount"] = 0 });
                }

                const long WarningThreshold = 500 * 1024 * 1024; // 500 MB
                long totalSize = 0;
                int fileCount = 0;

                var files = new DirectoryInfo(savesPath).GetFiles("*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    totalSize += file.Length;
                    fileCount++;
                }

                var sizeMB = totalSize / (1024 * 1024);
                var overThreshold = totalSize > WarningThreshold;

                _logger.LogInfo($"AnalyzeSavesQuotaAsync completed: {sizeMB} MB, {fileCount} files", nameof(ModConfigurationService));
                return ModConfigurationResult.Ok(overThreshold
                    ? $"Saves folder: {sizeMB} MB ({fileCount} files) - EXCEEDS 500 MB Steam Cloud quota warning"
                    : $"Saves folder: {sizeMB} MB ({fileCount} files) - within quota",
                    fileCount,
                    new Dictionary<string, object>
                    {
                        ["sizeBytes"] = totalSize,
                        ["sizeMB"] = sizeMB,
                        ["fileCount"] = fileCount,
                        ["overThreshold"] = overThreshold,
                        ["thresholdMB"] = 500
                    });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("AnalyzeSavesQuotaAsync cancelled", nameof(ModConfigurationService));
                return ModConfigurationResult.Cancelled();
            }
            catch (Exception ex)
            {
                _logger.LogError($"AnalyzeSavesQuotaAsync failed: {ex.Message}", nameof(ModConfigurationService));
                return ModConfigurationResult.Fail($"Analyze failed: {ex.Message}");
            }
        }

        private async Task CreateRollingBackupAsync(string modsConfigPath, CancellationToken cancellationToken)
        {
            if (!File.Exists(modsConfigPath))
                return;

            try
            {
                var configDir = Path.GetDirectoryName(modsConfigPath)!;
                var timestamp = (TestUtcNowOverride?.Invoke() ?? DateTime.UtcNow).ToString("yyyyMMdd_HHmmss");
                var backupName = $"{BackupPrefix}{timestamp}";
                var backupPath = Path.Combine(configDir, backupName);

                await Task.Run(() => File.Copy(modsConfigPath, backupPath, true), cancellationToken);

                // Clean up old rolling backups (keep last 10)
                var rollingBackups = new DirectoryInfo(configDir).GetFiles("ModsConfig.xml.backup_*")
                    .Where(f => Regex.IsMatch(f.Name, @"^ModsConfig\.xml\.backup_\d{8}_\d{6}$"))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Skip(MaxRollingBackups)
                    .ToList();

                foreach (var oldBackup in rollingBackups)
                {
                    try
                    {
                        oldBackup.Delete();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to delete old rolling backup {oldBackup.Name}: {ex.Message}", nameof(ModConfigurationService));
                    }
                }

                _logger.LogDebug($"Created rolling backup: {backupName}", nameof(ModConfigurationService));
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to create rolling backup: {ex.Message}", nameof(ModConfigurationService));
            }
        }

        private XDocument CreateVanillaModsConfig()
        {
            return new XDocument(
                new XElement("ModsConfigData",
                    new XElement("version", "1.4"),
                    new XElement("activeMods",
                        new XElement("li", "ludeon.rimworld")
                    ),
                    new XElement("knownExpansions")
                )
            );
        }

        public sealed class DuplicateModInfo
        {
            public string PackageId { get; init; } = string.Empty;
            public List<string> LocalPaths { get; init; } = new();
            public List<string> WorkshopPaths { get; init; } = new();
        }
    }
}