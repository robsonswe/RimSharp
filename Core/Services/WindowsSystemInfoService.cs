// Core/Services/WindowsSystemInfoService.cs
#nullable enable
using System;
using System.Linq;
using System.Management;

namespace RimSharp.Core.Services
{
    /// <summary>
    /// Provides system hardware information by querying WMI on Windows.
    /// </summary>
    public class WindowsSystemInfoService : ISystemInfoService
    {
        public long GetPrimaryGpuVram()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT AdapterRAM FROM Win32_VideoController");
                var firstGpu = searcher.Get().Cast<ManagementObject>().FirstOrDefault();

                if (firstGpu?["AdapterRAM"] is object ramValue)
                {
                    return Convert.ToInt64(ramValue);
                }
            }
            catch (Exception)
            {
                // WMI failure or non-Windows system
            }

            return 0;
        }
    }
}
