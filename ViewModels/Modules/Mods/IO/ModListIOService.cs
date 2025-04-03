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
            _pathService = pathService;
            _modListManager = modListManager;
        }

        public async Task ImportModListAsync()
        {
            try
            {
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string listsDirectory = Path.Combine(appDirectory, "Lists");

                if (!Directory.Exists(listsDirectory))
                {
                    Directory.CreateDirectory(listsDirectory);
                    Debug.WriteLine($"Created Lists directory at: {listsDirectory}");
                }

                var openFileDialog = new OpenFileDialog
                {
                    Title = "Import Mod List",
                    Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                    InitialDirectory = listsDirectory,
                    CheckFileExists = true
                };

                if (openFileDialog.ShowDialog() != true)
                {
                    Debug.WriteLine("Import cancelled by user");
                    return;
                }

                string filePath = openFileDialog.FileName;
                Debug.WriteLine($"Importing mod list from: {filePath}");

                XDocument doc;
                try
                {
                    doc = XDocument.Load(filePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading XML file: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var activeModsElement = doc.Root?.Element("activeMods");
                if (activeModsElement == null)
                {
                    MessageBox.Show("The selected file does not contain a valid mod list format.",
                        "Invalid File Format", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var activeModIds = activeModsElement.Elements("li")
                    .Select(e => e.Value.ToLowerInvariant())
                    .ToList();

                if (activeModIds.Count == 0)
                {
                    MessageBox.Show("The file contains an empty mod list.",
                        "Import Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var allMods = _modListManager.GetAllMods().ToList();
                _modListManager.Initialize(allMods, activeModIds);

                MessageBox.Show($"Successfully imported mod list from {Path.GetFileName(filePath)} with {activeModIds.Count} active mods.",
                    "Import Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An unexpected error occurred during import: {ex.Message}",
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task ExportModListAsync(IEnumerable<ModItem> activeMods)
        {
            try
            {
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string listsDirectory = Path.Combine(appDirectory, "Lists");

                if (!Directory.Exists(listsDirectory))
                {
                    Directory.CreateDirectory(listsDirectory);
                    Debug.WriteLine($"Created Lists directory at: {listsDirectory}");
                }

                var saveFileDialog = new SaveFileDialog
                {
                    Title = "Export Mod List",
                    Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                    InitialDirectory = listsDirectory,
                    DefaultExt = ".xml",
                    FileName = $"ModList_{DateTime.Now:yyyyMMdd}.xml"
                };

                if (saveFileDialog.ShowDialog() != true)
                {
                    Debug.WriteLine("Export cancelled by user");
                    return;
                }

                string filePath = saveFileDialog.FileName;
                Debug.WriteLine($"Exporting mod list to: {filePath}");

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

                doc.Save(filePath);

                MessageBox.Show($"Mod list exported successfully to {Path.GetFileName(filePath)}!",
                    "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show($"Error: Permission denied when saving the file.",
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An unexpected error occurred during export: {ex.Message}",
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
