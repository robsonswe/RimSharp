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

            var (currentFolders, maxFolders, dependencies) = ResolveRimworldLoadFolders(modPath, majorGameVersion, activeModPackageIds, sb);
            
            sb.AppendLine($"[INFO] Final Active Folders: {(currentFolders.Count > 0 ? string.Join(", ", currentFolders) : "NONE")}");

            var currentAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var maxAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var folder in maxFolders)
                    AddTexturesFromResolvedPath(modPath, folder, maxAssets, sb);

                foreach (var folder in currentFolders)
                    AddTexturesFromResolvedPath(modPath, folder, currentAssets, sb);
            }
            catch (Exception ex) 
            { 
                sb.AppendLine($"[ERROR] Exception mapping textures: {ex.Message}");
            }

            // --- BRUTE FORCE FALLBACK ---
            // If the modder made a typo in their loadFolders.xml (e.g. mapped "Assets" but folder is "LegacyAssets"),
            // RimWorld will fail to load it. For estimation purposes, we will brute-force scan the whole mod.
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
                
                // FALLBACK for 0-byte, heavily crushed, or locked images (Fixes 'Bionic Icons' mod)
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

        private static (List<string> Current, List<string> Max, List<ConditionalDependency> Dependencies) ResolveRimworldLoadFolders(string modPath, string majorGameVersion, IReadOnlySet<string> activeModPackageIds, StringBuilder sb)
        {
            var current = new List<string>();
            var max = new List<string>();
            var dependencies = new List<ConditionalDependency>();

            var normalizedActiveMods = new HashSet<string>(activeModPackageIds.Select(id => id.ToLowerInvariant()));

            string? xmlPath = null;
            string[] possibleLocations = { modPath, Path.Combine(modPath, "About"), Path.Combine(modPath, "LoadFolders") };
            foreach (var loc in possibleLocations)
            {
                if (!Directory.Exists(loc)) continue;
                var files = Directory.GetFiles(loc, "*.xml");
                var exactFile = files.FirstOrDefault(f => Path.GetFileName(f).Equals("loadfolders.xml", StringComparison.OrdinalIgnoreCase));
                if (exactFile != null) { xmlPath = exactFile; break; }
            }

            if (xmlPath != null)
            {
                sb.AppendLine($"[XML] Found loadFolders.xml at: {Path.GetRelativePath(modPath, xmlPath)}");
                try
                {
                    var doc = XDocument.Load(xmlPath);
                    var root = doc.Root;

                    if (root != null)
                    {
                        string exactTagName = "v" + majorGameVersion;
                        var targetNodes = root.Elements().Where(e => e.Name.LocalName.Equals(exactTagName, StringComparison.OrdinalIgnoreCase)).ToList();
                        
                        if (targetNodes.Count == 0)
                        {
                            sb.AppendLine($"[XML] No <{exactTagName}> found. Checking for <default>.");
                            targetNodes = root.Elements().Where(e => e.Name.LocalName.Equals("default", StringComparison.OrdinalIgnoreCase)).ToList();
                        }
                        
                        if (targetNodes.Count == 0)
                        {
                            // RimWorld Version Fallback Logic
                            var availableVersions = root.Elements()
                                .Select(e => e.Name.LocalName.ToLowerInvariant())
                                .Where(n => n.StartsWith("v") && Version.TryParse(n.Substring(1), out _))
                                .Distinct()
                                .ToList();

                            if (availableVersions.Count > 0)
                            {
                                string bestVersion = GetBestFallbackVersion(availableVersions, majorGameVersion);
                                sb.AppendLine($"[XML] No <default> found. Falling back to closest available version tag: <{bestVersion}>");
                                targetNodes = root.Elements().Where(e => e.Name.LocalName.Equals(bestVersion, StringComparison.OrdinalIgnoreCase)).ToList();
                            }
                            else
                            {
                                sb.AppendLine($"[XML] Neither <{exactTagName}> nor <default> existed in the XML. Falling back to directory structure.");
                            }
                        }
                        
                        if (targetNodes.Count > 0)
                        {
                            foreach (var targetNode in targetNodes)
                            {
                                bool isNodeMet = EvaluateConditions(targetNode, normalizedActiveMods, dependencies, sb);

                                foreach (var li in targetNode.Elements().Where(e => e.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)))
                                {
                                    bool isLiMet = EvaluateConditions(li, normalizedActiveMods, dependencies, sb);
                                    string folder = li.Value.Trim().Replace('/', Path.DirectorySeparatorChar);
                                    
                                    folder = folder.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                    if (string.IsNullOrEmpty(folder) || folder == ".") folder = ".";
                                    
                                    if (isNodeMet && isLiMet) current.Add(folder);
                                    max.Add(folder);
                                }
                            }
                            return (current, max, dependencies);
                        }
                    }
                } 
                catch (Exception ex) 
                { 
                    sb.AppendLine($"[XML ERROR] Parsing failed: {ex.Message}");
                }
            }
            else
            {
                sb.AppendLine($"[INFO] No loadFolders.xml found. Proceeding with directory fallback logic.");
            }

            // Fallback Logic if XML missing or didn't provide results
            current.Add(".");
            if (Directory.Exists(Path.Combine(modPath, "Common"))) current.Add("Common");
            
            string ver = majorGameVersion;
            if (Directory.Exists(Path.Combine(modPath, ver))) 
            {
                current.Add(ver);
            }
            else if (Directory.Exists(Path.Combine(modPath, "v" + ver))) 
            {
                current.Add("v" + ver);
            }
            else
            {
                var dirs = Directory.GetDirectories(modPath)
                    .Select(Path.GetFileName)
                    .Where(d => d != null && (Version.TryParse(d, out _) || (d.StartsWith("v", StringComparison.OrdinalIgnoreCase) && Version.TryParse(d.Substring(1), out _))))
                    .Select(d => d!)
                    .ToList();

                if (dirs.Count > 0)
                {
                    string bestDir = GetBestFallbackVersion(dirs, majorGameVersion);
                    sb.AppendLine($"[DIR FALLBACK] Exact folder '{ver}' not found. Falling back to best match: '{bestDir}'");
                    current.Add(bestDir);
                }
            }

            max.AddRange(current);
            return (current, max, dependencies);
        }

        private static string GetBestFallbackVersion(IEnumerable<string> availableVersions, string targetVersionStr)
        {
            if (!Version.TryParse(targetVersionStr, out var targetVersion))
                return availableVersions.OrderByDescending(v => v).FirstOrDefault() ?? targetVersionStr;

            var parsedVersions = new List<(string original, Version version)>();
            foreach (var vStr in availableVersions)
            {
                string clean = vStr.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? vStr.Substring(1) : vStr;
                if (Version.TryParse(clean, out var v))
                    parsedVersions.Add((vStr, v));
            }

            if (!parsedVersions.Any()) return targetVersionStr;

            var lessOrEqual = parsedVersions.Where(x => x.version <= targetVersion).OrderByDescending(x => x.version).FirstOrDefault();
            if (lessOrEqual.original != null) return lessOrEqual.original;

            var greater = parsedVersions.Where(x => x.version > targetVersion).OrderBy(x => x.version).FirstOrDefault();
            return greater.original ?? targetVersionStr;
        }

        private static bool EvaluateConditions(XElement element, HashSet<string> activeMods, List<ConditionalDependency> dependencies, StringBuilder sb)
        {
            bool isMet = true;

            foreach (var attr in element.Attributes())
            {
                string name = attr.Name.LocalName.ToLowerInvariant();
                string val = attr.Value;
                if (string.IsNullOrWhiteSpace(val)) continue;

                var mods = val.Split(',').Select(m => m.Trim().ToLowerInvariant()).ToList();

                if (name == "ifmodactive" || name == "mayrequire" || name == "ifmodactiveany")
                {
                    bool met = mods.Any(m => activeMods.Contains(m));
                    isMet &= met;
                    foreach(var m in mods) dependencies.Add(new ConditionalDependency(m, activeMods.Contains(m)));
                    sb.AppendLine($"[COND] {name}=\"{val}\" -> Evaluated to {met}");
                }
                else if (name == "ifmodactiveall")
                {
                    bool met = mods.All(m => activeMods.Contains(m));
                    isMet &= met;
                    foreach (var m in mods) dependencies.Add(new ConditionalDependency(m, activeMods.Contains(m)));
                    sb.AppendLine($"[COND] {name}=\"{val}\" -> Evaluated to {met}");
                }
                else if (name == "ifmodnotactive" || name == "ifmodnotactiveany")
                {
                    bool met = !mods.Any(m => activeMods.Contains(m));
                    isMet &= met;
                    foreach (var m in mods) dependencies.Add(new ConditionalDependency(m, activeMods.Contains(m)));
                    sb.AppendLine($"[COND] {name}=\"{val}\" -> Evaluated to {met}");
                }
                else if (name == "ifmodnotactiveall")
                {
                    bool met = !mods.All(m => activeMods.Contains(m));
                    isMet &= met;
                    foreach (var m in mods) dependencies.Add(new ConditionalDependency(m, activeMods.Contains(m)));
                    sb.AppendLine($"[COND] {name}=\"{val}\" -> Evaluated to {met}");
                }
            }
            return isMet;
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