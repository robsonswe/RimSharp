using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using RimSharp.Core.Services;

namespace RimSharp.Infrastructure.Workshop.Core
{
    /// <summary>

    /// </summary>
    public class SteamCmdPlatformInfo
    {
        public OSPlatform Platform { get; }
        public string SteamCmdUrl { get; }
        public string SteamCmdExeName { get; }
        public bool IsArchiveZip { get; }
        public bool IsSupported { get; }

        public SteamCmdPlatformInfo()
        {

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Platform = OSPlatform.Windows;
                SteamCmdUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
                SteamCmdExeName = "steamcmd.exe";
                IsArchiveZip = true;
                IsSupported = true;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Platform = OSPlatform.Linux;
                SteamCmdUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";
                SteamCmdExeName = "steamcmd.sh";
                IsArchiveZip = false; // Requires tar.gz handling
                IsSupported = true;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Platform = OSPlatform.OSX;
                SteamCmdUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz";
                SteamCmdExeName = "steamcmd.sh";
                IsArchiveZip = false; // Requires tar.gz handling
                IsSupported = true;
            }
            else
            {
                Platform = OSPlatform.FreeBSD; // Or some other default/unknown
                SteamCmdUrl = string.Empty;
                SteamCmdExeName = string.Empty;
                IsArchiveZip = false;
                IsSupported = false;
            }
        }

        public bool IsPosix => Platform == OSPlatform.Linux || Platform == OSPlatform.OSX;

        // The SteamCMD Linux build is a 32-bit i386 binary; it cannot start
        // unless the 32-bit runtime loader is present on a 64-bit system.
        private static readonly string[] LinuxLoaderPaths =
        {
            "/lib/ld-linux.so.2",
            "/lib32/ld-linux.so.2",
            "/usr/lib/ld-linux.so.2",
            "/usr/lib32/ld-linux.so.2",
        };

        /// <summary>
        /// Returns a human-readable problem description when the current system
        /// cannot execute SteamCMD, or null when the runtime prerequisites are met.
        /// <paramref name="fileExists"/> allows tests to probe loader paths without
        /// touching the real filesystem.
        /// </summary>
        public virtual string? GetRuntimePrerequisiteProblem(Func<string, bool>? fileExists = null)
        {
            var exists = fileExists ?? (path => File.Exists(path));

            if (Platform == OSPlatform.Linux && !LinuxLoaderPaths.Any(exists))
            {
                return "SteamCMD is a 32-bit application and this 64-bit Linux system is missing the 32-bit runtime loader (ld-linux.so.2). " +
                       $"Install the 32-bit libraries with your package manager ({GetLinuxRuntimeInstallCommand()}) and run the setup again.";
            }

            return null;
        }

        public string? SetupRequirements => Platform switch
        {
            var p when p == OSPlatform.Linux => 
                "Note: On 64-bit Linux, SteamCMD requires 32-bit libraries. " +
                $"Install them via your package manager ({GetLinuxRuntimeInstallCommand()}).",
            var p when p == OSPlatform.OSX => 
                "Note: macOS may block 'Breakpad.framework' on first run. " +
                "If SteamCMD fails to start, go to System Settings > Security & Privacy and allow the framework.",
            _ => null
        };

        private static string GetLinuxRuntimeInstallCommand()
        {
            return LinuxDistributionDetector.Detect() switch
            {
                LinuxDistributionFamily.Debian => "Debian/Ubuntu: sudo apt install lib32gcc-s1",
                LinuxDistributionFamily.Fedora => "Fedora: sudo dnf install glibc.i686 libstdc++.i686",
                LinuxDistributionFamily.Arch => "Arch: enable the [multilib] repository, then sudo pacman -S lib32-glibc lib32-gcc-libs",
                _ => "install the 32-bit glibc and libstdc++ runtime packages for your distribution"
            };
        }
    }
}

