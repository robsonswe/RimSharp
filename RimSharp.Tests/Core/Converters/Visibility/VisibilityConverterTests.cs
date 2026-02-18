using System;
using System.Globalization;
using WinVisibility = System.Windows.Visibility;
using FluentAssertions;
using RimSharp.Core.Converters.ViewVisibility;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Visibility
{
    public class VisibilityConverterTests
    {
        [Theory]
        [InlineData(true, true, WinVisibility.Visible)]
        [InlineData(true, false, WinVisibility.Collapsed)]
        [InlineData(false, true, WinVisibility.Collapsed)]
        [InlineData(false, false, WinVisibility.Collapsed)]
        [InlineData(null, true, WinVisibility.Collapsed)]
        public void IsActiveAndHasIssuesToVisibilityConverter_ShouldReturnCorrectVisibility(object? hasIssues, object? isActive, WinVisibility expected)
        {
            var converter = new IsActiveAndHasIssuesToVisibilityConverter();
            var result = converter.Convert(new[] { hasIssues!, isActive! }, typeof(WinVisibility), null, CultureInfo.InvariantCulture);
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("SomeValue", null, WinVisibility.Visible)]
        [InlineData(null, null, WinVisibility.Collapsed)]
        [InlineData("SomeValue", "Invert", WinVisibility.Collapsed)]
        [InlineData(null, "Invert", WinVisibility.Visible)]
        public void NullToVisibilityConverter_ShouldReturnCorrectVisibility(object? value, string? parameter, WinVisibility expected)
        {
            var converter = new NullToVisibilityConverter();
            var result = converter.Convert(value!, typeof(WinVisibility), parameter, CultureInfo.InvariantCulture);
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("Tab1", "Tab1", WinVisibility.Visible)]
        [InlineData("Tab1", "Tab2", WinVisibility.Collapsed)]
        [InlineData(null, "Tab1", WinVisibility.Collapsed)]
        public void TabVisibilityConverter_ShouldReturnCorrectVisibility(object? value, string? parameter, WinVisibility expected)
        {
            var converter = new TabVisibilityConverter();
            var result = converter.Convert(value!, typeof(WinVisibility), parameter, CultureInfo.InvariantCulture);
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("text", false, WinVisibility.Visible)]
        [InlineData("", false, WinVisibility.Collapsed)]
        [InlineData(null, false, WinVisibility.Collapsed)]
        [InlineData("text", true, WinVisibility.Collapsed)]
        [InlineData("", true, WinVisibility.Visible)]
        public void IsNotNullOrEmptyToVisibilityConverter_ShouldReturnCorrectVisibility(object? value, bool inverse, WinVisibility expected)
        {
            var converter = new IsNotNullOrEmptyToVisibilityConverter { Inverse = inverse };
            var result = converter.Convert(value!, typeof(WinVisibility), null, CultureInfo.InvariantCulture);
            result.Should().Be(expected);
        }
    }
}
