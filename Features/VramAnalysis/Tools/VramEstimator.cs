// O que precisa ser levado em conta:
// O carregamento de texturas é variável. Um mod pode ter muitas texturas, mas essas texturas nem sempre vão ser carregaas porque algumas vão depender de mod X ou mod Y
// Para ter uma melhor noção, precisariamos analisar as condicioniais do mod e ver os mods que estão ativos na lista. Num geral, poderiamos também faz um min-max com o min sendo apenas
// As texturas que serão carregadas incondicionalmente, e o carregamento de todas as texturas, considerando possíveis incompatibilidades.
// Isso ficou claro para mim no mod 'Graphic Chair Overhaul'. Esse mod tem uma pasta com patches para dezenas de mods, mas só vão ser carregadas aquelas em que os mods estão ativos.

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging; // Requires a project reference to PresentationCore.dll

namespace RimSharp.Features.VramAnalysis.Tools
{
    /// <summary>
    /// Holds the results of a VRAM estimation calculation for a single mod.
    /// </summary>
    public class VramEstimationResult
    {
        public long EstimatedVramUncompressed { get; set; }
        public long EstimatedVramCompressed { get; set; }
    }

    /// <summary>
    /// A utility to estimate the VRAM usage of texture files.
    /// </summary>
    public static class VramEstimator
    {
        private enum SimpleTextureFormat { Unknown, RGBA32, RGB24, DXT1, DXT5, BC7 }

        private static readonly HashSet<string> TextureExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".dds" // Focus on the most common convertible/direct-use formats
        };

        /// <summary>
        /// Gets all relevant texture file paths from a mod's directory, excluding the 'About' folder.
        /// </summary>
        public static IEnumerable<string> GetTexturePathsForMod(string modPath)
        {
            if (string.IsNullOrEmpty(modPath) || !Directory.Exists(modPath))
            {
                return Enumerable.Empty<string>();
            }

            try
            {
                string aboutFolderPath = Path.Combine(modPath, "About");
                return Directory.EnumerateFiles(modPath, "*.*", SearchOption.AllDirectories)
                                .Where(file => !file.StartsWith(aboutFolderPath, StringComparison.OrdinalIgnoreCase) &&
                                               TextureExtensions.Contains(Path.GetExtension(file)));
            }
            catch (Exception) // Catch potential access errors
            {
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>
        /// Calculates the estimated VRAM usage for a given list of texture file paths.
        /// </summary>
        public static VramEstimationResult Calculate(IEnumerable<string> textureFilePaths)
        {
            long totalVramUncompressed = 0;
            long totalVramCompressed = 0;
            const double mipmapFactor = 1.33333; // Approx. 33% overhead for mipmaps

            foreach (var filePath in textureFilePaths)
            {
                var (width, height, format, hasMips) = GetTextureInfo(filePath);
                if (width <= 0 || height <= 0 || format == SimpleTextureFormat.Unknown) continue;

                // --- Scenario 1: Uncompressed (In-Game Texture Compression OFF) ---
                // PNGs are loaded as RGBA32. DDS files use their native format.
                long baseSizeUncompressed = (long)(width * height * GetBytesPerPixel(format, isCompressed: false));
                if (hasMips && (format == SimpleTextureFormat.RGBA32 || format == SimpleTextureFormat.RGB24))
                {
                    totalVramUncompressed += (long)(baseSizeUncompressed * mipmapFactor);
                }
                else
                {
                    totalVramUncompressed += baseSizeUncompressed;
                }

                // --- Scenario 2: Compressed (In-Game Texture Compression ON) ---
                // Game converts PNGs to DXT5. DDS files use their native format.
                long baseSizeCompressed = (long)(width * height * GetBytesPerPixel(format, isCompressed: true));
                // Mipmaps are included in the DXT calculation, so no extra factor needed.
                totalVramCompressed += baseSizeCompressed;
            }

            return new VramEstimationResult
            {
                EstimatedVramUncompressed = totalVramUncompressed,
                EstimatedVramCompressed = totalVramCompressed
            };
        }

        private static double GetBytesPerPixel(SimpleTextureFormat format, bool isCompressed)
        {
            if (isCompressed)
            {
                // When in-game compression is ON, PNGs are treated as DXT5.
                return format switch
                {
                    SimpleTextureFormat.DXT1 => 0.5,
                    _ => 1.0 // DXT5, BC7, and converted PNGs (RGBA32->DXT5) all use 1 byte/pixel.
                };
            }
            // When in-game compression is OFF
            return format switch
            {
                SimpleTextureFormat.RGBA32 => 4.0,
                SimpleTextureFormat.RGB24 => 3.0,
                SimpleTextureFormat.DXT1 => 0.5,
                SimpleTextureFormat.DXT5 => 1.0,
                SimpleTextureFormat.BC7 => 1.0,
                _ => 0
            };
        }

        private static (int Width, int Height, SimpleTextureFormat Format, bool HasMips) GetTextureInfo(string filePath)
        {
            try
            {
                return Path.GetExtension(filePath).ToLowerInvariant() switch
                {
                    ".png" => GetPngInfo(filePath),
                    ".dds" => GetDdsInfo(filePath),
                    _ => (0, 0, SimpleTextureFormat.Unknown, false)
                };
            }
            catch { return (0, 0, SimpleTextureFormat.Unknown, false); }
        }

        private static (int, int, SimpleTextureFormat, bool) GetPngInfo(string filePath)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            int width = decoder.Frames[0].PixelWidth;
            int height = decoder.Frames[0].PixelHeight;
            // Assume PNGs will generate mipmaps if their dimensions are multiples of 4 (a common requirement).
            bool hasMips = (width % 4 == 0) && (height % 4 == 0);
            return (width, height, SimpleTextureFormat.RGBA32, hasMips);
        }

        private static (int, int, SimpleTextureFormat, bool) GetDdsInfo(string filePath)
        {
            using var reader = new BinaryReader(File.OpenRead(filePath));
            if (reader.BaseStream.Length < 128) return (0, 0, SimpleTextureFormat.Unknown, false);

            reader.BaseStream.Seek(8, SeekOrigin.Begin); // Skip magic number and size
            uint headerFlags = reader.ReadUInt32();
            int height = reader.ReadInt32();
            int width = reader.ReadInt32();

            reader.BaseStream.Seek(20, SeekOrigin.Current); // Skip pitch, depth, mipmapcount
            int mipMapCount = reader.ReadInt32();
            bool hasMips = (headerFlags & 0x20000) != 0 || mipMapCount > 1;

            reader.BaseStream.Seek(44, SeekOrigin.Current); // Skip reserved and pixel format size
            uint pixelFormatFlags = reader.ReadUInt32();

            if ((pixelFormatFlags & 0x4) != 0) // DDPF_FOURCC
            {
                char[] fourCC = reader.ReadChars(4);
                string fourCCString = new string(fourCC);
                if (fourCCString.StartsWith("DXT1")) return (width, height, SimpleTextureFormat.DXT1, hasMips);
                if (fourCCString.StartsWith("DXT5")) return (width, height, SimpleTextureFormat.DXT5, hasMips);
                if (fourCCString.StartsWith("DX10")) return (width, height, SimpleTextureFormat.BC7, hasMips); // BC7 is stored in DX10 header
            }
            return (width, height, SimpleTextureFormat.Unknown, false); // Fallback for other formats
        }
    }
}