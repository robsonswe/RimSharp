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

        public VramEstimatorTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "RimSharpTest_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDirectory);
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
            // Arrange
            var nonExistentPath = Path.Combine(_tempDirectory, "NonExistentMod");

            // Act
            var result = VramEstimator.Calculate(nonExistentPath, "1.5", new HashSet<string>());

            // Assert
            result.EstimatedVramUncompressed.Should().Be(0);
            result.TextureCount.Should().Be(0);
        }

        [Fact]
        public void Calculate_ShouldHandlePngFiles_Correctly()
        {
            // Arrange
            CreatePngFile(Path.Combine(_tempDirectory, "texture.png"), 1024, 1024);

            // Act
            var result = VramEstimator.Calculate(_tempDirectory, "1.5", new HashSet<string>());

            // Assert
            // 1024 * 1024 * 4 bytes (RGBA32) = 4,194,304 bytes
            // Since it's a power of 2, it assumes mipmaps if checks pass (but check implementation: mipmap logic is basic)
            // Logic: width/height > 0 -> RGBA32 -> 4 bytes per pixel.
            // Check mipmap logic: (hasMips && (RGBA32 || RGB24)) ? size * 1.33333 : size
            // For PNG, GetPngInfo returns hasMips = (width % 4 == 0) && (height % 4 == 0). 1024 % 4 == 0 is true.
            // So expected size = 4,194,304 * 1.33333 = 5,592,405 (approx)
            
            // Wait, strict calculation:
            long baseSize = 1024 * 1024 * 4;
            long expectedVram = (long)(baseSize * 1.33333);

            result.EstimatedVramUncompressed.Should().Be(expectedVram);
            result.TextureCount.Should().Be(1);
        }

        [Fact]
        public void Calculate_ShouldHandleDdsFiles_DXT1_Correctly()
        {
            // Arrange
            CreateDdsFile(Path.Combine(_tempDirectory, "texture.dds"), 1024, 1024, "DXT1");

            // Act
            var result = VramEstimator.Calculate(_tempDirectory, "1.5", new HashSet<string>());

            // Assert
            // DXT1 = 0.5 bytes per pixel (4 bits)
            // 1024 * 1024 * 0.5 = 524,288 bytes compressed size.
            // Uncompressed size for DXT1 is 0 in the switch statement for isCompressed=false?
            // Let's check the code: GetBytesPerPixel(DXT1, false) -> 0?
            // Code: return format switch { SimpleTextureFormat.RGBA32 => 4.0, SimpleTextureFormat.RGB24 => 3.0, SimpleTextureFormat.DXT1 => 0.5, ... }
            // Wait, for uncompressed it returns 0.5? No, DXT1 is compressed format.
            // The code uses `GetBytesPerPixel(format, isCompressed: false)` for `baseSizeUncompressed`.
            // If format is DXT1, `GetBytesPerPixel(DXT1, false)` returns 0.5.
            // Wait, DXT1 is a compressed format, so uncompressed VRAM size should technically be the decompressed size (RGBA32) if loaded as such,
            // OR if loaded as compressed texture in VRAM, it stays compressed.
            // The code calculates `vramUncompressed` using `GetBytesPerPixel(..., false)`.
            // For DXT1, it returns 0.5. So uncompressed estimate = 1024*1024*0.5 = 524,288.
            // For compressed estimate, `GetBytesPerPixel(..., true)` -> DXT1 returns 0.5.
            // So for DXT1, uncompressed and compressed estimates are equal in this logic.
            
            // Let's check `GetBytesPerPixel` implementation in the source provided:
            // private static double GetBytesPerPixel(SimpleTextureFormat format, bool isCompressed) { 
            //    if (isCompressed) return format switch { SimpleTextureFormat.DXT1 => 0.5, _ => 1.0 }; 
            //    return format switch { SimpleTextureFormat.RGBA32 => 4.0, SimpleTextureFormat.RGB24 => 3.0, SimpleTextureFormat.DXT1 => 0.5, SimpleTextureFormat.DXT5 => 1.0, SimpleTextureFormat.BC7 => 1.0, _ => 0 }; 
            // }
            
            long expectedSize = (long)(1024 * 1024 * 0.5);
            result.EstimatedVramCompressed.Should().Be(expectedSize);
            result.EstimatedVramUncompressed.Should().Be(expectedSize); 
            result.TextureCount.Should().Be(1);
        }

        [Fact]
        public void Calculate_ShouldIgnoreAboutFolder()
        {
            // Arrange
            var aboutDir = Path.Combine(_tempDirectory, "About");
            Directory.CreateDirectory(aboutDir);
            CreatePngFile(Path.Combine(aboutDir, "preview.png"), 512, 512);

            // Act
            var result = VramEstimator.Calculate(_tempDirectory, "1.5", new HashSet<string>());

            // Assert
            result.TextureCount.Should().Be(0);
        }

        [Fact]
        public void Calculate_ShouldHandleLoadFolders_Exclusions()
        {
            // Arrange
            // Create loadFolders.xml
            var loadFoldersXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<loadFolders>
  <v1.5>
    <li IfModActive=""OtherMod"">1.5/Patches</li>
  </v1.5>
</loadFolders>";
            File.WriteAllText(Path.Combine(_tempDirectory, "loadFolders.xml"), loadFoldersXml);

            // Create structure
            var patchDir = Path.Combine(_tempDirectory, "1.5", "Patches");
            Directory.CreateDirectory(patchDir);
            CreatePngFile(Path.Combine(patchDir, "patch.png"), 256, 256);

            // Act - Active mod not present, so exclusion should apply (wait, logic check)
            // Logic: if (!isActive) -> add to excludedFolder.
            // Here "OtherMod" is NOT in active list. So it should be excluded.
            var result = VramEstimator.Calculate(_tempDirectory, "1.5", new HashSet<string>());

            // Assert
            result.TextureCount.Should().Be(0);
            result.ConditionalDependencies.Should().Contain(d => d.PackageId == "OtherMod" && !d.IsActive);
        }

        [Fact]
        public void Calculate_ShouldIdentifyAtlasedTextures()
        {
            // Arrange
            // Texture smaller than 512x512
            CreatePngFile(Path.Combine(_tempDirectory, "small.png"), 256, 256);

            // Act
            var result = VramEstimator.Calculate(_tempDirectory, "1.5", new HashSet<string>());

            // Assert
            result.InAtlasCount.Should().Be(1);
            result.TextureCount.Should().Be(1);
        }

        private void CreatePngFile(string path, int width, int height)
        {
            using var fs = File.Create(path);
            using var writer = new BinaryWriter(fs);

            // PNG Signature
            writer.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            
            // IHDR Chunk
            // Length: 13 bytes
            writer.Write(ReverseBytes(13)); // Big Endian
            // Type: IHDR
            writer.Write(Encoding.ASCII.GetBytes("IHDR"));
            // Width
            writer.Write(ReverseBytes(width));
            // Height
            writer.Write(ReverseBytes(height));
            // Bit depth (8), Color Type (6 - RGBA), Compression (0), Filter (0), Interlace (0)
            writer.Write(new byte[] { 8, 6, 0, 0, 0 });
            // CRC (fake)
            writer.Write(new byte[] { 0, 0, 0, 0 });

            // IDAT (minimal fake)
            writer.Write(ReverseBytes(0)); // Length 0
            writer.Write(Encoding.ASCII.GetBytes("IDAT"));
            writer.Write(new byte[] { 0, 0, 0, 0 }); // CRC
            
            // IEND
            writer.Write(ReverseBytes(0));
            writer.Write(Encoding.ASCII.GetBytes("IEND"));
            writer.Write(new byte[] { 0, 0, 0, 0 });
        }

        private void CreateDdsFile(string path, int width, int height, string fourCC)
        {
            using var fs = File.Create(path);
            using var writer = new BinaryWriter(fs);

            // Magic
            writer.Write(Encoding.ASCII.GetBytes("DDS "));
            // Size (124)
            writer.Write((uint)124);
            // Flags (0x1 | 0x2 | 0x4 | 0x1000 | 0x20000 | 0x80000) - Caps, Height, Width, PixelFormat, MipMapCount, LinearSize
            writer.Write((uint)0xA1007); 
            // Height
            writer.Write((uint)height);
            // Width
            writer.Write((uint)width);
            // Linear Size
            writer.Write((uint)(width * height)); 
            // Depth
            writer.Write((uint)0);
            // MipMapCount
            writer.Write((uint)1);
            // Reserved1
            writer.Write(new byte[44]);
            
            // PixelFormat (32 bytes)
            writer.Write((uint)32); // Size
            writer.Write((uint)0x4); // Flags (DDPF_FOURCC)
            
            // FourCC
            var fourCCBytes = Encoding.ASCII.GetBytes(fourCC.PadRight(4, ' '));
            writer.Write(fourCCBytes);
            
            // RGB bit counts and masks (0 for FourCC)
            writer.Write(new byte[20]); // 5 * uint
            
            // Caps
            writer.Write((uint)0x1000); // DDSCAPS_TEXTURE
            // Caps2, 3, 4, Reserved2
            writer.Write(new byte[16]);
        }

        private static int ReverseBytes(int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }
    }
}
