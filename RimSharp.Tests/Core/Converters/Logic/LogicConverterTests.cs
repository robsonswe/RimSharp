using System;
using System.Collections.Generic;
using System.Globalization;
using WinVisibility = System.Windows.Visibility;
using FluentAssertions;
using RimSharp.Core.Converters.Logic;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Logic
{
    public class LogicConverterTests
    {
        [Fact]
        public void BooleanAndToVisibilityConverter_ShouldReturnVisible_WhenAllTrue()
        {
            var converter = new BooleanAndToVisibilityConverter();
            var values = new object[] { true, true, true };
            var result = converter.Convert(values, typeof(WinVisibility), null, CultureInfo.InvariantCulture);
            result.Should().Be(WinVisibility.Visible);
        }

        [Fact]
        public void BooleanAndToVisibilityConverter_ShouldReturnCollapsed_WhenAnyFalse()
        {
            var converter = new BooleanAndToVisibilityConverter();
            var values = new object[] { true, false, true };
            var result = converter.Convert(values, typeof(WinVisibility), null, CultureInfo.InvariantCulture);
            result.Should().Be(WinVisibility.Collapsed);
        }

        [Theory]
        [InlineData(5, null, true)]
        [InlineData(0, null, false)]
        [InlineData(null, null, false)]
        [InlineData(0, 0, false)]
        [InlineData(1, 0, true)]
        public void CountToBooleanConverter_ShouldReturnCorrectValue(object? value, object? parameter, bool expected)
        {
            var converter = new CountToBooleanConverter();
            var result = converter.Convert(value!, typeof(bool), parameter!, CultureInfo.InvariantCulture);
            result.Should().Be(expected);
        }

        [Fact]
        public void CountToBooleanConverter_WithCollection_ShouldReturnCorrectValue()
        {
            var converter = new CountToBooleanConverter();
            converter.Convert(new List<string> { "item" }, typeof(bool), null, CultureInfo.InvariantCulture).Should().Be(true);
            converter.Convert(new List<string>(), typeof(bool), null, CultureInfo.InvariantCulture).Should().Be(false);
        }

        [Theory]
        [InlineData("text", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData(123, true)]
        public void IsNotNullOrEmptyConverter_ShouldReturnCorrectValue(object? value, bool expected)
        {
            var converter = new IsNotNullOrEmptyConverter();
            var result = converter.Convert(value!, typeof(bool), null, CultureInfo.InvariantCulture);
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("Tab1", "Tab2", true)]
        [InlineData("Tab1", "Tab1", false)]
        [InlineData(null, "Tab1", true)]
        public void TabActiveConverter_ShouldReturnTrue_WhenNotEqual(object? val, object param, bool expected)
        {
            var converter = new TabActiveConverter();
            var result = converter.Convert(val!, typeof(bool), param, CultureInfo.InvariantCulture);
            result.Should().Be(expected);
        }
    }
}
