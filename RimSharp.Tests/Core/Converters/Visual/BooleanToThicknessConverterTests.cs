using System;
using System.Globalization;
using Avalonia;
using FluentAssertions;
using RimSharp.Core.Converters.Visual;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Visual
{
    public class BooleanToThicknessConverterTests
    {
        private readonly BooleanToThicknessConverter _converter;

        public BooleanToThicknessConverterTests()
        {
            _converter = new BooleanToThicknessConverter();
        }

        [Theory]
        [InlineData(true, "2,0,0,0|5,0,0,0", 2, 0, 0, 0)]
        [InlineData(false, "2,0,0,0|5,0,0,0", 5, 0, 0, 0)]
        [InlineData(true, "10|20", 10, 10, 10, 10)]
        [InlineData(false, "10|20", 20, 20, 20, 20)]
        [InlineData(true, "invalid", 0, 0, 0, 0)]
        public void Convert_ShouldReturnCorrectThickness(bool value, string parameter, double left, double top, double right, double bottom)
        {
            // Act
            var result = (Thickness)_converter.Convert(value, typeof(Thickness), parameter, CultureInfo.InvariantCulture)!;

            // Assert
            result.Left.Should().Be(left);
            result.Top.Should().Be(top);
            result.Right.Should().Be(right);
            result.Bottom.Should().Be(bottom);
        }
    }
}
