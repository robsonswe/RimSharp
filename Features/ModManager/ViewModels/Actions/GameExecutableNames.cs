using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    /// <summary>
    /// Supplies the platform-specific executable file names used to launch the game
    /// and the process names used to detect that it is running.
    /// </summary>
    public static class GameExecutableNames
    {
        private static readonly string[] WindowsLaunchCandidates = { "RimWorldWin64.exe", "RimWorld.exe" };
        private static readonly string[] LinuxLaunchCandidates = { "RimWorld", "RimWorldLinux", "start_RimWorld.sh" };

        private static readonly string[] WindowsProcessNames = { "RimWorldWin64", "RimWorld" };
        private static readonly string[] LinuxProcessNames = { "RimWorldWin64", "RimWorld", "RimWorldLinux" };

        public static IReadOnlyList<string> GetLaunchCandidates()
            => GetLaunchCandidates(GetCurrentPlatform());

        public static IReadOnlyList<string> GetLaunchCandidates(OSPlatform platform)
            => platform == OSPlatform.Linux ? LinuxLaunchCandidates : WindowsLaunchCandidates;

        public static IReadOnlyList<string> GetProcessNames()
            => GetProcessNames(GetCurrentPlatform());

        public static IReadOnlyList<string> GetProcessNames(OSPlatform platform)
            => platform == OSPlatform.Linux ? LinuxProcessNames : WindowsProcessNames;

        private static OSPlatform GetCurrentPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return OSPlatform.Linux;
            }

            return OSPlatform.Windows;
        }
    }
}
