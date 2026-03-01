#nullable enable
using System.Threading.Tasks;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Cross-platform file dialog service abstraction.
    /// Implementations should handle platform-specific dialog implementations.
    /// </summary>
    public interface IFileDialogService
    {
        /// <summary>
        /// Shows an open file dialog.
        /// </summary>
        /// <param name="title">Dialog title</param>
        /// <param name="filter">File filter (e.g., "XML Files|*.xml|All Files|*.*")</param>
        /// <param name="initialDirectory">Initial directory path</param>
        /// <returns>Tuple of (success, file path)</returns>
        Task<(bool Success, string? FilePath)> ShowOpenFileDialogAsync(string title, string filter, string? initialDirectory = null);

        /// <summary>
        /// Shows a save file dialog.
        /// </summary>
        /// <param name="title">Dialog title</param>
        /// <param name="filter">File filter</param>
        /// <param name="initialDirectory">Initial directory path</param>
        /// <param name="defaultExtension">Default file extension</param>
        /// <param name="defaultFileName">Default file name</param>
        /// <returns>Tuple of (success, file path)</returns>
        Task<(bool Success, string? FilePath)> ShowSaveFileDialogAsync(string title, string filter, string? initialDirectory = null, string? defaultExtension = null, string? defaultFileName = null);

        /// <summary>
        /// Shows an open folder dialog.
        /// </summary>
        /// <param name="title">Dialog title</param>
        /// <param name="initialDirectory">Initial directory path</param>
        /// <returns>Tuple of (success, folder path)</returns>
        Task<(bool Success, string? Path)> ShowOpenFolderDialogAsync(string title, string? initialDirectory = null);
    }
}
