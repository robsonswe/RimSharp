using System.Globalization;
using System.Windows.Data;
using FluentAssertions;
using RimSharp.Core.Converters.Logic;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Logic
{
    public class NullToBoolConverterTests
    {
        private readonly NullToBoolConverter _converter;

        public NullToBoolConverterTests()
        {
            _converter = new NullToBoolConverter();
        }

        [Theory]
        [InlineData(null, "Null", true)]
        [InlineData(null, "True", false)]
        [InlineData(null, "False", false)]
        [InlineData(true, "True", true)]
        [InlineData(true, "False", false)]
        [InlineData(true, "Null", false)]
        [InlineData(false, "False", true)]
        [InlineData(false, "True", false)]
        [InlineData(false, "Null", false)]
        public void Convert_ShouldReturnExpectedBoolean(object? value, string parameter, bool expected)
        {
            // Act
            var result = _converter.Convert(value!, typeof(bool), parameter, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(true, "Null", null)]
        [InlineData(true, "True", true)]
        [InlineData(true, "False", false)]
        public void ConvertBack_ShouldReturnExpectedValue(bool value, string parameter, bool? expected)
        {
            // Act
            var result = _converter.ConvertBack(value, typeof(bool?), parameter, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void ConvertBack_WhenCheckedIsFalse_ShouldReturnDoNothing()
        {
            // Act
            var result = _converter.ConvertBack(false, typeof(bool?), "True", CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(Binding.DoNothing);
        }
    }
}
