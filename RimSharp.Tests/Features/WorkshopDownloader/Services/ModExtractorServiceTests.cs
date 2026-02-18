using System;
using System.Threading.Tasks;
using FluentAssertions;
using RimSharp.Features.WorkshopDownloader.Services;
using Xunit;

namespace RimSharp.Tests.Features.WorkshopDownloader.Services
{
    public class ModExtractorServiceTests
    {
        // We use a mock-less constructor for the testable logic if possible, 
        // but since it requires WebView2 in ctor, we'll pass null and only test pure logic methods.
        private readonly ModExtractorService _service;

        public ModExtractorServiceTests()
        {
            _service = new ModExtractorService(null!);
        }

        [Theory]
        [InlineData("1.5 MB", 1.5 * 1024 * 1024)]
        [InlineData("10 KB", 10 * 1024)]
        [InlineData("2 GB", 2L * 1024 * 1024 * 1024)]
        [InlineData("1 Gigabytes", 1L * 1024 * 1024 * 1024)]
        [InlineData("500 Bytes", 500)]
        [InlineData("1,234 KB", 1234 * 1024)]
        [InlineData("", 0)]
        [InlineData(null, 0)]
        public async Task ParseFileSizeAsync_ShouldReturnCorrectBytes(string? input, double expectedBytes)
        {
            // Act
            var result = await _service.ParseFileSizeAsync(input!);

            // Assert
            result.Should().Be((long)expectedBytes);
        }

        [Theory]
        [InlineData("16 Jul @ 12:36am")] // Missing year
        [InlineData("1 Jan, 2024 @ 10:00pm")] // Full date
        [InlineData("15 Oct 2023")] // No time
        public async Task ConvertToStandardDate_ShouldParseValidDates(string input)
        {
            // Act
            var result = await _service.ConvertToStandardDate(input);

            // Assert
            result.Should().NotBeNullOrEmpty();
            // The result format is dd/MM/yyyy HH:mm:ss
            result.Should().MatchRegex(@"\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}");
        }

        [Fact]
        public async Task ConvertToStandardDate_WhenYearMissing_ShouldUseCurrentYear()
        {
            // Arrange
            var input = "10 May @ 5:00pm";
            var currentYear = DateTime.Now.Year.ToString();

            // Act
            var result = await _service.ConvertToStandardDate(input);

            // Assert
            result.Should().Contain(currentYear);
        }
    }
}
