using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Logic;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Logic
{
    public class EqualityConverterTests
    {
        private readonly EqualityConverter _converter;

        public EqualityConverterTests()
        {
            _converter = new EqualityConverter();
        }

        [Theory]
        [InlineData("test", "test", true)]
        [InlineData("test", "other", false)]
        [InlineData(123, "123", true)]
        [InlineData(null, null, true)]
        [InlineData("test", null, false)]
        [InlineData(null, "test", false)]
        public void Convert_ShouldReturnCorrectEquality(object? value, object? parameter, bool expected)
        {

            var result = _converter.Convert(value, typeof(bool), parameter, CultureInfo.InvariantCulture);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(true, "param", "param")]
        [InlineData(false, "param", null)]
        [InlineData(null, "param", null)]
        public void ConvertBack_ShouldReturnParameterIfTrue(object? value, object? parameter, object? expected)
        {

            var result = _converter.ConvertBack(value, typeof(object), parameter, CultureInfo.InvariantCulture);

            result.Should().Be(expected);
        }
    }
}

