#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Dialog
{
    /// <summary>
    /// Avalonia-based file dialog service implementation.
    /// Uses Avalonia's StorageProvider for cross-platform file dialogs.
    /// </summary>
    public class AvaloniaFileDialogService : IFileDialogService
    {
        private Window? GetMainWindow()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow;
            }
            return null;
        }

        public async Task<(bool Success, string? FilePath)> ShowOpenFileDialogAsync(string title, string filter, string? initialDirectory = null)
        {
            var window = GetMainWindow();
            if (window == null) return (false, null);

            var options = new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            };

            // Parse filter and add file type choices
            var fileTypes = ParseFilter(filter);
            if (fileTypes.Count > 0)
            {
                options.FileTypeFilter = fileTypes;
            }

            var files = await window.StorageProvider.OpenFilePickerAsync(options);
            
            if (files.Count > 0)
            {
                return (true, files[0].Path.LocalPath);
            }
            return (false, null);
        }

        public async Task<(bool Success, string? FilePath)> ShowSaveFileDialogAsync(string title, string filter, string? initialDirectory = null, string? defaultExtension = null, string? defaultFileName = null)
        {
            var window = GetMainWindow();
            if (window == null) return (false, null);

            var options = new FilePickerSaveOptions
            {
                Title = title,
                DefaultExtension = defaultExtension,
                SuggestedFileName = defaultFileName
            };

            // Parse filter and add file type choices
            var fileTypes = ParseFilter(filter);
            if (fileTypes.Count > 0)
            {
                options.FileTypeChoices = fileTypes;
            }

            var file = await window.StorageProvider.SaveFilePickerAsync(options);
            
            if (file != null)
            {
                return (true, file.Path.LocalPath);
            }
            return (false, null);
        }

        public async Task<(bool Success, string? Path)> ShowOpenFolderDialogAsync(string title, string? initialDirectory = null)
        {
            var window = GetMainWindow();
            if (window == null) return (false, null);

            var options = new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            };

            var folders = await window.StorageProvider.OpenFolderPickerAsync(options);
            
            if (folders.Count > 0)
            {
                return (true, folders[0].Path.LocalPath);
            }
            return (false, null);
        }

        /// <summary>
        /// Parses a filter string like "XML Files|*.xml|All Files|*.*" into FilePickerFileType list.
        /// </summary>
        private static System.Collections.Generic.List<FilePickerFileType> ParseFilter(string? filter)
        {
            var result = new System.Collections.Generic.List<FilePickerFileType>();
            
            if (string.IsNullOrEmpty(filter)) return result;

            // Format: "Description1|Pattern1|Description2|Pattern2|..."
            var parts = filter.Split('|');
            for (int i = 0; i < parts.Length - 1; i += 2)
            {
                var description = parts[i];
                var patterns = parts[i + 1].Split(';');
                
                var fileType = new FilePickerFileType(description)
                {
                    Patterns = patterns.Select(p => p.Trim()).ToList()
                };
                result.Add(fileType);
            }

            return result;
        }
    }
}
