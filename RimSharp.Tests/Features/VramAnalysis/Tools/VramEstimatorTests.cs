using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using RimSharp.Features.VramAnalysis.Tools;
using Xunit;

namespace RimSharp.Tests.Features.VramAnalysis.Tools
{
    public class VramEstimatorTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly string _texturesDirectory;

        public VramEstimatorTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "RimSharpTest_" + Guid.NewGuid());
            _texturesDirectory = Path.Combine(_tempDirectory, "Textures");
            Directory.CreateDirectory(_tempDirectory);
            Directory.CreateDirectory(_texturesDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                try { Directory.Delete(_tempDirectory, true); } catch { }
            }
        }

        [Fact]
        public void Calculate_ShouldReturnEmptyResult_WhenDirectoryDoesNotExist()
        {

            var nonExistentPath = Path.Combine(_tempDirectory, "NonExistentMod");

            var result = VramEstimator.Calculate(nonExistentPath, "1.5", new HashSet<string>());

            result.EstimatedVramUncompressed.Should().Be(0);
            result.TextureCount.Should().Be(0);
        }

        [Fact]
        public void Calculate_ShouldHandlePngFiles_Correctly()
        {

            CreatePngFile(Path.Combine(_texturesDirectory, "texture.png"), 1024, 1024);

            var result = VramEstimator.Calculate(_tempDirectory, "1.5", new HashSet<string>());

            long baseSize = 1024 * 1024 * 4;
            long expectedVram = (long)(baseSize * 1.33333);

            result.EstimatedVramUncompressed.Should().Be(expectedVram);
            result.TextureCount.Should().Be(1);
        }

        [Fact]
        public void Calculate_ShouldHandleDdsFiles_DXT1_Correctly()
        {

            CreateDdsFile(Path.Combine(_texturesDirectory, "texture.dds"), 1024, 1024, "DXT1");

            var result = VramEstimator.Calculate(_tempDirectory, "1.5", new HashSet<string>());

            long expectedSize = (long)(1024 * 1024 * 0.5 * 1.33333);
            result.EstimatedVramCompressed.Should().Be(expectedSize);
            result.EstimatedVramUncompressed.Should().Be(expectedSize);
            result.TextureCount.Should().Be(1);
        }

        [Theory]
        [InlineData(77, 1.0)]  // BC3_UNORM -> 1.0 bpp
        [InlineData(80, 0.5)]  // BC4_UNORM -> single-channel, 0.5 bpp
        [InlineData(83, 1.0)]  // BC5_UNORM -> two-channel, 1.0 bpp
        [InlineData(98, 1.0)]  // BC7_UNORM -> 1.0 bpp
        public void Calculate_ShouldHandleDdsFiles_Dx10Header_UsesRealDxgiFormat(uint dxgiFormat, double bpp)
        {

            CreateDx10DdsFile(Path.Combine(_texturesDirectory, "texture.dds"), 1024, 1024, dxgiFormat);

            var result = VramEstimator.Calculate(_tempDirectory, "1.5", new HashSet<string>());

            long expectedSize = (long)(1024 * 1024 * bpp * 1.33333);
            result.EstimatedVramCompressed.Should().Be(expectedSize);
            result.TextureCount.Should().Be(1);
        }

        [Fact]
        public void Calculate_ShouldHandleDdsFiles_Dx10Header_UncompressedR8G8B8A8()
        {
            // DXGI 28 = R8G8B8A8_UNORM: uncompressed source, so the compressed column
            // models hypothetical BC compression (1.0 bpp) while the uncompressed
            // column uses 4 bpp plus the mip factor (helper sets the mip flag).
            CreateDx10DdsFile(Path.Combine(_texturesDirectory, "texture.dds"), 1024, 1024, 28);

            var result = VramEstimator.Calculate(_tempDirectory, "1.5", new HashSet<string>());

            long baseSize = 1024L * 1024 * 4;
            result.EstimatedVramUncompressed.Should().Be((long)(baseSize * 1.33333));
            result.EstimatedVramCompressed.Should().Be((long)(1024L * 1024 * 1.33333));
            result.TextureCount.Should().Be(1);
        }

        [Fact]
        public void Calculate_ShouldHandlePsdFiles_Correctly()
        {

            CreatePsdFile(Path.Combine(_texturesDirectory, "texture.psd"), 1024, 1024);

            var result = VramEstimator.Calculate(_tempDirectory, "1.5", new HashSet<string>());

            long baseSize = 1024 * 1024 * 4;
            long expectedVram = (long)(baseSize * 1.33333);
            result.EstimatedVramUncompressed.Should().Be(expectedVram);
            result.TextureCount.Should().Be(1);
        }

        [Fact]
        public void Calculate_ShouldHandleJpgFiles_Correctly()
        {

            CreateJpgFile(Path.Combine(_texturesDirectory, "texture.jpg"), 1024, 1024);

            var result = VramEstimator.Calculate(_tempDirectory, "1.5", new HashSet<string>());

            long expectedSize = 1024 * 1024 * 3;
            long expectedVram = (long)(expectedSize * 1.33333);
            result.EstimatedVramUncompressed.Should().Be(expectedVram);
            result.TextureCount.Should().Be(1);
        }

        [Fact]
        public void Calculate_ShouldIgnoreAboutFolder()
        {

            var aboutDir = Path.Combine(_tempDirectory, "About");
            Directory.CreateDirectory(aboutDir);
            CreatePngFile(Path.Combine(aboutDir, "preview.png"), 512, 512);

            var result = VramEstimator.Calculate(_tempDirectory, "1.5", new HashSet<string>());

            result.TextureCount.Should().Be(0);
        }

        [Fact]
        public void Calculate_ShouldHandleLoadFolders_Exclusions()
        {

            var loadFoldersXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<loadFolders>
  <v1.5>
    <li IfModActive=""OtherMod"">1.5/Patches</li>
  </v1.5>
</loadFolders>";
            File.WriteAllText(Path.Combine(_tempDirectory, "loadFolders.xml"), loadFoldersXml);

            var patchDir = Path.Combine(_tempDirectory, "1.5", "Patches", "Textures");
            Directory.CreateDirectory(patchDir);
            CreatePngFile(Path.Combine(patchDir, "patch.png"), 256, 256);

            var result = VramEstimator.Calculate(_tempDirectory, "1.5", new HashSet<string>());

            result.TextureCount.Should().Be(0);
            result.ConditionalDependencies.Should().Contain(d => d.PackageId == "othermod" && !d.IsActive);
        }

        [Fact]
        public void Calculate_ShouldIdentifyAtlasedTextures()
        {

            CreatePngFile(Path.Combine(_texturesDirectory, "small.png"), 256, 256);

            var result = VramEstimator.Calculate(_tempDirectory, "1.5", new HashSet<string>());

            result.InAtlasCount.Should().Be(1);
            result.TextureCount.Should().Be(1);
        }

        [Fact]
        public void Calculate_ShouldMatchLowercaseTexturesFolder_DuringBruteForceScan()
        {
            // Strict resolution finds no 'Textures' child, so the brute-force scan runs.
            // A nested lowercase 'textures' folder must still be matched on case-sensitive
            // file systems.
            var modDirectory = Path.Combine(_tempDirectory, "LowercaseMod");
            var nestedTextures = Path.Combine(modDirectory, "Sub", "textures");
            Directory.CreateDirectory(nestedTextures);
            CreatePngFile(Path.Combine(nestedTextures, "icon.png"), 256, 256);

            var result = VramEstimator.Calculate(modDirectory, "1.5", new HashSet<string>());

            result.TextureCount.Should().Be(1);
        }

        private void CreatePngFile(string path, int width, int height)
        {
            using var fs = File.Create(path);
            using var writer = new BinaryWriter(fs);
            writer.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            writer.Write(ReverseBytes(13));
            writer.Write(Encoding.ASCII.GetBytes("IHDR"));
            writer.Write(ReverseBytes(width));
            writer.Write(ReverseBytes(height));
            writer.Write(new byte[] { 8, 6, 0, 0, 0 });
            writer.Write(new byte[] { 0, 0, 0, 0 });
            writer.Write(ReverseBytes(0));
            writer.Write(Encoding.ASCII.GetBytes("IDAT"));
            writer.Write(new byte[] { 0, 0, 0, 0 });
            writer.Write(ReverseBytes(0));
            writer.Write(Encoding.ASCII.GetBytes("IEND"));
            writer.Write(new byte[] { 0, 0, 0, 0 });
        }

        private void CreateDdsFile(string path, int width, int height, string fourCC)
        {
            using var fs = File.Create(path);
            using var writer = new BinaryWriter(fs);
            writer.Write(Encoding.ASCII.GetBytes("DDS "));
            writer.Write((uint)124);
            writer.Write((uint)0xA1007);
            writer.Write((uint)height);
            writer.Write((uint)width);
            writer.Write((uint)(width * height));
            writer.Write((uint)0);
            writer.Write((uint)1);
            writer.Write(new byte[44]);
            writer.Write((uint)32);
            writer.Write((uint)0x4);
            writer.Write(Encoding.ASCII.GetBytes(fourCC.PadRight(4, ' ')));
            writer.Write(new byte[20]);
            writer.Write((uint)0x1000);
            writer.Write(new byte[16]);
        }

        private void CreateDx10DdsFile(string path, int width, int height, uint dxgiFormat)
        {
            using var fs = File.Create(path);
            using var writer = new BinaryWriter(fs);
            writer.Write(Encoding.ASCII.GetBytes("DDS "));
            writer.Write((uint)124);
            writer.Write((uint)0xA1007);
            writer.Write((uint)height);
            writer.Write((uint)width);
            writer.Write((uint)(width * height));
            writer.Write((uint)0);
            writer.Write((uint)1);
            writer.Write(new byte[44]);
            writer.Write((uint)32);
            writer.Write((uint)0x4);
            writer.Write(Encoding.ASCII.GetBytes("DX10"));
            writer.Write(new byte[20]);
            writer.Write((uint)0x1000);
            writer.Write(new byte[16]);
            // DDS_HEADER_DXT10 extension: dxgiFormat, resourceDimension, miscFlag, arraySize, miscFlags2
            writer.Write(dxgiFormat);
            writer.Write((uint)2); // DDS_DIMENSION_TEXTURE2D
            writer.Write((uint)0);
            writer.Write((uint)1);
            writer.Write((uint)0);
        }

        private void CreatePsdFile(string path, int width, int height)
        {
            using var fs = File.Create(path);
            using var writer = new BinaryWriter(fs);
            writer.Write(Encoding.ASCII.GetBytes("8BPS"));
            writer.Write((short)ReverseBytesShort(1));
            writer.Write(new byte[6]);
            writer.Write((short)ReverseBytesShort(4));
            writer.Write(ReverseBytes(height));
            writer.Write(ReverseBytes(width));
            writer.Write((short)ReverseBytesShort(8));
            writer.Write((short)ReverseBytesShort(3));
        }

        private void CreateJpgFile(string path, int width, int height)
        {
            using var fs = File.Create(path);
            using var writer = new BinaryWriter(fs);
            writer.Write(new byte[] { 0xFF, 0xD8 });
            writer.Write(new byte[] { 0xFF, 0xC0 });
            writer.Write((short)ReverseBytesShort(17));
            writer.Write((byte)8);
            writer.Write((short)ReverseBytesShort((short)height));
            writer.Write((short)ReverseBytesShort((short)width));
            writer.Write((byte)3);
            writer.Write(new byte[] { 1, 0x11, 0, 2, 0x11, 1, 3, 0x11, 1 });
            writer.Write(new byte[] { 0xFF, 0xD9 });
        }

        private static short ReverseBytesShort(short value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return BitConverter.ToInt16(bytes, 0);
        }

        private static int ReverseBytes(int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }
    }
}

