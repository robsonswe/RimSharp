
#nullable enable
using System.Runtime.InteropServices;

namespace RimSharp.Core.Services
{
    public static class SystemInfoServiceFactory
    {
        public static ISystemInfoService Create()
        {
#if WINDOWS
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new WindowsSystemInfoService();
            }
#endif

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return new LinuxSystemInfoService();
            }

            return new NullSystemInfoService();
        }
    }

    public class NullSystemInfoService : ISystemInfoService
    {
        public long GetPrimaryGpuVram() => 0;
    }
}


