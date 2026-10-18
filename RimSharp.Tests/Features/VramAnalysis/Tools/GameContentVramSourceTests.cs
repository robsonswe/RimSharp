using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using FluentAssertions;
using RimSharp.Features.VramAnalysis.Tools;
using Xunit;

namespace RimSharp.Tests.Features.VramAnalysis.Tools
{
    public class GameContentVramSourceTests
    {
        private readonly ITestOutputHelper _output;

        public GameContentVramSourceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Estimate_ShouldSumDdsEntrySizes_ForDlcLayout()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "RimSharpGameContentTest_" + Guid.NewGuid());
            try
            {
                var dlcPath = Path.Combine(tempRoot, "Royalty");
                Directory.CreateDirectory(dlcPath);
                CreateAssetsZip(Path.Combine(dlcPath, "Assets.zip"), new[]
                {
                    ("alpha.dds", 1024),
                    ("beta.dds", 2048),
                    ("preview.png", 4096)
                });

                var result = GameContentVramSource.Estimate(dlcPath);

                result.VramCompressed.Should().Be(3072);
                result.VramUncompressed.Should().Be(3072);
                result.TextureCount.Should().Be(2);
                result.InAtlasCount.Should().Be(0);
            }
            finally
            {
                CleanupDirectory(tempRoot);
            }
        }

        [Fact]
        public void Estimate_ShouldFindAssetsZip_AtCoreGrandparent()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "RimSharpGameContentTest_" + Guid.NewGuid());
            try
            {
                var corePath = Path.Combine(tempRoot, "Data", "Core");
                Directory.CreateDirectory(corePath);
                CreateAssetsZip(Path.Combine(tempRoot, "Assets.zip"), new[]
                {
                    ("core.dds", 4096)
                });

                var result = GameContentVramSource.Estimate(corePath);

                result.VramCompressed.Should().Be(4096);
                result.VramUncompressed.Should().Be(4096);
                result.TextureCount.Should().Be(1);
                result.InAtlasCount.Should().Be(0);
            }
            finally
            {
                CleanupDirectory(tempRoot);
            }
        }

        [Fact]
        public void Estimate_ShouldReturnZeros_WhenNoAssetsZipFound()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "RimSharpGameContentTest_" + Guid.NewGuid());
            try
            {
                var modPath = Path.Combine(tempRoot, "SomeMod");
                Directory.CreateDirectory(modPath);

                var result = GameContentVramSource.Estimate(modPath);

                result.VramCompressed.Should().Be(0);
                result.VramUncompressed.Should().Be(0);
                result.TextureCount.Should().Be(0);
                result.InAtlasCount.Should().Be(0);
            }
            finally
            {
                CleanupDirectory(tempRoot);
            }
        }

        [Fact]
        public void Estimate_ShouldReturnZeros_WhenZipIsCorrupt()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "RimSharpGameContentTest_" + Guid.NewGuid());
            try
            {
                var modPath = Path.Combine(tempRoot, "CorruptMod");
                Directory.CreateDirectory(modPath);
                File.WriteAllBytes(Path.Combine(modPath, "Assets.zip"), new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 });

                var result = GameContentVramSource.Estimate(modPath);

                result.VramCompressed.Should().Be(0);
                result.VramUncompressed.Should().Be(0);
                result.TextureCount.Should().Be(0);
                result.InAtlasCount.Should().Be(0);
            }
            finally
            {
                CleanupDirectory(tempRoot);
            }
        }

        [Fact]
        public void Estimate_ShouldReturnZeros_WhenBundleIsCorrupt()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "RimSharpGameContentTest_" + Guid.NewGuid());
            try
            {
                var bundlesDirectory = Path.Combine(tempRoot, "Mod", "AssetBundles");
                Directory.CreateDirectory(bundlesDirectory);
                File.WriteAllBytes(Path.Combine(bundlesDirectory, "resources_test"), new byte[] { 0x55, 0x6E, 0x69, 0x74, 0x79, 0x46, 0x53, 0x00, 0x01, 0x02 });

                var result = GameContentVramSource.Estimate(Path.Combine(tempRoot, "Mod"));

                result.VramCompressed.Should().Be(0);
                result.VramUncompressed.Should().Be(0);
                result.TextureCount.Should().Be(0);
                result.InAtlasCount.Should().Be(0);
                result.Logs.Should().Contain("Failed to read bundle");
            }
            finally
            {
                CleanupDirectory(tempRoot);
            }
        }

        [Fact]
        public void Estimate_ShouldSkipManifestFiles_InAssetBundles()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "RimSharpGameContentTest_" + Guid.NewGuid());
            try
            {
                var bundlesDirectory = Path.Combine(tempRoot, "Mod", "AssetBundles");
                Directory.CreateDirectory(bundlesDirectory);
                File.WriteAllBytes(Path.Combine(bundlesDirectory, "resources_test.manifest"), new byte[] { 0x01, 0x02, 0x03 });

                var result = GameContentVramSource.Estimate(Path.Combine(tempRoot, "Mod"));

                result.VramCompressed.Should().Be(0);
                result.TextureCount.Should().Be(0);
                result.Logs.Should().Contain("Skipping bundle manifest");
                result.Logs.Should().NotContain("Failed to read bundle");
            }
            finally
            {
                CleanupDirectory(tempRoot);
            }
        }

        /// <summary>
        /// Opt-in real-install test. Set <c>RIMSHARP_TEST_GAME_16</c> to a RimWorld 1.6 install
        /// root (Windows or Linux player-data layout). Skipped when the variable is unset, empty,
        /// or points to a missing directory, or when the install has no Royalty asset bundles.
        /// </summary>
        [Fact]
        public void Estimate_ShouldProduceNumbers_WhenUnityContainersPresent()
        {
            var gameRoot = Environment.GetEnvironmentVariable("RIMSHARP_TEST_GAME_16");
            if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return;

            var royaltyBundles = Path.Combine(gameRoot, "Data", "Royalty", "AssetBundles");
            if (!Directory.Exists(royaltyBundles)) return;

            var royalty = GameContentVramSource.Estimate(Path.Combine(gameRoot, "Data", "Royalty"));
            var core = GameContentVramSource.Estimate(Path.Combine(gameRoot, "Data", "Core"));

            royalty.VramCompressed.Should().BeGreaterThan(0);
            royalty.TextureCount.Should().BeGreaterThan(0);
            core.VramCompressed.Should().BeGreaterThan(0);
            core.TextureCount.Should().BeGreaterThan(0);

            _output.WriteLine($"[REAL] Royalty: compressed={royalty.VramCompressed} uncompressed={royalty.VramUncompressed} textures={royalty.TextureCount} inAtlas={royalty.InAtlasCount}");
            _output.WriteLine($"[REAL] Core: compressed={core.VramCompressed} uncompressed={core.VramUncompressed} textures={core.TextureCount} inAtlas={core.InAtlasCount}");
            _output.WriteLine("--- Royalty log ---");
            _output.WriteLine(royalty.Logs);
            _output.WriteLine("--- Core log ---");
            _output.WriteLine(core.Logs);
        }

        /// <summary>
        /// Opt-in real-install test. Set <c>RIMSHARP_TEST_GAME_17</c> to a RimWorld 1.7 install
        /// root (Assets.zip at the root). Skipped when the variable is unset, empty, or points to
        /// a missing directory.
        /// </summary>
        [Fact]
        public void Estimate_Game17RealInstall_ShouldResolveRootAssetsZip()
        {
            var gameRoot = Environment.GetEnvironmentVariable("RIMSHARP_TEST_GAME_17");
            if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return;

            var core = GameContentVramSource.Estimate(Path.Combine(gameRoot, "Data", "Core"));

            core.VramCompressed.Should().BeGreaterThan(0);
            core.TextureCount.Should().BeGreaterThan(0);

            _output.WriteLine($"[REAL-GAME17] Core: compressed={core.VramCompressed} uncompressed={core.VramUncompressed} textures={core.TextureCount} inAtlas={core.InAtlasCount}");
            _output.WriteLine("--- Game17 Core log ---");
            _output.WriteLine(core.Logs);
        }

        /// <summary>
        /// Opt-in real-install test. Set <c>RIMSHARP_TEST_GAME_16</c> to a RimWorld 1.6 install
        /// root (Windows or Linux player-data layout). Skipped when the variable is unset, empty,
        /// or points to a missing directory.
        /// </summary>
        [Fact]
        public void Estimate_Game16RealInstall_ShouldResolvePlayerDataAssets()
        {
            var gameRoot = Environment.GetEnvironmentVariable("RIMSHARP_TEST_GAME_16");
            if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return;

            var core = GameContentVramSource.Estimate(Path.Combine(gameRoot, "Data", "Core"));

            core.VramCompressed.Should().BeGreaterThan(0);
            core.TextureCount.Should().BeGreaterThan(0);

            _output.WriteLine($"[REAL-GAME16] Core: compressed={core.VramCompressed} uncompressed={core.VramUncompressed} textures={core.TextureCount} inAtlas={core.InAtlasCount}");
            _output.WriteLine("--- Game16 Core log ---");
            _output.WriteLine(core.Logs);
        }

        private static void CreateAssetsZip(string zipPath, IEnumerable<(string Name, int Size)> entries)
        {
            using var fs = File.Create(zipPath);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
            foreach (var (name, size) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                entryStream.Write(new byte[size], 0, size);
            }
        }

        private static void CleanupDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                try { Directory.Delete(path, true); } catch { }
            }
        }
    }
}
