using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Logic;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Logic
{
    public class StringIsNullOrEmptyConverterTests
    {
        private readonly StringIsNullOrEmptyConverter _converter;

        public StringIsNullOrEmptyConverterTests()
        {
            _converter = new StringIsNullOrEmptyConverter();
        }

        [Theory]
        [InlineData("", true)]
        [InlineData(null, true)]
        [InlineData("text", false)]
        [InlineData(" ", false)] // Space is NOT empty
        public void Convert_ShouldReturnCorrectValue(string? value, bool expected)
        {
            // Act
            var result = _converter.Convert(value!, typeof(bool), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_WithNonString_ShouldReturnTrue()
        {
            // Act
            var result = _converter.Convert(123, typeof(bool), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(true);
        }
    }
}
