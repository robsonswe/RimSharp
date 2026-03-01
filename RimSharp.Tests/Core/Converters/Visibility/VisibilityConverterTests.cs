using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.ViewVisibility;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Visibility
{
    public class VisibilityConverterTests
    {
        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, false)]
        [InlineData(null, true, false)]
        public void IsActiveAndHasIssuesToVisibilityConverter_ShouldReturnCorrectVisibility(object? hasIssues, object? isActive, bool expected)
        {
            var converter = new IsActiveAndHasIssuesToVisibilityConverter();
            var result = converter.Convert(new[] { hasIssues!, isActive! }, typeof(bool), null, CultureInfo.InvariantCulture);
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("SomeValue", null, true)]
        [InlineData(null, null, false)]
        [InlineData("SomeValue", "Invert", false)]
        [InlineData(null, "Invert", true)]
        public void NullToVisibilityConverter_ShouldReturnCorrectVisibility(object? value, string? parameter, bool expected)
        {
            var converter = new NullToVisibilityConverter();
            var result = converter.Convert(value!, typeof(bool), parameter, CultureInfo.InvariantCulture);
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("Tab1", "Tab1", true)]
        [InlineData("Tab1", "Tab2", false)]
        [InlineData(null, "Tab1", false)]
        public void TabVisibilityConverter_ShouldReturnCorrectVisibility(object? value, string? parameter, bool expected)
        {
            var converter = new TabVisibilityConverter();
            var result = converter.Convert(value!, typeof(bool), parameter, CultureInfo.InvariantCulture);
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("text", false, true)]
        [InlineData("", false, false)]
        [InlineData(null, false, false)]
        [InlineData("text", true, false)]
        [InlineData("", true, true)]
        public void IsNotNullOrEmptyToVisibilityConverter_ShouldReturnCorrectVisibility(string? value, bool invert, bool expected)
        {
            var converter = new IsNotNullOrEmptyToVisibilityConverter();
            var result = converter.Convert(value!, typeof(bool), invert, CultureInfo.InvariantCulture);
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(true, false, true)]
        [InlineData(false, false, false)]
        [InlineData(true, true, false)]
        [InlineData(false, true, true)]
        public void BooleanToVisibilityConverter_ShouldReturnCorrectVisibility(bool value, bool invert, bool expected)
        {
            var converter = new BooleanToVisibilityConverter();
            var result = converter.Convert(value, typeof(bool), invert, CultureInfo.InvariantCulture);
            result.Should().Be(expected);
        }
    }
}
