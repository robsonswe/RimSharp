using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Text;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Text
{
    public class BooleanToTextConverterTests
    {
        private readonly BooleanToTextConverter _converter;

        public BooleanToTextConverterTests()
        {
            _converter = new BooleanToTextConverter();
        }

        [Theory]
        [InlineData(true, "False|True", "True")]
        [InlineData(false, "False|True", "False")]
        [InlineData(true, "No|Yes", "Yes")]
        [InlineData(false, "No|Yes", "No")]
        [InlineData(true, "Invalid", "")]
        [InlineData("not a bool", "No|Yes", "")]
        public void Convert_ShouldReturnCorrectText(object? value, string parameter, string expected)
        {
            // Act
            var result = _converter.Convert(value, typeof(string), parameter, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("True", "False|True", true)]
        [InlineData("False", "False|True", false)]
        [InlineData("Yes", "No|Yes", true)]
        [InlineData("No", "No|Yes", false)]
        [InlineData("Unknown", "No|Yes", null)]
        public void ConvertBack_ShouldReturnCorrectBoolean(string value, string parameter, bool? expected)
        {
            // Act
            var result = _converter.ConvertBack(value, typeof(bool), parameter, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }
    }
}
