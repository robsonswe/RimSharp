using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Logic;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Logic
{
    public class InvertedBooleanConverterTests
    {
        private readonly InvertedBooleanConverter _converter;

        public InvertedBooleanConverterTests()
        {
            _converter = new InvertedBooleanConverter();
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void Convert_ShouldInvertBoolean(bool value, bool expected)
        {
            // Act
            var result = _converter.Convert(value, typeof(bool), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void ConvertBack_ShouldInvertBoolean(bool value, bool expected)
        {
            // Act
            var result = _converter.ConvertBack(value, typeof(bool), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_WithNonBoolean_ShouldReturnFalse()
        {
            // Act
            var result = _converter.Convert("not a bool", typeof(bool), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(false);
        }
    }
}
