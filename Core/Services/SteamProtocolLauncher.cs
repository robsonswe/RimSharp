#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Core.Services
{
    /// <summary>
    /// Opens <c>steam://</c> protocol URLs. On Windows/macOS the shell handler is
    /// enough; on Linux there is frequently no registered URL handler, so the
    /// <c>steam</c> binary is launched directly — through a terminal emulator when
    /// one is available (so the client's output is not lost to a detached
    /// process), and detached as the last resort (RimSort reference behavior:
    /// gnome-terminal, konsole, xfce4-terminal, mate-terminal, xterm,
    /// x-terminal-emulator, then direct launch).
    /// </summary>
    public static class SteamProtocolLauncher
    {
        private static readonly string[] LinuxTerminals =
        {
            "gnome-terminal", "konsole", "xfce4-terminal",
            "mate-terminal", "xterm", "x-terminal-emulator"
        };

        /// <summary>
        /// First terminal emulator available on PATH, or null when none is found.
        /// <paramref name="commandExists"/> lets tests probe candidate availability.
        /// </summary>
        public static string? SelectLinuxTerminal(Func<string, bool> commandExists)
        {
            return LinuxTerminals.FirstOrDefault(commandExists);
        }

        /// <summary>
        /// Attempts to open a <c>steam://</c> URL. Returns true when a launch was
        /// started; false when every strategy failed (caller logs/surfaces).
        /// </summary>
        public static bool TryOpen(string url, ILoggerService? logger = null, Func<string, bool>? commandExists = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(url);

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    return process != null;
                }

                // Linux: no shell URL handler in most setups.
                var probe = commandExists ?? FromPath;
                var terminal = SelectLinuxTerminal(probe);
                var psi = new ProcessStartInfo { UseShellExecute = false };

                if (terminal != null)
                {
                    // gnome-terminal needs "--" before the command; -e works for the
                    // xterm family (konsole/xfce4-terminal/mate-terminal accept -e too,
                    // though some warn about deprecation — acceptable for a launcher).
                    psi.FileName = terminal;
                    if (terminal == "gnome-terminal")
                    {
                        psi.ArgumentList.Add("--");
                    }
                    else
                    {
                        psi.ArgumentList.Add("-e");
                    }
                    psi.ArgumentList.Add("steam");
                    psi.ArgumentList.Add(url);
                }
                else
                {
                    psi.FileName = "steam";
                    psi.ArgumentList.Add(url);
                }

                var started = Process.Start(psi);
                logger?.LogInfo($"Steam protocol '{url}' launched via {(terminal ?? "direct steam process")}.", nameof(SteamProtocolLauncher));
                return started != null;
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"Failed to launch Steam protocol '{url}': {ex.Message}", nameof(SteamProtocolLauncher));
                return false;
            }
        }

        private static bool FromPath(string command)
        {
            var pathVariable = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathVariable)) return false;

            return pathVariable
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(dir => !string.IsNullOrWhiteSpace(dir) &&
                            File.Exists(Path.Combine(dir, command)));
        }
    }
}
