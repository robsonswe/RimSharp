using System.Runtime.InteropServices;
using FluentAssertions;
using RimSharp.Core.Services;
using Xunit;

namespace RimSharp.Tests.Core.Services
{
    public class SystemInfoServiceTests
    {
        private const ulong Mebibyte = 1024UL * 1024UL;

        [Theory]
        [InlineData("24576\n", 24576UL * Mebibyte)]
        [InlineData("24576, 24576\n", 24576UL * Mebibyte)]
        [InlineData("8192", 8192UL * Mebibyte)]
        [InlineData("  16384  \n", 16384UL * Mebibyte)]
        [InlineData("24576 MiB\n", 24576UL * Mebibyte)]
        public void ParseNvidiaSmiOutput_WithValidOutput_ReturnsBytes(string output, ulong expected)
        {
            LinuxSystemInfoService.ParseNvidiaSmiOutput(output).Should().Be(expected);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\n\n")]
        [InlineData("garbage")]
        [InlineData("N/A\n")]
        public void ParseNvidiaSmiOutput_WithInvalidOutput_ReturnsZero(string output)
        {
            LinuxSystemInfoService.ParseNvidiaSmiOutput(output).Should().Be(0UL);
        }

        [Fact]
        public void ParseNvidiaSmiOutput_WithMultipleValuesOnFirstLine_UsesFirstValue()
        {
            LinuxSystemInfoService.ParseNvidiaSmiOutput("24576, 8192\n")
                .Should().Be(24576UL * Mebibyte);
        }

        [Fact]
        public void ParseNvidiaSmiOutput_WithNull_ReturnsZero()
        {
            LinuxSystemInfoService.ParseNvidiaSmiOutput(null).Should().Be(0UL);
        }

        [Theory]
        [InlineData("16106127360\n", 16106127360UL)]
        [InlineData("17179869184", 17179869184UL)]
        [InlineData("  8589934592  \n", 8589934592UL)]
        public void ParseSysFsVram_WithValidContent_ReturnsBytes(string content, ulong expected)
        {
            LinuxSystemInfoService.ParseSysFsVram(content).Should().Be(expected);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\n")]
        [InlineData("garbage")]
        [InlineData("-1")]
        public void ParseSysFsVram_WithInvalidContent_ReturnsZero(string content)
        {
            LinuxSystemInfoService.ParseSysFsVram(content).Should().Be(0UL);
        }

        [Fact]
        public void ParseSysFsVram_WithNull_ReturnsZero()
        {
            LinuxSystemInfoService.ParseSysFsVram(null).Should().Be(0UL);
        }

        [Fact]
        public void ParseSysFsResource_WithVmwareSvgaResource_ReturnsLargestMemoryBar()
        {
            // Real content of a VMware SVGA II adapter: 16-byte I/O region,
            // 256 MiB prefetchable 64-bit memory BAR, 2 MiB MMIO region.
            const string vmwareResource =
                "0x000000000000d000 0x000000000000d00f 0x0000000000040101\n" +
                "0x00000000e0000000 0x00000000efffffff 0x0000000000042208\n" +
                "0x00000000f0000000 0x00000000f01fffff 0x0000000000040200\n" +
                "0x0000000000000000 0x0000000000000000 0x0000000000000000\n" +
                "0x00000000000c0000 0x00000000000dffff 0x0000000000000212\n";

            LinuxSystemInfoService.ParseSysFsResource(vmwareResource)
                .Should().Be(256UL * Mebibyte);
        }

        [Fact]
        public void ParseSysFsResource_WithOnlyIoRegions_ReturnsZero()
        {
            const string content =
                "0x000000000000d000 0x000000000000d00f 0x0000000000040101\n" +
                "0x0000000000000000 0x0000000000000000 0x0000000000000000\n";

            LinuxSystemInfoService.ParseSysFsResource(content).Should().Be(0UL);
        }

        [Fact]
        public void ParseSysFsResource_WithEmptyRegions_ReturnsZero()
        {
            const string content =
                "0x0000000000000000 0x0000000000000000 0x0000000000000000\n" +
                "0x0000000000000000 0x0000000000000000 0x0000000000000000\n";

            LinuxSystemInfoService.ParseSysFsResource(content).Should().Be(0UL);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("garbage")]
        public void ParseSysFsResource_WithInvalidContent_ReturnsZero(string content)
        {
            LinuxSystemInfoService.ParseSysFsResource(content).Should().Be(0UL);
        }

        [Fact]
        public void ParseSysFsResource_WithNull_ReturnsZero()
        {
            LinuxSystemInfoService.ParseSysFsResource(null).Should().Be(0UL);
        }

        [Fact]
        public void Factory_OnWindows_ReturnsWindowsSystemInfoService()
        {
            ISystemInfoService service = SystemInfoServiceFactory.Create();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#if WINDOWS
                service.Should().BeOfType<WindowsSystemInfoService>();
#else
                service.Should().BeOfType<NullSystemInfoService>();
#endif
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                service.Should().BeOfType<LinuxSystemInfoService>();
            }
            else
            {
                service.Should().BeOfType<NullSystemInfoService>();
            }
        }
    }
}
