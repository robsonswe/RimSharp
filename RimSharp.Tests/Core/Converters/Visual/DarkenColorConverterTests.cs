using System;
using System.Globalization;
using Avalonia.Media;
using FluentAssertions;
using RimSharp.Core.Converters.Visual;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Visual
{
    public class DarkenColorConverterTests
    {
        private readonly DarkenColorConverter _converter;

        public DarkenColorConverterTests()
        {
            _converter = new DarkenColorConverter();
        }

        [Fact]
        public void Convert_WithColor_ShouldDarkenCorrectly()
        {
            // Arrange
            var color = Color.FromArgb(255, 100, 100, 100);
            _converter.DarkenFactor = 0.5;

            // Act
            var result = (Color)_converter.Convert(color, typeof(Color), null, CultureInfo.InvariantCulture)!;

            // Assert
            result.R.Should().Be(50);
            result.G.Should().Be(50);
            result.B.Should().Be(50);
            result.A.Should().Be(255);
        }

        [Fact]
        public void Convert_WithBrush_ShouldDarkenCorrectly()
        {
            // Arrange
            var brush = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100));
            _converter.DarkenFactor = 0.5;

            // Act
            var result = (ISolidColorBrush)_converter.Convert(brush, typeof(IBrush), null, CultureInfo.InvariantCulture)!;

            // Assert
            result.Color.R.Should().Be(50);
            result.Color.G.Should().Be(50);
            result.Color.B.Should().Be(50);
            result.Color.A.Should().Be(255);
        }

        [Fact]
        public void Convert_WithInvalidValue_ShouldReturnOriginal()
        {
            // Act
            var result = _converter.Convert("not a color", typeof(object), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be("not a color");
        }
    }
}
