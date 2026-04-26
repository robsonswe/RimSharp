using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Logic;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Logic
{
    public class TabActiveConverterTests
    {
        private readonly TabActiveConverter _converter;

        public TabActiveConverterTests()
        {
            _converter = new TabActiveConverter();
        }

        [Fact]
        public void Convert_WhenValueEqualsParameter_ShouldReturnFalse()
        {
            var result = _converter.Convert("Tab1", typeof(bool), "Tab1", CultureInfo.InvariantCulture);

            result.Should().Be(false);
        }

        [Fact]
        public void Convert_WhenValueDiffersFromParameter_ShouldReturnTrue()
        {
            var result = _converter.Convert("Tab1", typeof(bool), "Tab2", CultureInfo.InvariantCulture);

            result.Should().Be(true);
        }

        [Fact]
        public void Convert_WhenValueIsNull_ShouldReturnTrue()
        {
            var result = _converter.Convert(null, typeof(bool), "Tab1", CultureInfo.InvariantCulture);

            result.Should().Be(true);
        }

        [Fact]
        public void Convert_WhenParameterIsNull_ShouldReturnTrue()
        {
            var result = _converter.Convert("Tab1", typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(true);
        }

        [Fact]
        public void Convert_WhenBothAreNull_ShouldReturnTrue()
        {
            var result = _converter.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(true);
        }

        [Fact]
        public void ConvertBack_ShouldReturnNull()
        {
            var result = _converter.ConvertBack(true, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().BeNull();
        }
    }
}
