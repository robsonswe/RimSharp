using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Numeric;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Numeric
{
    public class MultiplyValueConverterTests
    {
        private readonly MultiplyValueConverter _converter;

        public MultiplyValueConverterTests()
        {
            _converter = new MultiplyValueConverter();
        }

        [Theory]
        [InlineData(10.0, "2", 20.0)]
        [InlineData(5.5, "0.5", 2.75)]
        [InlineData(10.0, "invalid", 10.0)]
        public void Convert_ShouldMultiplyValue(double value, string parameter, double expected)
        {
            // Act
            var result = _converter.Convert(value, typeof(double), parameter, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_WithNonDouble_ShouldReturnOriginalValue()
        {
            // Act
            var result = _converter.Convert(123, typeof(double), "2", CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(123);
        }
    }
}
