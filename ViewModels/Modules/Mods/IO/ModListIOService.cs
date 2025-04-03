using Microsoft.Win32;
using RimSharp.Models;
using RimSharp.Services;
using RimSharp.ViewModels.Modules.Mods.Management;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;

namespace RimSharp.ViewModels.Modules.Mods.IO
{
    public class ModListIOService : IModListIOService
    {
        private readonly IPathService _pathService;
        private readonly IModListManager _modListManager;

        public ModListIOService(IPathService pathService, IModListManager modListManager)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _modListManager = modListManager ?? throw new ArgumentNullException(nameof(modListManager));
        }

        public async Task ImportModListAsync()
        {
            try
            {
                // Ensure Lists directory exists
                var listsDirectory = await EnsureListsDirectoryAsync();

                // Show file dialog
                var filePath = await ShowFileDialogAsync(listsDirectory, FileDialogType.Open);
                if (string.IsNullOrEmpty(filePath))
                    return;

                Debug.WriteLine($"Importing mod list from: {filePath}");

                // Load and parse XML file
                var activeModIds = await ParseModListFileAsync(filePath);
                if (activeModIds is null || !activeModIds.Any())
                    return;

                // Update mods
                var allMods = _modListManager.GetAllMods().ToList();
                
                // Check which mods are missing
                var availableModIds = allMods
                    .Where(m => !string.IsNullOrEmpty(m.PackageId))
                    .Select(m => m.PackageId.ToLowerInvariant())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                
                var missingModIds = activeModIds
                    .Where(id => !availableModIds.Contains(id))
                    .ToList();
                
                // Only pass available mod IDs to the manager
                var availableActiveModIds = activeModIds
                    .Where(id => availableModIds.Contains(id))
                    .ToList();
                
                await Task.Run(() => _modListManager.Initialize(allMods, availableActiveModIds));

                // Display appropriate message with missing mods if any
                DisplayImportResults(Path.GetFileName(filePath), availableActiveModIds.Count, missingModIds);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An unexpected error occurred during import: {ex.Message}",
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task ExportModListAsync(IEnumerable<Models.ModItem> activeMods)
        {
            if (activeMods is null)
                throw new ArgumentNullException(nameof(activeMods));

            try
            {
                // Ensure Lists directory exists
                var listsDirectory = await EnsureListsDirectoryAsync();

                // Show file dialog
                var filePath = await ShowFileDialogAsync(listsDirectory, FileDialogType.Save);
                if (string.IsNullOrEmpty(filePath))
                    return;

                Debug.WriteLine($"Exporting mod list to: {filePath}");

                // Filter out mods without package IDs before saving
                var validActiveMods = activeMods.Where(m => !string.IsNullOrEmpty(m.PackageId)).ToList();
                
                // Create XML document with active mods
                await SaveModListFileAsync(filePath, validActiveMods);

                MessageBox.Show($"Mod list exported successfully to {Path.GetFileName(filePath)}!",
                    "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Error: Permission denied when saving the file.",
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An unexpected error occurred during export: {ex.Message}",
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region Helper Methods

        private async Task<string> EnsureListsDirectoryAsync()
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string listsDirectory = Path.Combine(appDirectory, "Lists");

            if (!Directory.Exists(listsDirectory))
            {
                await Task.Run(() => Directory.CreateDirectory(listsDirectory));
                Debug.WriteLine($"Created Lists directory at: {listsDirectory}");
            }

            return listsDirectory;
        }

        private enum FileDialogType { Open, Save }

        private Task<string> ShowFileDialogAsync(string initialDirectory, FileDialogType dialogType)
        {
            return Task.Run(() =>
            {
                string result = null;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (dialogType == FileDialogType.Open)
                    {
                        var openFileDialog = new OpenFileDialog
                        {
                            Title = "Import Mod List",
                            Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                            InitialDirectory = initialDirectory,
                            CheckFileExists = true
                        };

                        if (openFileDialog.ShowDialog() == true)
                        {
                            result = openFileDialog.FileName;
                        }
                    }
                    else // Save dialog
                    {
                        var saveFileDialog = new SaveFileDialog
                        {
                            Title = "Export Mod List",
                            Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                            InitialDirectory = initialDirectory,
                            DefaultExt = ".xml",
                            FileName = $"ModList_{DateTime.Now:yyyyMMdd}.xml"
                        };

                        if (saveFileDialog.ShowDialog() == true)
                        {
                            result = saveFileDialog.FileName;
                        }
                    }
                });

                return result;
            });
        }

        private async Task<List<string>> ParseModListFileAsync(string filePath)
        {
            XDocument doc;
            try
            {
                doc = await Task.Run(() => XDocument.Load(filePath));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading XML file: {ex.Message}",
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }

            var activeModsElement = doc.Root?.Element("activeMods");
            if (activeModsElement is null)
            {
                MessageBox.Show("The selected file does not contain a valid mod list format.",
                    "Invalid File Format", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }

            var activeModIds = activeModsElement.Elements("li")
                .Select(e => e.Value.ToLowerInvariant())
                .ToList();

            if (!activeModIds.Any())
            {
                MessageBox.Show("The file contains an empty mod list.",
                    "Import Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            return activeModIds;
        }

        private async Task SaveModListFileAsync(string filePath, IEnumerable<Models.ModItem> activeMods)
        {
            var doc = new XDocument(
                new XElement("ModsConfigData",
                    new XElement("version", "1.0"),
                    new XElement("activeMods")
                )
            );

            var activeModsElement = doc.Root.Element("activeMods");

            foreach (var mod in activeMods)
            {
                if (!string.IsNullOrEmpty(mod.PackageId))
                {
                    activeModsElement.Add(new XElement("li", mod.PackageId.ToLowerInvariant()));
                }
                else
                {
                    Debug.WriteLine($"Warning: Mod '{mod.Name}' has no PackageId and was not exported.");
                }
            }

            await Task.Run(() => doc.Save(filePath));
        }
        
        private void DisplayImportResults(string fileName, int importedCount, List<string> missingModIds)
        {
            if (missingModIds.Count == 0)
            {
                MessageBox.Show($"Successfully imported mod list from {fileName} with {importedCount} active mods.",
                    "Import Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var messageBuilder = new System.Text.StringBuilder();
            messageBuilder.AppendLine($"RimSharp imported {fileName} list.");
            messageBuilder.AppendLine();
            messageBuilder.AppendLine("The following mods are not installed and were not imported:");
            
            foreach (var modId in missingModIds)
            {
                messageBuilder.AppendLine($"- {modId}");
            }
            
            messageBuilder.AppendLine();
            messageBuilder.AppendLine($"Successfully activated {importedCount} installed mods.");

            MessageBox.Show(messageBuilder.ToString(),
                "Import Partial Success", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        #endregion
    }
}