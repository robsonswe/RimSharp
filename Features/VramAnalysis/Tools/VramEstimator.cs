// Features/VramAnalysis/Tools/VramEstimator.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading; // Required for Interlocked
using System.Threading.Tasks; // Required for Parallel
using System.Windows.Media.Imaging;
using System.Xml.Linq;

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
    }

    public static class VramEstimator
    {
        private enum SimpleTextureFormat { Unknown, RGBA32, RGB24, DXT1, DXT5, BC7 }
        private static readonly HashSet<string> TextureExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".dds" };

        public static VramEstimationResult Calculate(string modPath, string majorGameVersion, IReadOnlySet<string> activeModPackageIds)
        {
            if (string.IsNullOrEmpty(modPath) || !Directory.Exists(modPath))
                return new VramEstimationResult();

            var (foldersToExclude, dependencies) = GetConditionalExclusions(modPath, majorGameVersion, activeModPackageIds);
            string aboutFolderPathWithSeparator = Path.Combine(modPath, "About") + Path.DirectorySeparatorChar;

            var allFiles = new List<string>();
            try
            {
                allFiles = Directory.EnumerateFiles(modPath, "*.*", SearchOption.AllDirectories)
                    .Where(file => !file.StartsWith(aboutFolderPathWithSeparator, StringComparison.OrdinalIgnoreCase) && 
                                   TextureExtensions.Contains(Path.GetExtension(file)))
                    .ToList();
            }
            catch (Exception) { return new VramEstimationResult(); }

            long currentVramUncompressed = 0, currentVramCompressed = 0;
            long maxVramUncompressed = 0, maxVramCompressed = 0;
            int currentTextureCount = 0, maxTextureCount = 0;
            int currentInAtlasCount = 0, maxInAtlasCount = 0;
            
            const double mipmapFactor = 1.33333;

            Parallel.ForEach(allFiles, file =>
            {
                var (width, height, format, hasMips) = GetTextureInfo(file);
                if (width <= 0 || height <= 0 || format == SimpleTextureFormat.Unknown) return;

                // --- FIX: Explicitly cast the result of the double multiplication to long ---
                long baseSizeUncompressed = (long)(width * height * GetBytesPerPixel(format, isCompressed: false));
                
                long vramUncompressed = (hasMips && (format == SimpleTextureFormat.RGBA32 || format == SimpleTextureFormat.RGB24))
                    ? (long)(baseSizeUncompressed * mipmapFactor)
                    : baseSizeUncompressed;
                
                // --- FIX: Explicitly cast the result of the double multiplication to long ---
                long vramCompressed = (long)(width * height * GetBytesPerPixel(format, isCompressed: true));
                
                bool isInAtlas = width < 512 && height < 512;

                Interlocked.Increment(ref maxTextureCount);
                Interlocked.Add(ref maxVramUncompressed, vramUncompressed);
                Interlocked.Add(ref maxVramCompressed, vramCompressed);
                if (isInAtlas) Interlocked.Increment(ref maxInAtlasCount);

                bool isExcluded = foldersToExclude.Any(excludedFolder => file.StartsWith(excludedFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                if (!isExcluded)
                {
                    Interlocked.Increment(ref currentTextureCount);
                    Interlocked.Add(ref currentVramUncompressed, vramUncompressed);
                    Interlocked.Add(ref currentVramCompressed, vramCompressed);
                    if (isInAtlas) Interlocked.Increment(ref currentInAtlasCount);
                }
            });

            return new VramEstimationResult
            {
                EstimatedVramUncompressed = currentVramUncompressed,
                EstimatedVramCompressed = currentVramCompressed,
                MaxEstimatedVramUncompressed = maxVramUncompressed,
                MaxEstimatedVramCompressed = maxVramCompressed,
                ConditionalDependencies = dependencies,
                TextureCount = currentTextureCount,
                MaxTextureCount = maxTextureCount,
                InAtlasCount = currentInAtlasCount,
                MaxInAtlasCount = maxInAtlasCount
            };
        }
        
        private static (IReadOnlySet<string> Folders, List<ConditionalDependency> Dependencies) GetConditionalExclusions(string modPath, string majorGameVersion, IReadOnlySet<string> activeModPackageIds)
        {
            var loadFoldersXmlPath = Path.Combine(modPath, "loadFolders.xml");
            var foldersToExclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dependencies = new List<ConditionalDependency>();
            if (File.Exists(loadFoldersXmlPath))
            {
                try
                {
                    var doc = XDocument.Load(loadFoldersXmlPath);
                    XElement? versionNode = FindBestVersionNode(doc, majorGameVersion);
                    if (versionNode != null)
                    {
                        foreach (var li in versionNode.Elements("li"))
                        {
                            var ifModActiveAttr = li.Attribute("IfModActive");
                            if (ifModActiveAttr != null)
                            {
                                bool isActive = activeModPackageIds.Contains(ifModActiveAttr.Value);
                                dependencies.Add(new ConditionalDependency(ifModActiveAttr.Value, isActive));
                                if (!isActive)
                                {
                                    var relativePath = li.Value.Trim().Replace('/', Path.DirectorySeparatorChar);
                                    var fullPath = Path.GetFullPath(Path.Combine(modPath, relativePath));
                                    if (Directory.Exists(fullPath)) foldersToExclude.Add(fullPath);
                                }
                            }
                        }
                    }
                }
                catch (Exception) { /* If parsing fails, don't exclude anything */ }
            }
            return (foldersToExclude, dependencies);
        }

        private static XElement? FindBestVersionNode(XDocument doc, string majorGameVersion) { var specificVersionNode = doc.Root?.Elements("v" + majorGameVersion).FirstOrDefault(); if (specificVersionNode != null) return specificVersionNode; return doc.Root?.Elements().Where(e => e.Name.LocalName.StartsWith("v", StringComparison.OrdinalIgnoreCase)).Select(e => new { Element = e, Version = Version.TryParse(e.Name.LocalName.Substring(1), out var v) ? v : null }).Where(x => x.Version != null).OrderByDescending(x => x.Version).FirstOrDefault()?.Element; }
        private static double GetBytesPerPixel(SimpleTextureFormat format, bool isCompressed) { if (isCompressed) return format switch { SimpleTextureFormat.DXT1 => 0.5, _ => 1.0 }; return format switch { SimpleTextureFormat.RGBA32 => 4.0, SimpleTextureFormat.RGB24 => 3.0, SimpleTextureFormat.DXT1 => 0.5, SimpleTextureFormat.DXT5 => 1.0, SimpleTextureFormat.BC7 => 1.0, _ => 0 }; }
        private static (int Width, int Height, SimpleTextureFormat Format, bool HasMips) GetTextureInfo(string filePath) { try { return Path.GetExtension(filePath).ToLowerInvariant() switch { ".png" => GetPngInfo(filePath), ".dds" => GetDdsInfo(filePath), _ => (0, 0, SimpleTextureFormat.Unknown, false) }; } catch { return (0, 0, SimpleTextureFormat.Unknown, false); } }
        private static (int, int, SimpleTextureFormat, bool) GetPngInfo(string filePath) { using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read); var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None); return (decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight, SimpleTextureFormat.RGBA32, (decoder.Frames[0].PixelWidth % 4 == 0) && (decoder.Frames[0].PixelHeight % 4 == 0)); }
        private static (int, int, SimpleTextureFormat, bool) GetDdsInfo(string filePath) { using var reader = new BinaryReader(File.OpenRead(filePath)); if (reader.BaseStream.Length < 128) return (0, 0, SimpleTextureFormat.Unknown, false); reader.BaseStream.Seek(8, SeekOrigin.Begin); uint headerFlags = reader.ReadUInt32(); int height = reader.ReadInt32(); int width = reader.ReadInt32(); reader.BaseStream.Seek(20, SeekOrigin.Current); int mipMapCount = reader.ReadInt32(); bool hasMips = (headerFlags & 0x20000) != 0 || mipMapCount > 1; reader.BaseStream.Seek(44, SeekOrigin.Current); if ((reader.ReadUInt32() & 0x4) != 0) { var fourCC = new string(reader.ReadChars(4)); if (fourCC.StartsWith("DXT1")) return (width, height, SimpleTextureFormat.DXT1, hasMips); if (fourCC.StartsWith("DXT5")) return (width, height, SimpleTextureFormat.DXT5, hasMips); if (fourCC.StartsWith("DX10")) return (width, height, SimpleTextureFormat.BC7, hasMips); } return (width, height, SimpleTextureFormat.Unknown, false); }
    }
}