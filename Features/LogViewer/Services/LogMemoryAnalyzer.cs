#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace RimSharp.Features.LogViewer.Services
{
    /// <summary>One memory figure the log reports for itself.</summary>
    public sealed record LogMemoryEntry(string Name, double Megabytes, string Source)
    {
        /// <summary>Size as shown in the list, in gigabytes above 1024 MB.</summary>
        public string DisplaySize => Megabytes >= 1024
            ? FormattableString.Invariant($"{Megabytes / 1024:F2} GB")
            : FormattableString.Invariant($"{Megabytes:F0} MB");
    }

    /// <summary>
    /// Reads the memory figures a Player.log reports about the machine it ran on.
    /// The two engine generations report differently: Unity logs print allocator
    /// peaks, while Waylith logs print neither of those but do report the GPU's
    /// device-local budget and the texture heap it uploaded at startup.
    /// </summary>
    public static class LogMemoryAnalyzer
    {
        private const RegexOptions Options =
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static readonly Regex AllocSection = new(@"^\s*\[(ALLOC_[A-Z_]+)\]", Options);

        private static readonly Regex PeakAllocated = new(
            @"Peak Allocated memory\s+([\d.]+)\s*(B|KB|MB|GB)", Options);

        private static readonly Regex GpuLine = new(
            @"GPU \[0\] '([^']+)'.*?(\d+(?:\.\d+)?)\s*MB device-local", Options);

        private static readonly Regex TextureHeap = new(
            @"Textures are uploaded to heap index \d+\s+\(([\d.]+)\s*(B|KB|MB|GiB|MiB|KiB)\)", Options);

        private static readonly Dictionary<string, string> UnityAllocLabels = new(StringComparer.Ordinal)
        {
            ["ALLOC_DEFAULT_MAIN"] = "Game memory (main)",
            ["ALLOC_GFX_MAIN"] = "Graphics memory",
            ["ALLOC_DEFAULT_THREAD"] = "Thread memory"
        };

        public static List<LogMemoryEntry> Analyze(IReadOnlyList<LogDiagnosticLine>? lines, LogFileFormat format)
        {
            if (lines == null || lines.Count == 0)
            {
                return new List<LogMemoryEntry>();
            }

            return format == LogFileFormat.Waylith
                ? ReadWaylithMemory(lines)
                : ReadUnityMemory(lines);
        }

        private static List<LogMemoryEntry> ReadWaylithMemory(IReadOnlyList<LogDiagnosticLine> lines)
        {
            var entries = new List<LogMemoryEntry>();

            foreach (var line in lines)
            {
                var gpu = GpuLine.Match(line.Text);
                if (gpu.Success)
                {
                    entries.Add(new LogMemoryEntry(
                        $"GPU VRAM — {gpu.Groups[1].Value}",
                        double.Parse(gpu.Groups[2].Value, CultureInfo.InvariantCulture),
                        "device-local budget reported by the driver"));
                    break;
                }
            }

            foreach (var line in lines)
            {
                var heap = TextureHeap.Match(line.Text);
                if (!heap.Success) continue;

                entries.Add(new LogMemoryEntry(
                    "Texture residency heap",
                    ToMegabytes(heap.Groups[1].Value, heap.Groups[2].Value),
                    "textures uploaded at startup"));
                break;
            }

            return entries;
        }

        private static List<LogMemoryEntry> ReadUnityMemory(IReadOnlyList<LogDiagnosticLine> lines)
        {
            var entries = new List<LogMemoryEntry>();
            string? section = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var line in lines)
            {
                var sectionMatch = AllocSection.Match(line.Text);
                if (sectionMatch.Success)
                {
                    section = sectionMatch.Groups[1].Value;
                    continue;
                }

                if (section == null) continue;

                var peak = PeakAllocated.Match(line.Text);
                if (!peak.Success) continue;

                if (seen.Add(section) && UnityAllocLabels.TryGetValue(section, out var label))
                {
                    entries.Add(new LogMemoryEntry(
                        label,
                        ToMegabytes(peak.Groups[1].Value, peak.Groups[2].Value),
                        "allocator peak reported by Unity"));
                }

                section = null;
            }

            return entries
                .OrderByDescending(e => e.Megabytes)
                .ToList();
        }

        // Both engines report in binary units (Unity's "MB" is a MiB), so
        // everything normalizes to binary megabytes for display.
        private static double ToMegabytes(string value, string unit)
        {
            var factor = unit.ToUpperInvariant() switch
            {
                "B" => 1d / 1024 / 1024,
                "KB" or "KIB" => 1d / 1024,
                "MB" or "MIB" => 1d,
                "GB" or "GIB" => 1024d,
                _ => 1d
            };

            return double.Parse(value, CultureInfo.InvariantCulture) * factor;
        }
    }
}
