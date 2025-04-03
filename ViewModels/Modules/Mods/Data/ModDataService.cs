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

        public ModDataService(IModService modService, IPathService pathService)
        {
            _modService = modService;
            _pathService = pathService;
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
                var configPathDir = _pathService.GetConfigPath();
                if (string.IsNullOrEmpty(configPathDir))
                {
                    Debug.WriteLine("Config path is empty or null. Cannot read ModsConfig.xml.");
                    return new List<string>();
                }

                var configPath = Path.Combine(configPathDir, "ModsConfig.xml");
                if (!File.Exists(configPath))
                {
                    Debug.WriteLine($"ModsConfig.xml does not exist at path: {configPath}");
                    return new List<string>();
                }

                var doc = XDocument.Load(configPath);
                return doc.Root?.Element("activeMods")?.Elements("li")
                    .Select(x => x.Value.ToLowerInvariant())
                    .ToList() ?? new List<string>();
            }
            catch (ArgumentNullException ex)
            {
                Debug.WriteLine($"Null argument error reading ModsConfig.xml: {ex.Message}");
                MessageBox.Show($"Warning: Could not read active mods from ModsConfig.xml.\nReason: Path is not set properly.\nStarting with an empty active list.",
                               "Config Read Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return new List<string>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading ModsConfig.xml: {ex.Message}");
                MessageBox.Show($"Warning: Could not read active mods from ModsConfig.xml.\nReason: {ex.Message}\nStarting with an empty active list.",
                               "Config Read Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return new List<string>();
            }
        }

        public void SaveActiveModIdsToConfig(IEnumerable<string> activeModIds)
        {
            try
            {
                var configDir = _pathService.GetConfigPath();
                if (string.IsNullOrEmpty(configDir) || !Directory.Exists(configDir))
                {
                    MessageBox.Show("Error: Config directory path is not set or invalid.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var configPath = Path.Combine(configDir, "ModsConfig.xml");
                XDocument doc;
                bool fileExisted = File.Exists(configPath);

                if (!fileExisted)
                {
                    doc = new XDocument(new XElement("ModsConfigData", new XElement("version", "1.0"), new XElement("activeMods")));
                }
                else
                {
                    try { doc = XDocument.Load(configPath); }
                    catch (Exception loadEx)
                    {
                        var result = MessageBox.Show($"Error loading existing ModsConfig.xml: {loadEx.Message}\n\nOverwrite with the current active mod list?", "Load Error", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (result == MessageBoxResult.Yes)
                        {
                            doc = new XDocument(new XElement("ModsConfigData", new XElement("version", "1.0"), new XElement("activeMods")));
                            fileExisted = false;
                        }
                        else { return; }
                    }
                }

                var activeModsElement = doc.Root?.Element("activeMods");
                if (activeModsElement == null)
                {
                    if (doc.Root != null)
                    {
                        activeModsElement = new XElement("activeMods");
                        doc.Root.Add(activeModsElement);
                    }
                    else
                    {
                        MessageBox.Show("Error: ModsConfig.xml structure invalid.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                activeModsElement.RemoveAll();
                foreach (var modId in activeModIds)
                {
                    if (!string.IsNullOrEmpty(modId))
                    {
                        activeModsElement.Add(new XElement("li", modId.ToLowerInvariant()));
                    }
                }

                doc.Save(configPath);
                MessageBox.Show($"Mods configuration saved successfully{(fileExisted ? "!" : " to new file")}!", "Save Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show($"Error: Permission denied saving to {Path.Combine(_pathService.GetConfigPath(), "ModsConfig.xml")}.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An unexpected error occurred saving mods configuration: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
