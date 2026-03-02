// Features/VramAnalysis/Tools/VramEstimator.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using RimSharp.Infrastructure.Mods.IO;

namespace RimSharp.Features.VramAnalysis.Tools
{
    public record ConditionalDependency(string PackageId, bool IsActive);

    public class VramEstimationResult
    {
        public long EstimatedVramUncompressed { get; set; }
        public long EstimatedVramCompressed { get; set; }
        public long MaxEstimatedVramUncompressed { get; set; }
        public long MaxEstimatedVramCompressed { get; set; }
        public int TextureCount { get; set; }
        public int MaxTextureCount { get; set; }
        public int InAtlasCount { get; set; }
        public int MaxInAtlasCount { get; set; }
        public bool HasConditionalContent => EstimatedVramCompressed != MaxEstimatedVramCompressed;
        public List<ConditionalDependency> ConditionalDependencies { get; set; } = new();
        public string Logs { get; set; } = string.Empty; 
    }

    public static class VramEstimator
    {
        private enum SimpleTextureFormat { Unknown, RGBA32, RGB24, DXT1, DXT3, DXT5, BC7 }
        private static readonly HashSet<string> TextureExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".dds", ".jpg", ".jpeg", ".psd" };

        public static VramEstimationResult Calculate(string modPath, string majorGameVersion, IReadOnlySet<string> activeModPackageIds)
        {
            var sb = new StringBuilder();
            string modName = Path.GetFileName(modPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            sb.AppendLine($"=== Analyzing {modName} for RimWorld v{majorGameVersion} ===");

            if (string.IsNullOrEmpty(modPath) || !Directory.Exists(modPath))
            {
                sb.AppendLine("[ERROR] Mod path is invalid or does not exist.");
                return new VramEstimationResult { Logs = sb.ToString() };
            }

            var resolved = ModFolderResolver.Resolve(modPath, majorGameVersion, activeModPackageIds, sb);
            var dependencies = resolved.Dependencies.Select(d => new ConditionalDependency(d, activeModPackageIds.Contains(d))).ToList();

            sb.AppendLine($"[INFO] Final Active Folders: {(resolved.Current.Count > 0 ? string.Join(", ", resolved.Current) : "NONE")}");

            var currentAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var maxAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var folder in resolved.Max)
                    AddTexturesFromResolvedPath(modPath, folder, maxAssets, sb);

                foreach (var folder in resolved.Current)
                    AddTexturesFromResolvedPath(modPath, folder, currentAssets, sb);
            }
            catch (Exception ex) 
            { 
                sb.AppendLine($"[ERROR] Exception mapping textures: {ex.Message}");
            }

            if (maxAssets.Count == 0)
            {
                sb.AppendLine($"[WARN] Strict XML parsing found 0 textures. The modder likely made a folder typo. Initiating brute-force scan...");

                int fallbackCount = 0;
                var allTextureDirs = Directory.GetDirectories(modPath, "Textures", SearchOption.AllDirectories);
                foreach (var tDir in allTextureDirs)
                {
                    foreach (var file in Directory.EnumerateFiles(tDir, "*.*", SearchOption.AllDirectories))
                    {
                        string ext = Path.GetExtension(file);
                        if (!TextureExtensions.Contains(ext)) continue;

                        string assetName = Path.GetRelativePath(tDir, file);
                        assetName = Path.ChangeExtension(assetName, null).ToLowerInvariant();

                        if (maxAssets.TryGetValue(assetName, out var existing))
                        {
                            if (GetFormatPriority(ext) < GetFormatPriority(Path.GetExtension(existing)))
                            {
                                maxAssets[assetName] = file;
                                currentAssets[assetName] = file;
                            }
                        }
                        else
                        {
                            maxAssets[assetName] = file;
                            currentAssets[assetName] = file;
                            fallbackCount++;
                        }
                    }
                }
                sb.AppendLine($"[BRUTE-FORCE] Discovered {fallbackCount} unmapped textures.");
            }

            var uniqueMaxFiles = maxAssets.Values.ToHashSet();
            var uniqueCurrentFiles = currentAssets.Values.ToHashSet();

            sb.AppendLine($"[INFO] Unique image files mapped - Active: {uniqueCurrentFiles.Count} | Max: {uniqueMaxFiles.Count}");

            long currentVramUncompressed = 0, currentVramCompressed = 0;
            long maxVramUncompressed = 0, maxVramCompressed = 0;
            int currentTextureCount = 0, maxTextureCount = 0;
            int currentInAtlasCount = 0, maxInAtlasCount = 0;
            int parseFailures = 0;

            const double mipmapFactor = 1.33333;

            Parallel.ForEach(uniqueMaxFiles, file =>
            {
                var (width, height, format, hasMips) = GetTextureInfo(file);

                if (width <= 0 || height <= 0 || format == SimpleTextureFormat.Unknown)
                {
                    Interlocked.Increment(ref parseFailures);
                    width = width <= 0 ? 64 : width;
                    height = height <= 0 ? 64 : height;
                    format = SimpleTextureFormat.RGBA32;
                }

                long baseSizeUncompressed = (long)(width * height * GetBytesPerPixel(format, isCompressed: false));
                long vramUncompressed = (hasMips && (format == SimpleTextureFormat.RGBA32 || format == SimpleTextureFormat.RGB24)) ? (long)(baseSizeUncompressed * mipmapFactor) : baseSizeUncompressed;
                long vramCompressed = (long)(width * height * GetBytesPerPixel(format, isCompressed: true));
                bool isInAtlas = width < 512 && height < 512;

                Interlocked.Increment(ref maxTextureCount);
                Interlocked.Add(ref maxVramUncompressed, vramUncompressed);
                Interlocked.Add(ref maxVramCompressed, vramCompressed);
                if (isInAtlas) Interlocked.Increment(ref maxInAtlasCount);

                if (uniqueCurrentFiles.Contains(file))
                {
                    Interlocked.Increment(ref currentTextureCount);
                    Interlocked.Add(ref currentVramUncompressed, vramUncompressed);
                    Interlocked.Add(ref currentVramCompressed, vramCompressed);
                    if (isInAtlas) Interlocked.Increment(ref currentInAtlasCount);
                }
            });

            if (parseFailures > 0)
                sb.AppendLine($"[WARN] {parseFailures} dummy/corrupt images bypassed using standard 64x64 fallback.");

            sb.AppendLine($"=== Finished. VRAM (Compressed): {currentVramCompressed} bytes ===");

            return new VramEstimationResult {
                EstimatedVramUncompressed = currentVramUncompressed, EstimatedVramCompressed = currentVramCompressed,
                MaxEstimatedVramUncompressed = maxVramUncompressed, MaxEstimatedVramCompressed = maxVramCompressed,
                ConditionalDependencies = dependencies, TextureCount = currentTextureCount, MaxTextureCount = maxTextureCount,
                InAtlasCount = currentInAtlasCount, MaxInAtlasCount = maxInAtlasCount,
                Logs = sb.ToString()
            };
        }

        private static void AddTexturesFromResolvedPath(string modPath, string relativePath, Dictionary<string, string> assetMap, StringBuilder sb)
        {
            string fullPath = Path.GetFullPath(Path.Combine(modPath, relativePath));
            if (!Directory.Exists(fullPath))
            {
                sb.AppendLine($"[DIR] SKIPPING: Path defined in LoadFolders does not exist on disk: {relativePath}");
                return;
            }

            var dirs = Directory.GetDirectories(fullPath);
            string? texturesDir = dirs.FirstOrDefault(d => Path.GetFileName(d).Equals("Textures", StringComparison.OrdinalIgnoreCase));

            if (texturesDir == null || !Directory.Exists(texturesDir))
            {
                sb.AppendLine($"[DIR] SKIPPING: No 'Textures' folder exists inside: {relativePath}");
                return;
            }

            int count = 0;
            foreach (var file in Directory.EnumerateFiles(texturesDir, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file);
                if (!TextureExtensions.Contains(ext)) continue;

                string assetName = Path.GetRelativePath(texturesDir, file);
                assetName = Path.ChangeExtension(assetName, null).ToLowerInvariant();

                if (assetMap.TryGetValue(assetName, out var existing))
                {
                    if (GetFormatPriority(ext) < GetFormatPriority(Path.GetExtension(existing)))
                        assetMap[assetName] = file;
                }
                else assetMap[assetName] = file;

                count++;
            }

            sb.AppendLine($"[DIR] SUCCESS: Discovered {count} image files in {relativePath}/Textures");
        }

        private static int GetFormatPriority(string ext) {
            ext = ext.ToLowerInvariant();
            if (ext == ".dds") return 0;
            if (ext == ".png") return 1;
            if (ext == ".jpg" || ext == ".jpeg") return 2;
            if (ext == ".psd") return 3;
            return 4;
        }

        private static double GetBytesPerPixel(SimpleTextureFormat format, bool isCompressed) {
            if (isCompressed) return format switch { SimpleTextureFormat.DXT1 => 0.5, SimpleTextureFormat.DXT3 => 1.0, SimpleTextureFormat.DXT5 => 1.0, SimpleTextureFormat.BC7 => 1.0, _ => 1.0 };
            return format switch { SimpleTextureFormat.RGBA32 => 4.0, SimpleTextureFormat.RGB24 => 3.0, SimpleTextureFormat.DXT1 => 0.5, SimpleTextureFormat.DXT3 => 1.0, SimpleTextureFormat.DXT5 => 1.0, SimpleTextureFormat.BC7 => 1.0, _ => 0 };
        }

        private static (int Width, int Height, SimpleTextureFormat Format, bool HasMips) GetTextureInfo(string filePath) {
            try { return Path.GetExtension(filePath).ToLowerInvariant() switch { ".png" => GetPngInfo(filePath), ".dds" => GetDdsInfo(filePath), ".psd" => GetPsdInfo(filePath), ".jpg" or ".jpeg" => GetJpgInfo(filePath), _ => (0, 0, SimpleTextureFormat.Unknown, false) }; }
            catch { return (64, 64, SimpleTextureFormat.RGBA32, false); }
        }

        private static (int, int, SimpleTextureFormat, bool) GetPngInfo(string filePath) {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length < 24) return (64, 64, SimpleTextureFormat.RGBA32, false);

            using var s = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var r = new BinaryReader(s);
            byte[] sig = r.ReadBytes(8); if (sig.Length != 8 || sig[0] != 0x89 || sig[1] != 0x50) return (64, 64, SimpleTextureFormat.RGBA32, false);
            r.ReadBytes(4); r.ReadBytes(4); byte[] wB = r.ReadBytes(4); byte[] hB = r.ReadBytes(4);
            if (BitConverter.IsLittleEndian) { Array.Reverse(wB); Array.Reverse(hB); }
            int w = BitConverter.ToInt32(wB, 0); int h = BitConverter.ToInt32(hB, 0);

            if (w <= 0 || h <= 0 || w > 16384 || h > 16384) return (64, 64, SimpleTextureFormat.RGBA32, false);

            return (w, h, SimpleTextureFormat.RGBA32, (w % 4 == 0) && (h % 4 == 0));
        }

        private static (int, int, SimpleTextureFormat, bool) GetDdsInfo(string filePath) {
            using var s = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var r = new BinaryReader(s);
            if (r.BaseStream.Length < 128) return (0, 0, SimpleTextureFormat.Unknown, false);
            r.BaseStream.Seek(8, SeekOrigin.Begin);
            uint fl = r.ReadUInt32(); int h = r.ReadInt32(); int w = r.ReadInt32();
            r.BaseStream.Seek(8, SeekOrigin.Current); int mips = r.ReadInt32();
            bool hasMips = (fl & 0x20000) != 0 || mips > 1;
            r.BaseStream.Seek(44, SeekOrigin.Current); r.ReadUInt32(); uint pfFl = r.ReadUInt32();

            if ((pfFl & 0x4) != 0) {
                var fourCC = new string(r.ReadChars(4)).TrimEnd('\0');
                if (fourCC.Equals("DXT1", StringComparison.OrdinalIgnoreCase)) return (w, h, SimpleTextureFormat.DXT1, hasMips);
                if (fourCC.Equals("DXT3", StringComparison.OrdinalIgnoreCase)) return (w, h, SimpleTextureFormat.DXT3, hasMips);
                if (fourCC.Equals("DXT5", StringComparison.OrdinalIgnoreCase)) return (w, h, SimpleTextureFormat.DXT5, hasMips);
                if (fourCC.Equals("DX10", StringComparison.OrdinalIgnoreCase)) return (w, h, SimpleTextureFormat.BC7, hasMips);
            }

            return (w, h, SimpleTextureFormat.RGBA32, hasMips); 
        }

        private static (int, int, SimpleTextureFormat, bool) GetPsdInfo(string filePath) {
            using var s = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var r = new BinaryReader(s);
            if (r.BaseStream.Length < 26) return (0, 0, SimpleTextureFormat.Unknown, false);
            r.BaseStream.Seek(14, SeekOrigin.Begin);
            byte[] hB = r.ReadBytes(4); byte[] wB = r.ReadBytes(4);
            if (BitConverter.IsLittleEndian) { Array.Reverse(wB); Array.Reverse(hB); }
            return (BitConverter.ToInt32(wB, 0), BitConverter.ToInt32(hB, 0), SimpleTextureFormat.RGBA32, true);
        }

        private static (int, int, SimpleTextureFormat, bool) GetJpgInfo(string filePath) {
            using var s = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var r = new BinaryReader(s);
            if (r.BaseStream.Length < 4) return (0, 0, SimpleTextureFormat.Unknown, false);
            if (r.ReadByte() != 0xFF || r.ReadByte() != 0xD8) return (0, 0, SimpleTextureFormat.Unknown, false);
            while (r.BaseStream.Position < r.BaseStream.Length) {
                byte marker = r.ReadByte(); if (marker != 0xFF) continue; marker = r.ReadByte();
                if ((marker >= 0xC0 && marker <= 0xC3) || (marker >= 0xC5 && marker <= 0xC7) || (marker >= 0xC9 && marker <= 0xCB) || (marker >= 0xCD && marker <= 0xCF)) {
                    r.BaseStream.Seek(3, SeekOrigin.Current);
                    byte[] hB = r.ReadBytes(2); byte[] wB = r.ReadBytes(2);
                    if (BitConverter.IsLittleEndian) { Array.Reverse(wB); Array.Reverse(hB); }
                    return (BitConverter.ToUInt16(wB, 0), BitConverter.ToUInt16(hB, 0), SimpleTextureFormat.RGB24, true);
                } else {
                    int len = (r.ReadByte() << 8) | r.ReadByte(); if (len < 2) break;
                    r.BaseStream.Seek(len - 2, SeekOrigin.Current);
                }
            }
            return (0, 0, SimpleTextureFormat.Unknown, false);
        }
    }
}


