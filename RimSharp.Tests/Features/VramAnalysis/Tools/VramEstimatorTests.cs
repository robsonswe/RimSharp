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

            long expectedSize = (long)(1024 * 1024 * 0.5);
            result.EstimatedVramCompressed.Should().Be(expectedSize);
            result.EstimatedVramUncompressed.Should().Be(expectedSize); 
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

