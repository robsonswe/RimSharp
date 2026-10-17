// Features/VramAnalysis/Tools/GameContentVramSource.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace RimSharp.Features.VramAnalysis.Tools
{
    public record GameContentVramResult(long VramCompressed, long VramUncompressed, int TextureCount, int InAtlasCount, string Logs);

    public static class GameContentVramSource
    {
        // Unity TextureFormat ids that store BC-compressed data directly (the stored
        // byte count is therefore the VRAM cost, mips included).
        private static readonly HashSet<int> BcTextureFormats = new() { 10, 11, 12, 24, 25, 26, 27, 28, 29 };

        // Player-data folder names, checked in order via directory enumeration so the
        // matching stays case-correct on case-sensitive file systems.
        private static readonly string[] PlayerDataFolderNames = { "RimWorldWin64_Data", "RimWorld_Data" };

        private const double MipFactor = 1.33333;
        private const int PlayerDataSearchLevels = 3;

        public static GameContentVramResult Estimate(string contentModPath, StringBuilder? log = null)
        {
            var sb = log ?? new StringBuilder();

            string? zipPath = FindAssetsZip(contentModPath);
            if (zipPath != null)
            {
                return EstimateFromAssetsZip(zipPath, sb);
            }

            return EstimateFromUnityContainers(contentModPath, sb);
        }

        private static GameContentVramResult EstimateFromAssetsZip(string zipPath, StringBuilder sb)
        {
            sb.AppendLine($"[GAME-CONTENT] Using Assets.zip: {zipPath}");

            try
            {
                long totalBytes = 0;
                int ddsCount = 0;

                using var archive = ZipFile.OpenRead(zipPath);
                foreach (var entry in archive.Entries)
                {
                    if (!entry.Name.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) continue;

                    totalBytes += entry.Length;
                    ddsCount++;
                }

                sb.AppendLine($"[GAME-CONTENT] {ddsCount} .dds entries, {totalBytes} bytes.");
                sb.AppendLine("[GAME-CONTENT] Entries are baked BC data; compressed == uncompressed and atlas classification is unavailable.");

                return new GameContentVramResult(totalBytes, totalBytes, ddsCount, 0, sb.ToString());
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[GAME-CONTENT] Failed to read Assets.zip: {ex.Message}");
                return new GameContentVramResult(0, 0, 0, 0, sb.ToString());
            }
        }

        private static GameContentVramResult EstimateFromUnityContainers(string contentModPath, StringBuilder sb)
        {
            long vramCompressed = 0, vramUncompressed = 0;
            int textureCount = 0, inAtlasCount = 0;

            string? bundlesDirectory = FindChildDirectory(contentModPath, "AssetBundles");
            if (bundlesDirectory != null)
            {
                foreach (var bundleFile in Directory.EnumerateFiles(bundlesDirectory))
                {
                    if (bundleFile.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"[GAME-CONTENT] Skipping bundle manifest: {Path.GetFileName(bundleFile)}");
                        continue;
                    }

                    var (compressed, uncompressed, count, inAtlas) = EstimateFromBundle(bundleFile, sb);
                    vramCompressed += compressed;
                    vramUncompressed += uncompressed;
                    textureCount += count;
                    inAtlasCount += inAtlas;
                }
            }
            else
            {
                string? playerDataDirectory = FindPlayerDataDirectory(contentModPath);
                if (playerDataDirectory != null)
                {
                    string assetsPath = Path.Combine(playerDataDirectory, "resources.assets");
                    if (File.Exists(assetsPath))
                    {
                        var (compressed, uncompressed, count, inAtlas) = EstimateFromAssetsFile(assetsPath, sb);
                        vramCompressed += compressed;
                        vramUncompressed += uncompressed;
                        textureCount += count;
                        inAtlasCount += inAtlas;
                    }
                    else
                    {
                        sb.AppendLine($"[GAME-CONTENT] No resources.assets found in '{playerDataDirectory}'.");
                    }
                }
                else
                {
                    sb.AppendLine($"[GAME-CONTENT] No Unity containers found for '{contentModPath}'.");
                }
            }

            return new GameContentVramResult(vramCompressed, vramUncompressed, textureCount, inAtlasCount, sb.ToString());
        }

        private static (long Compressed, long Uncompressed, int Count, int InAtlas) EstimateFromBundle(string bundlePath, StringBuilder sb)
        {
            var manager = new AssetsManager();
            try
            {
                var bundle = manager.LoadBundleFile(bundlePath, unpackIfPacked: true);
                var assetsFile = manager.LoadAssetsFileFromBundle(bundle, 0, false);

                if (!TryLoadClassDatabase(manager, assetsFile, sb))
                {
                    return (0, 0, 0, 0);
                }

                var totals = AccumulateTextures(manager, assetsFile, sb);
                sb.AppendLine($"[GAME-CONTENT] Bundle '{Path.GetFileName(bundlePath)}': {totals.Count} textures, {totals.Compressed} compressed bytes.");
                return totals;
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[GAME-CONTENT] Failed to read bundle '{Path.GetFileName(bundlePath)}': {ex.Message}");
                return (0, 0, 0, 0);
            }
            finally
            {
                manager.UnloadAllBundleFiles();
                manager.UnloadAllAssetsFiles();
            }
        }

        private static (long Compressed, long Uncompressed, int Count, int InAtlas) EstimateFromAssetsFile(string assetsPath, StringBuilder sb)
        {
            var manager = new AssetsManager();
            try
            {
                var assetsFile = manager.LoadAssetsFile(assetsPath, false);

                if (!TryLoadClassDatabase(manager, assetsFile, sb))
                {
                    return (0, 0, 0, 0);
                }

                var totals = AccumulateTextures(manager, assetsFile, sb);
                sb.AppendLine($"[GAME-CONTENT] Assets file '{Path.GetFileName(assetsPath)}': {totals.Count} textures, {totals.Compressed} compressed bytes.");
                return totals;
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[GAME-CONTENT] Failed to read assets file '{Path.GetFileName(assetsPath)}': {ex.Message}");
                return (0, 0, 0, 0);
            }
            finally
            {
                manager.UnloadAllAssetsFiles();
            }
        }

        private static bool TryLoadClassDatabase(AssetsManager manager, AssetsFileInstance assetsFile, StringBuilder sb)
        {
            if (assetsFile.file.Metadata.TypeTreeEnabled) return true;

            string? tpkPath = FindClassDataFile();
            if (tpkPath == null)
            {
                sb.AppendLine($"[GAME-CONTENT] Type tree is stripped and classdata.tpk was not found (expected at '{Path.Combine(AppContext.BaseDirectory, "classdata.tpk")}'); skipping container.");
                return false;
            }

            manager.LoadClassPackage(tpkPath);
            manager.LoadClassDatabaseFromPackage(assetsFile.file.Metadata.UnityVersion);
            return true;
        }

        private static (long Compressed, long Uncompressed, int Count, int InAtlas) AccumulateTextures(AssetsManager manager, AssetsFileInstance assetsFile, StringBuilder sb)
        {
            long compressed = 0, uncompressed = 0;
            int count = 0, inAtlas = 0, skipped = 0;

            foreach (AssetFileInfo info in assetsFile.file.GetAssetsOfType(AssetClassID.Texture2D))
            {
                AssetTypeValueField baseField = manager.GetBaseField(assetsFile, info);

                int width = baseField["m_Width"].AsInt;
                int height = baseField["m_Height"].AsInt;
                int format = baseField["m_TextureFormat"].AsInt;
                int completeImageSize = baseField["m_CompleteImageSize"].AsInt;
                int mipCount = baseField["m_MipCount"].IsDummy ? 0 : baseField["m_MipCount"].AsInt;

                long streamSize = 0;
                AssetTypeValueField streamData = baseField["m_StreamData"];
                if (streamData != null && !streamData.IsDummy)
                {
                    AssetTypeValueField sizeField = streamData["size"];
                    if (sizeField != null && !sizeField.IsDummy) streamSize = sizeField.AsUInt;
                }

                long storedBytes = streamSize > 0 ? streamSize : completeImageSize;
                if (storedBytes <= 0)
                {
                    skipped++;
                    continue;
                }

                bool isBcStored = BcTextureFormats.Contains(format);
                double mipFactor = mipCount > 1 ? MipFactor : 1.0;

                uncompressed += storedBytes;
                compressed += isBcStored ? storedBytes : (long)((long)width * height * mipFactor);
                count++;
                if (width < 512 && height < 512) inAtlas++;
            }

            if (skipped > 0)
            {
                sb.AppendLine($"[GAME-CONTENT] Skipped {skipped} textures with no stored size.");
            }

            return (compressed, uncompressed, count, inAtlas);
        }

        private static string? FindAssetsZip(string contentModPath)
        {
            if (string.IsNullOrWhiteSpace(contentModPath)) return null;

            var candidates = new[]
            {
                contentModPath,
                SafeGetParent(contentModPath),
                SafeGetParent(SafeGetParent(contentModPath))
            };

            foreach (var directory in candidates)
            {
                if (string.IsNullOrEmpty(directory)) continue;

                var candidate = Path.Combine(directory, "Assets.zip");
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }

        private static string? FindChildDirectory(string parentPath, string name)
        {
            if (string.IsNullOrWhiteSpace(parentPath) || !Directory.Exists(parentPath)) return null;

            foreach (var directory in Directory.EnumerateDirectories(parentPath))
            {
                if (Path.GetFileName(directory).Equals(name, StringComparison.OrdinalIgnoreCase)) return directory;
            }

            return null;
        }

        private static string? FindPlayerDataDirectory(string contentModPath)
        {
            if (string.IsNullOrWhiteSpace(contentModPath)) return null;

            var current = new DirectoryInfo(contentModPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            for (int level = 0; level < PlayerDataSearchLevels && current != null; level++, current = current.Parent)
            {
                if (!current.Exists) continue;

                foreach (var name in PlayerDataFolderNames)
                {
                    foreach (var directory in Directory.EnumerateDirectories(current.FullName))
                    {
                        if (Path.GetFileName(directory).Equals(name, StringComparison.OrdinalIgnoreCase)) return directory;
                    }
                }
            }

            return null;
        }

        private static string? FindClassDataFile()
        {
            string direct = Path.Combine(AppContext.BaseDirectory, "classdata.tpk");
            if (File.Exists(direct)) return direct;

            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (int level = 0; level < 6 && directory != null; level++, directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, "Resources", "classdata.tpk");
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }

        private static string? SafeGetParent(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.GetDirectoryName(trimmed);
        }
    }
}
