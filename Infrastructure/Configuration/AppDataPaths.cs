#nullable enable
using System;
using System.IO;

namespace RimSharp.Infrastructure.Configuration
{
    /// <summary>
    /// Central, platform-aware resolution of app-managed data locations.
    ///
    /// Windows: everything stays next to the executable
    /// (<see cref="AppDomain.CurrentDomain.BaseDirectory"/>), exactly as in
    /// previous versions - zero behavior change.
    ///
    /// Linux: app-managed data follows the XDG base directory specification:
    ///   - config.cfg -> $XDG_CONFIG_HOME/RimSharp (fallback ~/.config/RimSharp)
    ///   - data (SteamCMD_Data, Logs, Rules, Lists) -> $XDG_DATA_HOME/RimSharp
    ///     (fallback ~/.local/share/RimSharp)
    ///
    /// Explicit XDG environment variables are honored when set to an absolute
    /// path (per the XDG spec, relative values are ignored).
    /// </summary>
    public static class AppDataPaths
    {
        private const string AppFolderName = "RimSharp";
        private const string XdgConfigHomeEnv = "XDG_CONFIG_HOME";
        private const string XdgDataHomeEnv = "XDG_DATA_HOME";

        /// <summary>
        /// The executable's directory. Legacy (pre-XDG) location of all
        /// app-managed data; still the sole location on Windows.
        /// </summary>
        public static string BaseDirectory { get; } =
            AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// Directory holding config.cfg. Windows: next to the exe.
        /// Linux: $XDG_CONFIG_HOME/RimSharp or ~/.config/RimSharp.
        /// </summary>
        public static string ConfigDirectory =>
            OperatingSystem.IsWindows()
                ? BaseDirectory
                : ResolveXdgDirectory(XdgConfigHomeEnv, Environment.SpecialFolder.ApplicationData);

        /// <summary>
        /// Directory holding app-managed data (SteamCMD_Data, Logs, Rules,
        /// Lists). Windows: next to the exe. Linux: $XDG_DATA_HOME/RimSharp
        /// or ~/.local/share/RimSharp.
        /// </summary>
        public static string DataDirectory =>
            OperatingSystem.IsWindows()
                ? BaseDirectory
                : ResolveXdgDirectory(XdgDataHomeEnv, Environment.SpecialFolder.LocalApplicationData);

        /// <summary>Full path of the app's config.cfg file.</summary>
        public static string GetConfigFilePath() => Path.Combine(ConfigDirectory, "config.cfg");

        /// <summary>Creates <see cref="ConfigDirectory"/> on demand and returns it.</summary>
        public static string EnsureConfigDirectory() => EnsureDirectory(ConfigDirectory);

        /// <summary>Creates <see cref="DataDirectory"/> on demand and returns it.</summary>
        public static string EnsureDataDirectory() => EnsureDirectory(DataDirectory);

        /// <summary>Creates the given directory on demand and returns it.</summary>
        public static string EnsureDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            return directory;
        }

        /// <summary>
        /// One-time migration of legacy exe-side data into the XDG locations
        /// (Linux only; a no-op on Windows). For each known item, if the XDG
        /// target does not exist yet, the legacy counterpart exists next to
        /// the executable, and the executable directory is writable, the item
        /// is renamed (moved) into place. Nothing is ever deleted; failures
        /// are reported and skipped so startup can continue.
        /// </summary>
        public static void MigrateLegacyLocations()
        {
            if (OperatingSystem.IsWindows()) return;

            try
            {
                var items = new (string Name, string TargetRoot)[]
                {
                    ("config.cfg", ConfigDirectory),
                    ("SteamCMD_Data", DataDirectory),
                    ("Logs", DataDirectory),
                    ("Rules", DataDirectory),
                    ("Lists", DataDirectory),
                };

                foreach (var (name, targetDirectory) in items)
                {
                    try
                    {
                        MigrateLegacyItem(Path.Combine(BaseDirectory, name), Path.Combine(targetDirectory, name));
                    }
                    catch (Exception itemEx)
                    {
                        Console.WriteLine($"[RimSharp] Could not migrate legacy '{name}': {itemEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RimSharp] Legacy data migration failed (continuing): {ex.Message}");
            }
        }

        private static void MigrateLegacyItem(string legacyPath, string targetPath)
        {
            // Nothing to migrate, or the target already exists (never overwrite).
            if (!File.Exists(legacyPath) && !Directory.Exists(legacyPath)) return;
            if (File.Exists(targetPath) || Directory.Exists(targetPath)) return;

            // Guard against a degenerate resolution where legacy and target coincide.
            if (string.Equals(
                    Path.GetFullPath(legacyPath),
                    Path.GetFullPath(targetPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Only move when the exe directory is writable; otherwise leave
            // the legacy data in place rather than destroying it.
            if (!IsDirectoryWritable(BaseDirectory)) return;

            EnsureDirectory(Path.GetDirectoryName(targetPath)!);

            if (File.Exists(legacyPath))
            {
                File.Move(legacyPath, targetPath);
            }
            else
            {
                Directory.Move(legacyPath, targetPath);
            }

            Console.WriteLine($"[RimSharp] Migrated legacy data location: {legacyPath} -> {targetPath}");
        }

        private static bool IsDirectoryWritable(string directory)
        {
            try
            {
                var probe = Path.Combine(directory, $".rimsharp_write_test_{Guid.NewGuid():N}");
                File.WriteAllText(probe, "probe");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveXdgDirectory(string environmentVariable, Environment.SpecialFolder fallbackFolder)
        {
            string? root = Environment.GetEnvironmentVariable(environmentVariable);

            // XDG spec: overrides must be absolute paths to be valid.
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root))
            {
                // GetFolderPath is XDG-compliant on Linux:
                // ApplicationData -> $XDG_CONFIG_HOME or ~/.config
                // LocalApplicationData -> $XDG_DATA_HOME or ~/.local/share
                root = Environment.GetFolderPath(fallbackFolder, Environment.SpecialFolderOption.DoNotVerify);
            }

            if (string.IsNullOrWhiteSpace(root))
            {
                // Last resort: keep everything next to the executable.
                root = BaseDirectory;
            }

            return Path.Combine(root, AppFolderName);
        }
    }
}
