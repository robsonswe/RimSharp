using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Numeric;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Numeric
{
    public class ClampValueConverterTests
    {
        private readonly ClampValueConverter _converter;

        public ClampValueConverterTests()
        {
            _converter = new ClampValueConverter();
        }

        [Theory]
        [InlineData(0.0, "10,100", 10.0)]
        [InlineData(500.0, "10,100", 55.0)] // 10 + (100-10) * (500/1000) = 10 + 90*0.5 = 55
        [InlineData(1000.0, "10,100", 100.0)]
        [InlineData(2000.0, "10,100", 100.0)] // Clamped to max
        [InlineData(-100.0, "10,100", 10.0)] // Clamped to min
        public void Convert_ShouldScaleAndClampCorrectly(double value, string parameter, double expected)
        {
            // Act
            var result = _converter.Convert(value, typeof(double), parameter, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_WithInvalidParameter_ShouldReturnOriginalValue()
        {
            // Act
            var result = _converter.Convert(123.45, typeof(double), "invalid", CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(123.45);
        }
    }
}
