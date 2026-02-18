using System;
using System.Collections.Generic;
using System.Globalization;
using WinVisibility = System.Windows.Visibility;
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
        [InlineData(1, WinVisibility.Visible)]
        [InlineData(0, WinVisibility.Collapsed)]
        [InlineData(-1, WinVisibility.Collapsed)]
        [InlineData(5L, WinVisibility.Visible)]
        [InlineData(null, WinVisibility.Collapsed)]
        [InlineData("not a number", WinVisibility.Collapsed)]
        public void Convert_WithNumericValues_ShouldReturnCorrectVisibility(object value, WinVisibility expected)
        {
            // Act
            var result = _converter.Convert(value, typeof(WinVisibility), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_WithCollection_ShouldReturnCorrectVisibility()
        {
            // Arrange
            var emptyList = new List<string>();
            var nonEmptyList = new List<string> { "item" };

            // Act
            var resultEmpty = _converter.Convert(emptyList, typeof(WinVisibility), null, CultureInfo.InvariantCulture);
            var resultNonEmpty = _converter.Convert(nonEmptyList, typeof(WinVisibility), null, CultureInfo.InvariantCulture);

            // Assert
            resultEmpty.Should().Be(WinVisibility.Collapsed);
            resultNonEmpty.Should().Be(WinVisibility.Visible);
        }
    }
}
