using System;
using System.Collections.Generic;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Text;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Text
{
    public class TrimmedTextTooltipConverterTests
    {
        private readonly TrimmedTextTooltipConverter _converter;

        public TrimmedTextTooltipConverterTests()
        {
            _converter = new TrimmedTextTooltipConverter();
        }

        [Fact]
        public void Convert_ShouldReturnFirstValue()
        {
            // Arrange
            var values = new object?[] { "TooltipText", true };

            // Act
            var result = _converter.Convert(values, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be("TooltipText");
        }

        [Fact]
        public void Convert_WithEmptyValues_ShouldReturnNull()
        {
            // Arrange
            var values = new List<object?>();

            // Act
            var result = _converter.Convert(values, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().BeNull();
        }
    }
}
