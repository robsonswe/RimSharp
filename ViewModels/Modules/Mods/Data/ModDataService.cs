using RimSharp.Models;
using RimSharp.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;

namespace RimSharp.ViewModels.Modules.Mods.Data
{
    public class ModDataService : IModDataService
    {
        private readonly IModService _modService;
        private readonly IPathService _pathService;
        private const string CONFIG_FILENAME = "ModsConfig.xml";

        public ModDataService(IModService modService, IPathService pathService)
        {
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        }

        public async Task<List<ModItem>> LoadAllModsAsync()
        {
            await _modService.LoadModsAsync();
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
                
                // We don't filter mod IDs here - let the ModListManager handle that
                // so it can track which mods are missing for user feedback
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
                    MessageBox.Show("Error: Config directory path is not set or invalid.",
                        "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var configPath = Path.Combine(configDir, CONFIG_FILENAME);
                var doc = PrepareModsConfigDocument(configPath, out bool fileExisted);
                if (doc is null) return;

                // Update or create the activeMods element
                UpdateActiveModsInDocument(doc, activeModIds);

                // Save the document
                doc.Save(configPath);
                MessageBox.Show($"Mods configuration saved successfully{(fileExisted ? "!" : " to new file")}!",
                    "Save Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show($"Error: Permission denied saving to {Path.Combine(_pathService.GetConfigPath(), CONFIG_FILENAME)}.",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An unexpected error occurred saving mods configuration: {ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show($"Warning: Could not read active mods from ModsConfig.xml.\nReason: {message}\nStarting with an empty active list.",
                           "Config Read Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                var result = MessageBox.Show(
                    $"Error loading existing ModsConfig.xml: {loadEx.Message}\n\nOverwrite with the current active mod list?",
                    "Load Error", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
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