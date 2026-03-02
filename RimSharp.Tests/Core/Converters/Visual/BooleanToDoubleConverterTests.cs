using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Visual;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Visual
{
    public class BooleanToDoubleConverterTests
    {
        private readonly BooleanToDoubleConverter _converter;

        public BooleanToDoubleConverterTests()
        {
            _converter = new BooleanToDoubleConverter();
        }

        [Theory]
        [InlineData(true, "3|0", 3.0)]
        [InlineData(false, "3|0", 0.0)]
        [InlineData(true, "1.5|2.5", 1.5)]
        [InlineData(false, "1.5|2.5", 2.5)]
        [InlineData(true, "invalid", 0.0)]
        [InlineData(null, "3|0", 0.0)]
        public void Convert_ShouldReturnCorrectDouble(object? value, string parameter, double expected)
        {

            var result = _converter.Convert(value, typeof(double), parameter, CultureInfo.InvariantCulture);

            result.Should().Be(expected);
        }
    }
}

