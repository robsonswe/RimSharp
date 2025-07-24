// Core/Services/SystemInfoService.cs
#nullable enable
using System;
using System.Linq;
using System.Management;

namespace RimSharp.Core.Services
{
    /// <summary>
    /// Provides system hardware information by querying WMI on Windows.
    /// </summary>
    public class SystemInfoService : ISystemInfoService
    {
        public long GetPrimaryGpuVram()
        {
            try
            {
                // Query WMI for video controller information
                using var searcher = new ManagementObjectSearcher("SELECT AdapterRAM FROM Win32_VideoController");

                // Typically, the first result is the primary GPU.
                var firstGpu = searcher.Get().OfType<ManagementObject>().FirstOrDefault();

                if (firstGpu?["AdapterRAM"] is object ramValue)
                {
                    // AdapterRAM is often a UInt32, but using Convert.ToInt64 is robust.
                    return Convert.ToInt64(ramValue);
                }
            }
            catch (Exception)
            {
                // WMI can fail if permissions are insufficient or on non-Windows systems.
                // Silently fail and return 0.
            }

            return 0;
        }
    }
}