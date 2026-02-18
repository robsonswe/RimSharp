using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Utility;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Utility
{
    public class FilterButtonTagConverterTests
    {
        private readonly FilterButtonTagConverter _converter;

        public FilterButtonTagConverterTests()
        {
            _converter = new FilterButtonTagConverter();
        }

        [Theory]
        [InlineData("Active", true, false, "ActiveFiltered")]
        [InlineData("Active", false, false, "Unfiltered")]
        [InlineData("Inactive", false, true, "InactiveFiltered")]
        [InlineData("Inactive", false, false, "Unfiltered")]
        [InlineData("Other", true, true, "Unfiltered")]
        [InlineData(null, true, true, "Unfiltered")]
        public void Convert_ShouldReturnCorrectTag(string? header, bool active, bool inactive, string expected)
        {
            // Act
            var result = _converter.Convert(new object[] { header!, active, inactive }, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_WithInsufficientValues_ShouldReturnUnfiltered()
        {
            // Act
            var result = _converter.Convert(new object[] { "Active" }, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be("Unfiltered");
        }
    }
}
