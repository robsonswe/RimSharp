using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Logic;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Logic
{
    public class InequalityConverterTests
    {
        private readonly InequalityConverter _converter;

        public InequalityConverterTests()
        {
            _converter = new InequalityConverter();
        }

        [Theory]
        [InlineData("Tab1", "Tab2", true)]
        [InlineData("Tab1", "Tab1", false)]
        [InlineData(1, 2, true)]
        [InlineData(1, 1, false)]
        [InlineData("1", 1, false)] // ToString comparison
        [InlineData(null, "Not null", true)]
        [InlineData("Not null", null, true)]
        [InlineData(null, null, false)]
        public void Convert_ShouldReturnCorrectInequality(object? val1, object? val2, bool expected)
        {
            // Act
            var result = _converter.Convert(new[] { val1!, val2! }, typeof(bool), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_WithInsufficientValues_ShouldReturnTrue()
        {
            // Act
            var result = _converter.Convert(new[] { new object() }, typeof(bool), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(true);
        }
    }
}
