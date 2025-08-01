using RimSharp.Core.Extensions;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
// Removed: using System.Windows; // No longer needed for MessageBox
using System.Xml.Linq;

namespace RimSharp.Features.ModManager.Services.Data
{
    public class ModDataService : IModDataService
    {
        private readonly IModService _modService;
        private readonly IPathService _pathService;
        private readonly IDialogService _dialogService; // Added
        private const string CONFIG_FILENAME = "ModsConfig.xml";

        // Updated Constructor
        public ModDataService(IModService modService, IPathService pathService, IDialogService dialogService)
        {
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService)); // Added
        }

        public async Task<List<ModItem>> LoadAllModsAsync()
        {
            await _modService.LoadModsAsync(); // This call now ensures rules are applied
            return _modService.GetLoadedMods().ToList();
        }
        public List<string> LoadActiveModIdsFromConfig()
        {
            try
            {
                var configPath = GetModsConfigPath();
                if (string.IsNullOrEmpty(configPath))
                {
                    DisplayConfigError("Config path is empty or null. Cannot read ModsConfig.xml.");
                    return new List<string>();
                }

                if (!File.Exists(configPath))
                {
                    DisplayConfigError($"ModsConfig.xml does not exist at path: {configPath}");
                    return new List<string>();
                }

                // Load and parse the XML
                var allModIds = ParseModsConfigXml(configPath);
                var allMods = _modService.GetLoadedMods();

                // Check which mods are missing
                var availableModIds = allMods
                    .Where(m => !string.IsNullOrEmpty(m.PackageId))
                    .Select(m => m.PackageId.ToLowerInvariant())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var missingModIds = allModIds
                    .Where(id => !availableModIds.Contains(id))
                    .ToList();

                // Show message about missing mods if any
                if (missingModIds.Count > 0)
                {
                    var messageBuilder = new System.Text.StringBuilder();
                    messageBuilder.AppendLine("The following mods in your ModsConfig.xml are not installed:");
                    messageBuilder.AppendLine();

                    foreach (var modId in missingModIds)
                    {
                        messageBuilder.AppendLine($"- {modId}");
                    }

                    messageBuilder.AppendLine();
                    messageBuilder.AppendLine($"Successfully loaded {allModIds.Count - missingModIds.Count} installed mods.");

                    ThreadHelper.EnsureUiThread(() =>
                    {
                        _dialogService.ShowMessageWithCopy(
                            "Missing Mods",
                            messageBuilder.ToString(),
                            MessageDialogType.Warning);
                    });
                }


                return allModIds;
            }
            catch (Exception ex) when (ex is ArgumentNullException || ex is DirectoryNotFoundException)
            {
                DisplayConfigError($"Path error reading ModsConfig.xml: {ex.Message}");
                return new List<string>();
            }
            catch (Exception ex)
            {
                DisplayConfigError($"Error reading ModsConfig.xml: {ex.Message}");
                return new List<string>();
            }
        }

        public void SaveActiveModIdsToConfig(IEnumerable<string> activeModIds)
        {
            if (activeModIds is null)
                throw new ArgumentNullException(nameof(activeModIds));

            try
            {
                var configDir = _pathService.GetConfigPath();
                if (string.IsNullOrEmpty(configDir) || !Directory.Exists(configDir))
                {
                    // --- Replaced MessageBox ---
                    _dialogService.ShowError("Save Error", "Error: Config directory path is not set or invalid.");
                    // -------------------------
                    return;
                }

                var configPath = Path.Combine(configDir, CONFIG_FILENAME);
                var doc = PrepareModsConfigDocument(configPath, out bool fileExisted);
                if (doc is null) return;

                // Update or create the activeMods element
                UpdateActiveModsInDocument(doc, activeModIds);

                // Save the document
                doc.Save(configPath);
                // --- Replaced MessageBox ---
                _dialogService.ShowInformation("Save Successful", $"Mods configuration saved successfully{(fileExisted ? "!" : " to new file")}!");
                // -------------------------
            }
            catch (UnauthorizedAccessException)
            {
                // --- Replaced MessageBox ---
                _dialogService.ShowError("Save Error", $"Error: Permission denied saving to {Path.Combine(_pathService.GetConfigPath(), CONFIG_FILENAME)}.");
                // -------------------------
            }
            catch (Exception ex)
            {
                // --- Replaced MessageBox ---
                _dialogService.ShowError("Save Error", $"An unexpected error occurred saving mods configuration: {ex.Message}");
                // -------------------------
            }
        }

        #region Helper Methods

        private string GetModsConfigPath()
        {
            var configPathDir = _pathService.GetConfigPath();
            return string.IsNullOrEmpty(configPathDir)
                ? null
                : Path.Combine(configPathDir, CONFIG_FILENAME);
        }

        private void DisplayConfigError(string message)
        {
            Debug.WriteLine(message);
            // --- Replaced MessageBox ---
            ThreadHelper.EnsureUiThread(() =>
            {
                _dialogService.ShowWarning("Config Read Warning", $"Warning: Could not read active mods from ModsConfig.xml.\nReason: {message}\nStarting with an empty active list.");
            });
            // -------------------------
        }

        private List<string> ParseModsConfigXml(string configPath)
        {
            var doc = XDocument.Load(configPath);

            return doc.Root?.Element("activeMods")?.Elements("li")
                .Select(x => x.Value.ToLowerInvariant())
                .ToList() ?? new List<string>();
        }

        private XDocument PrepareModsConfigDocument(string configPath, out bool fileExisted)
        {
            fileExisted = File.Exists(configPath);

            if (!fileExisted)
            {
                return CreateNewModsConfigDocument();
            }

            try
            {
                return XDocument.Load(configPath);
            }
            catch (Exception loadEx)
            {
                // --- Replaced MessageBox ---
                var dialogResult = _dialogService.ShowConfirmation(
                    "Load Error",
                    $"Error loading existing ModsConfig.xml: {loadEx.Message}\n\nOverwrite with the current active mod list?",
                    showCancel: true); // Show OK and Cancel
                // -------------------------

                // Assuming OK maps to Yes, Cancel maps to No
                if (dialogResult == MessageDialogResult.OK) // Use MessageDialogResult.OK
                {
                    fileExisted = false;
                    return CreateNewModsConfigDocument();
                }

                return null;
            }
        }

        private XDocument CreateNewModsConfigDocument()
        {
            return new XDocument(
                new XElement("ModsConfigData",
                    new XElement("version", "1.0"),
                    new XElement("activeMods"))
            );
        }

        private void UpdateActiveModsInDocument(XDocument doc, IEnumerable<string> activeModIds)
        {
            // Get or create activeMods element
            var activeModsElement = doc.Root?.Element("activeMods");
            if (activeModsElement is null)
            {
                if (doc.Root is null)
                {
                    throw new InvalidOperationException("ModsConfig.xml structure invalid - document has no root.");
                }

                activeModsElement = new XElement("activeMods");
                doc.Root.Add(activeModsElement);
            }

            // Clear existing entries
            activeModsElement.RemoveAll();

            // Add new entries
            foreach (var modId in activeModIds.Where(id => !string.IsNullOrEmpty(id)))
            {
                activeModsElement.Add(new XElement("li", modId.ToLowerInvariant()));
            }
        }

        #endregion
    }
}
