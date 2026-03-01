// Core/Services/SystemInfoServiceFactory.cs
#nullable enable
using System.Runtime.InteropServices;

namespace RimSharp.Core.Services
{
    public static class SystemInfoServiceFactory
    {
        public static ISystemInfoService Create()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new WindowsSystemInfoService();
            }
            
            // Placeholder for other platforms
            return new NullSystemInfoService();
        }
    }

    public class NullSystemInfoService : ISystemInfoService
    {
        public long GetPrimaryGpuVram() => 0;
    }
}
