using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Utility;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Utility
{
    public class FileSizeConverterTests
    {
        private readonly FileSizeConverter _converter;

        public FileSizeConverterTests()
        {
            _converter = new FileSizeConverter();
        }

        [Theory]
        [InlineData(0L, "Not Calculated")]
        [InlineData(500L, "500 Bytes")]
        [InlineData(1024L, "1.0 KB")]
        [InlineData(1024L * 1024L, "1.0 MB")]
        [InlineData(1024L * 1024L * 1024L, "1.00 GB")]
        [InlineData(-1L, "N/A")]
        [InlineData(null, "N/A")]
        [InlineData("not a long", "N/A")]
        public void Convert_ShouldReturnCorrectString(object? value, string expected)
        {
            // Act
            var result = _converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(1536L, "1.5 KB")]
        [InlineData(1024L * 1024L * 1.5, "1.5 MB")]
        [InlineData(1024L * 1024L * 1024L * 1.5, "1.50 GB")]
        public void Convert_WithFractionalValues_ShouldReturnCorrectString(double value, string expected)
        {
             // Act
            var result = _converter.Convert((long)value, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }
    }
}
