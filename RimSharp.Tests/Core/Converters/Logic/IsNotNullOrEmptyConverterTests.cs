using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Logic;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Logic
{
    public class IsNotNullOrEmptyConverterTests
    {
        private readonly IsNotNullOrEmptyConverter _converter;

        public IsNotNullOrEmptyConverterTests()
        {
            _converter = new IsNotNullOrEmptyConverter();
        }

        [Theory]
        [InlineData("hello", false, true)]
        [InlineData("", false, false)]
        [InlineData("   ", false, false)]
        [InlineData(null, false, false)]
        public void Convert_WithDefaultInverse_ShouldReturnTrueWhenNonEmpty(string? value, bool inverse, bool expected)
        {
            _converter.Inverse = inverse;

            var result = _converter.Convert(value!, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("hello", true, false)]
        [InlineData("", true, true)]
        [InlineData("   ", true, true)]
        [InlineData(null, true, true)]
        public void Convert_WithInverseTrue_ShouldInvertResult(string? value, bool inverse, bool expected)
        {
            _converter.Inverse = inverse;

            var result = _converter.Convert(value!, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_WithNonStringValue_ShouldReturnTrueWhenNotNull()
        {
            var result = _converter.Convert(42, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(true);
        }

        [Fact]
        public void ConvertBack_ShouldReturnNull()
        {
            var result = _converter.ConvertBack(true, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().BeNull();
        }
    }
}
