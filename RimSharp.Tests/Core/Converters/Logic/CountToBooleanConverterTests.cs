using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using RimSharp.Core.Converters.Logic;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Logic
{
    public class CountToBooleanConverterTests
    {
        private readonly CountToBooleanConverter _converter;

        public CountToBooleanConverterTests()
        {
            _converter = new CountToBooleanConverter();
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(0, false)]
        [InlineData(-1, false)]
        [InlineData(5, true)]
        public void Convert_WithInt_ShouldReturnTrueWhenPositive(int value, bool expected)
        {
            var result = _converter.Convert(value, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(1L, true)]
        [InlineData(0L, false)]
        [InlineData(-1L, false)]
        public void Convert_WithLong_ShouldReturnTrueWhenPositive(long value, bool expected)
        {
            var result = _converter.Convert(value, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(1.0, true)]
        [InlineData(0.0, false)]
        [InlineData(-1.0, false)]
        public void Convert_WithDouble_ShouldReturnTrueWhenPositive(double value, bool expected)
        {
            var result = _converter.Convert(value, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_WithDecimalPositive_ShouldReturnTrue()
        {
            var result = _converter.Convert(1.5m, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(true);
        }

        [Fact]
        public void Convert_WithDecimalZero_ShouldReturnFalse()
        {
            var result = _converter.Convert(0m, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(false);
        }

        [Fact]
        public void Convert_WithNonEmptyCollection_ShouldReturnTrue()
        {
            var list = new List<string> { "item1", "item2" };

            var result = _converter.Convert(list, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(true);
        }

        [Fact]
        public void Convert_WithEmptyCollection_ShouldReturnFalse()
        {
            var list = new List<string>();

            var result = _converter.Convert(list, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(false);
        }

        [Fact]
        public void Convert_WithNonEmptyEnumerable_ShouldReturnTrue()
        {
            var enumerable = Enumerable.Range(1, 3);

            var result = _converter.Convert(enumerable, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(true);
        }

        [Fact]
        public void Convert_WithEmptyEnumerable_ShouldReturnFalse()
        {
            var enumerable = Enumerable.Empty<int>();

            var result = _converter.Convert(enumerable, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(false);
        }

        [Fact]
        public void Convert_WithNull_ShouldReturnFalse()
        {
            var result = _converter.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(false);
        }

        [Fact]
        public void Convert_WithUnsupportedType_ShouldReturnFalse()
        {
            var result = _converter.Convert("a string", typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(false);
        }

        [Fact]
        public void ConvertBack_ShouldReturnNull()
        {
            var result = _converter.ConvertBack(true, typeof(int), null, CultureInfo.InvariantCulture);

            result.Should().BeNull();
        }
    }
}
