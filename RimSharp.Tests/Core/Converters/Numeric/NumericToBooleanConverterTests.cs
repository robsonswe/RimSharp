using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Numeric;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Numeric
{
    public class NumericToBooleanConverterTests
    {
        private readonly NumericToBooleanConverter _converter;

        public NumericToBooleanConverterTests()
        {
            _converter = new NumericToBooleanConverter();
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(0, false)]
        [InlineData(-1, false)]
        public void Convert_WithInt_ShouldReturnCorrectValue(int value, bool expected)
        {
            // Act
            var result = _converter.Convert(value, typeof(bool), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(1L, true)]
        [InlineData(0L, false)]
        [InlineData(-1L, false)]
        public void Convert_WithLong_ShouldReturnCorrectValue(long value, bool expected)
        {
            // Act
            var result = _converter.Convert(value, typeof(bool), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(0.1, true)]
        [InlineData(0.0, false)]
        [InlineData(-0.1, false)]
        public void Convert_WithDouble_ShouldReturnCorrectValue(double value, bool expected)
        {
            // Act
            var result = _converter.Convert(value, typeof(bool), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(0.1f, true)]
        [InlineData(0.0f, false)]
        [InlineData(-0.1f, false)]
        public void Convert_WithFloat_ShouldReturnCorrectValue(float value, bool expected)
        {
            // Act
            var result = _converter.Convert(value, typeof(bool), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_WithNonNumeric_ShouldReturnFalse()
        {
            // Act
            var result = _converter.Convert("not a number", typeof(bool), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(false);
        }
    }
}
