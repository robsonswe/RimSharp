using System;
using System.Threading.Tasks;
using FluentAssertions;
using RimSharp.Features.WorkshopDownloader.Services;
using Xunit;

namespace RimSharp.Tests.Features.WorkshopDownloader.Services
{
    public class ModExtractorServiceTests
    {

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

            var result = await _service.ParseFileSizeAsync(input!);

            result.Should().Be((long)expectedBytes);
        }

        [Theory]
        [InlineData("16 Jul @ 12:36am")] // Missing year
        [InlineData("1 Jan, 2024 @ 10:00pm")] // Full date
        [InlineData("15 Oct 2023")] // No time
        public async Task ConvertToStandardDate_ShouldParseValidDates(string input)
        {

            var result = await _service.ConvertToStandardDate(input);

            result.Should().NotBeNullOrEmpty();
            
            result.Should().MatchRegex(@"\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}");
        }

        [Fact]
        public async Task ConvertToStandardDate_WhenYearMissing_ShouldUseCurrentYear()
        {

            var input = "10 May @ 5:00pm";
            var currentYear = DateTime.Now.Year.ToString();

            var result = await _service.ConvertToStandardDate(input);

            result.Should().Contain(currentYear);
        }
    }
}


