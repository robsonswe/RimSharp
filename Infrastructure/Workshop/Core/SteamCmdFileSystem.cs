#nullable enable
using System;
using System.Diagnostics;
using System.IO;
// using System.Runtime.InteropServices; // No longer needed if only ClearDepotCache remains
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Workshop.Core
{
    /// <summary>
    /// Handles file system operations related to SteamCMD, primarily managing the depot cache.
    /// </summary>
    public class SteamCmdFileSystem : ISteamCmdFileSystem
    {
        private readonly ISteamCmdPathService _pathService;
        // Remove platform info if not needed for other methods
        // private readonly SteamCmdPlatformInfo _platformInfo; 

        // Adjust constructor if SteamCmdPlatformInfo is removed
        public SteamCmdFileSystem(ISteamCmdPathService pathService /*, SteamCmdPlatformInfo platformInfo */)
        {
            _pathService = pathService;
            // _platformInfo = platformInfo;
        }


        public async Task<bool> ClearDepotCacheAsync()
        {
            string cachePath = _pathService.SteamCmdDepotCachePath;
            if (string.IsNullOrEmpty(cachePath)) return false;

            if (!Directory.Exists(cachePath))
            {
                Console.WriteLine($"Depot cache directory not found, skipping clear: {cachePath}");
                return true; // Nothing to clear is success
            }

            Console.WriteLine($"Attempting to clear depot cache: {cachePath}");
            try
            {
                // Use asynchronous delete if preferred, but recursive sync delete is often fine here.
                // Consider adding retry logic or more robust error handling if needed.
                Directory.Delete(cachePath, true); // Recursive delete
                await Task.Delay(100); // Brief pause for FS
                
                bool deleted = !Directory.Exists(cachePath);
                if (deleted)
                {
                    Console.WriteLine($"Successfully cleared depot cache: {cachePath}");
                }
                else
                {
                    Console.WriteLine($"Warning: Depot cache directory still exists after attempting deletion: {cachePath}");
                }
                return deleted;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to clear depot cache: {ex.Message}");
                // Log the exception details
                // _logger?.LogError($"Failed to clear depot cache: {ex}", "SteamCmdFileSystem"); 
                return false;
            }
        }
    }
}
