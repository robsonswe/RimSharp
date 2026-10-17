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
        [InlineData(0L, "0 Bytes")]
        [InlineData(500L, "500 Bytes")]
        [InlineData(1024L, "1.0 KB")]
        [InlineData(1024L * 1024L, "1.0 MB")]
        [InlineData(1024L * 1024L * 1024L, "1.00 GB")]
        [InlineData(-1L, "N/A")]
        public void Convert_ShouldReturnCorrectString(object? value, string expected)
        {

            var result = _converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_ShouldReturnNotCalculated_ForNull()
        {
            var result = _converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be("Not Calculated");
        }

        [Fact]
        public void Convert_ShouldReturnNotCalculated_ForNullableNull()
        {
            long? value = null;

            var result = _converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be("Not Calculated");
        }

        [Fact]
        public void Convert_ShouldReturnNa_ForNonNumeric()
        {
            var result = _converter.Convert("not a long", typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be("N/A");
        }

        [Theory]
        [InlineData(1536L, "1.5 KB")]
        [InlineData(1024L * 1024L * 1.5, "1.5 MB")]
        [InlineData(1024L * 1024L * 1024L * 1.5, "1.50 GB")]
        public void Convert_WithFractionalValues_ShouldReturnCorrectString(double value, string expected)
        {

            var result = _converter.Convert((long)value, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be(expected);
        }
    }
}
