#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;

namespace RimSharp.Core.Services
{
    /// <summary>
    /// Multi-OS helpers for "hand the path/URL to the desktop" operations
    /// (Windows/macOS/Linux). Do NOT use Process.Start with UseShellExecute
    /// blindly — on Linux it is NOT a desktop-shell opener; use the dedicated
    /// branch helpers here instead of re-inventing per call site.
    /// </summary>
    public static class SystemLauncher
    {
        /// <summary>True for http/https absolute URLs.</summary>
        public static bool IsHttpUrl(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        /// <summary>
        /// Opens a URL in the default browser.
        /// Windows: shell handler. Linux: xdg-open. macOS: open.
        /// </summary>
        public static void OpenUrl(string url)
        {
            if (!IsHttpUrl(url)) return;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else
                {
                    // Linux/macOS: UseShellExecute doesn't resolve default
                    // desktop apps from config files alone.
                    var opener = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open";
                    Process.Start(new ProcessStartInfo(opener, $"\"{url}\"")
                    {
                        UseShellExecute = false
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SystemLauncher.OpenUrl failed for '{url}': {ex.Message}");
            }
        }

        /// <summary>
        /// Opens a file-system path with the default application (folder →
        /// file manager, file → associated app).
        /// Windows: shell handler. Linux: xdg-open. macOS: open.
        /// </summary>
        public static void OpenPath(string path)
        {
            if (!PathExists(path)) return;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                else
                {
                    var opener = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open";
                    Process.Start(new ProcessStartInfo(opener, $"\"{path}\"")
                    {
                        UseShellExecute = false
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SystemLauncher.OpenPath failed for '{path}': {ex.Message}");
            }
        }

        /// <summary>Dispatches a value that may be a URL or a file-system path.</summary>
        public static void OpenPathOrUrl(string? value)
        {
            if (IsHttpUrl(value)) OpenUrl(value!);
            else OpenPath(value ?? string.Empty);
        }

        /// <summary>
        /// Opens a folder in the OS file manager (does NOT launch assoc apps).
        /// Windows: Explorer. Linux: xdg-open. macOS: open.
        /// </summary>
        public static void OpenInFileManager(string path)
        {
            if (!Directory.Exists(path)) return;
            OpenPath(path);
        }

        /// <summary>
        /// Reveals a path inside the file manager without launching it.
        /// Windows: Explorer /select. macOS: open -R. Linux: opens the
        /// containing folder (most file managers have no reveal API).
        /// </summary>
        public static void RevealInFileManager(string path)
        {
            if (!PathExists(path)) return;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
                    else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    if (File.Exists(path)) Process.Start(new ProcessStartInfo("open", $"-R \"{path}\"") { UseShellExecute = false });
                    else if (Directory.Exists(path)) OpenPath(path);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var folder = File.Exists(path) ? (Path.GetDirectoryName(path) ?? path) : path;
                    Process.Start(new ProcessStartInfo("xdg-open", $"\"{folder}\"") { UseShellExecute = false });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SystemLauncher.RevealInFileManager failed for '{path}': {ex.Message}");
            }
        }

        /// <summary>
        /// Launches an executable binary (game/exe), not a desktop handler.
        /// Windows: shell handler is fine; Linux/macOS spawn the process
        /// directly (the launcher may not know a raw binary).
        /// </summary>
        public static void LaunchExecutable(string exePath, string? workingDirectory = null, Action<Exception>? onError = null)
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                onError?.Invoke(new FileNotFoundException("Executable not found.", exePath));
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                        ? Path.GetDirectoryName(Path.GetFullPath(exePath))
                        : workingDirectory
                };

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    psi.UseShellExecute = true;
                else
                    psi.UseShellExecute = false; // spawn the binary directly

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }

        private static bool PathExists(string? path) =>
            !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path));
    }
}
