using System.Collections.Generic;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Logic;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Logic
{
    public class BooleanAndToVisibilityConverterTests
    {
        private readonly BooleanAndToVisibilityConverter _converter;

        public BooleanAndToVisibilityConverterTests()
        {
            _converter = new BooleanAndToVisibilityConverter();
        }

        [Theory]
        [InlineData(new object[] { true, true }, true)]
        [InlineData(new object[] { true, false }, false)]
        [InlineData(new object[] { false, true }, false)]
        [InlineData(new object[] { false, false }, false)]
        [InlineData(new object[] { true, true, true }, true)]
        [InlineData(new object[] { true, true, false }, false)]
        public void Convert_ShouldReturnTrueOnlyWhenAllValuesAreTrue(object[] values, bool expected)
        {
            var result = _converter.Convert(values, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_WithEmptyList_ShouldReturnTrue()
        {
            var result = _converter.Convert(new List<object?>(), typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(true);
        }

        [Fact]
        public void Convert_WithNonBooleanValue_ShouldReturnFalse()
        {
            var values = new object[] { true, "not a bool" };

            var result = _converter.Convert(values, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(false);
        }

        [Fact]
        public void Convert_WithNullValue_ShouldReturnFalse()
        {
            var values = new object?[] { true, null };

            var result = _converter.Convert(values, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(false);
        }
    }
}
