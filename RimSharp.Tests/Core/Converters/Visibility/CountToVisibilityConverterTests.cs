using System;
using System.Collections.Generic;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.ViewVisibility;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Visibility
{
    public class CountToVisibilityConverterTests
    {
        private readonly CountToVisibilityConverter _converter;

        public CountToVisibilityConverterTests()
        {
            _converter = new CountToVisibilityConverter();
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(0, false)]
        [InlineData(-1, false)]
        [InlineData(5L, true)]
        [InlineData(null, false)]
        [InlineData("not a number", false)]
        public void Convert_WithNumericValues_ShouldReturnCorrectVisibility(object? value, bool expected)
        {

            var result = _converter.Convert(value!, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_WithCollection_ShouldReturnCorrectVisibility()
        {

            var emptyList = new List<string>();
            var nonEmptyList = new List<string> { "item" };

            var resultEmpty = _converter.Convert(emptyList, typeof(bool), null, CultureInfo.InvariantCulture);
            var resultNonEmpty = _converter.Convert(nonEmptyList, typeof(bool), null, CultureInfo.InvariantCulture);

            resultEmpty.Should().Be(false);
            resultNonEmpty.Should().Be(true);
        }
    }
}

