
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

        private static readonly string[] DrmCardResourcePaths =
        {
            "/sys/class/drm/card0/device/resource",
            "/sys/class/drm/card1/device/resource",
            "/sys/class/drm/card2/device/resource",
            "/sys/class/drm/card3/device/resource"
        };

        public long GetPrimaryGpuVram()
        {
            ulong nvidiaVram = TryGetNvidiaVram();
            if (nvidiaVram > 0)
            {
                return ToLongBytes(nvidiaVram);
            }

            ulong sysFsVram = TryGetSysFsVram();
            if (sysFsVram > 0)
            {
                return ToLongBytes(sysFsVram);
            }

            // Last resort: the largest PCI memory BAR of the DRM device is a
            // good VRAM approximation for GPUs whose driver does not expose
            // mem_info_vram_total (Intel, virtual GPUs, older drivers).
            ulong barVram = TryGetPciBarVram();
            if (barVram > 0)
            {
                return ToLongBytes(barVram);
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

        private static ulong TryGetSysFsVram()
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

        private static ulong TryGetPciBarVram()
        {
            foreach (string path in DrmCardResourcePaths)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    ulong barSize = ParseSysFsResource(File.ReadAllText(path));
                    if (barSize > 0)
                    {
                        return barSize;
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

        /// <summary>
        /// Parses a PCI sysfs resource file (lines of "start end flags" in
        /// hexadecimal) and returns the size of the largest memory region,
        /// which approximates the GPU VRAM. I/O port regions are ignored.
        /// </summary>
        internal static ulong ParseSysFsResource(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return 0;
            }

            const ulong IoResourceFlag = 0x100;
            const ulong MemoryResourceFlag = 0x200;

            ulong largestRegion = 0;

            foreach (string line in content.Split('\n'))
            {
                string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                {
                    continue;
                }

                if (!TryParseHex(parts[0], out ulong start) ||
                    !TryParseHex(parts[1], out ulong end) ||
                    !TryParseHex(parts[2], out ulong flags))
                {
                    continue;
                }

                if (start == 0 && end == 0)
                {
                    continue;
                }

                if ((flags & MemoryResourceFlag) == 0 || (flags & IoResourceFlag) != 0)
                {
                    continue;
                }

                ulong size = end - start + 1;
                if (size > largestRegion)
                {
                    largestRegion = size;
                }
            }

            return largestRegion;
        }

        private static bool TryParseHex(string value, out ulong result)
        {
            string normalized = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? value[2..]
                : value;

            return ulong.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        private static long ToLongBytes(ulong bytes)
        {
            return bytes > long.MaxValue ? long.MaxValue : (long)bytes;
        }
    }
}

