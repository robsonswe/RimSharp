
#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RimSharp.Core.Services
{
    /// <summary>

    /// </summary>
    public class LinuxSystemInfoService : ISystemInfoService
    {
        private const string NvidiaSmiExecutable = "nvidia-smi";
        private const string NvidiaSmiArguments = "--query-gpu=memory.total --format=csv,noheader,nounits";
        private const int NvidiaSmiTimeoutMs = 3000;
        private const ulong Mebibyte = 1024UL * 1024UL;

        private static readonly string[] DrmCardPaths =
        {
            "/sys/class/drm/card0/device/mem_info_vram_total",
            "/sys/class/drm/card1/device/mem_info_vram_total",
            "/sys/class/drm/card2/device/mem_info_vram_total",
            "/sys/class/drm/card3/device/mem_info_vram_total"
        };

        public long GetPrimaryGpuVram()
        {
            ulong nvidiaVram = TryGetNvidiaVram();
            if (nvidiaVram > 0)
            {
                return ToLongBytes(nvidiaVram);
            }

            ulong amdVram = TryGetAmdVram();
            if (amdVram > 0)
            {
                return ToLongBytes(amdVram);
            }

            return 0;
        }

        private static ulong TryGetNvidiaVram()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = NvidiaSmiExecutable,
                    Arguments = NvidiaSmiArguments,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return 0;
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();

                if (!process.WaitForExit(NvidiaSmiTimeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception)
                    {
                        // Process already gone or cannot be terminated
                    }

                    return 0;
                }

                string output = outputTask.GetAwaiter().GetResult();
                return ParseNvidiaSmiOutput(output);
            }
            catch (Exception)
            {
                // nvidia-smi not installed or failed to run
                return 0;
            }
        }

        private static ulong TryGetAmdVram()
        {
            foreach (string path in DrmCardPaths)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    ulong vram = ParseSysFsVram(File.ReadAllText(path));
                    if (vram > 0)
                    {
                        return vram;
                    }
                }
                catch (Exception)
                {
                    // Card not readable, try the next candidate
                }
            }

            return 0;
        }

        internal static ulong ParseNvidiaSmiOutput(string? output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return 0;
            }

            string? firstLine = output
                .Split('\n')
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Length > 0);

            if (firstLine is null)
            {
                return 0;
            }

            int commaIndex = firstLine.IndexOf(',');
            string value = (commaIndex >= 0 ? firstLine[..commaIndex] : firstLine).Trim();

            int spaceIndex = value.IndexOf(' ');
            if (spaceIndex >= 0)
            {
                value = value[..spaceIndex];
            }

            if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong mebibytes))
            {
                return 0;
            }

            return mebibytes * Mebibyte;
        }

        internal static ulong ParseSysFsVram(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return 0;
            }

            string value = content.Trim();

            return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong bytes)
                ? bytes
                : 0;
        }

        private static long ToLongBytes(ulong bytes)
        {
            return bytes > long.MaxValue ? long.MaxValue : (long)bytes;
        }
    }
}

