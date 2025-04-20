using System.Runtime.InteropServices;

namespace RimSharp.Infrastructure.Workshop.Core
{
    /// <summary>
    /// Holds platform-specific information related to SteamCMD installation and execution.
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
            // Determine OS and Set Platform Specifics
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
    }
}