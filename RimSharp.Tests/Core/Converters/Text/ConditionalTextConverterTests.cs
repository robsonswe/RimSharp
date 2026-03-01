using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Text;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Text
{
    public class ConditionalTextConverterTests
    {
        private readonly ConditionalTextConverter _converter;

        public ConditionalTextConverterTests()
        {
            _converter = new ConditionalTextConverter();
        }

        [Theory]
        [InlineData(true, "False|True", "True")]
        [InlineData(false, "False|True", "False")]
        [InlineData(true, "Add Mod|Add Collection", "Add Collection")]
        [InlineData(false, "Add Mod|Add Collection", "Add Mod")]
        [InlineData(true, "SinglePart", "True")]
        [InlineData(123, "False|True", "123")]
        [InlineData(null, "False|True", null)]
        public void Convert_ShouldReturnCorrectText(object? value, string parameter, string? expected)
        {
            // Act
            var result = _converter.Convert(value, typeof(string), parameter, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }
    }
}
