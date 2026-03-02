using System;
using System.Collections.Generic;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Text;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Text
{
    public class ListToStringConverterTests
    {
        private readonly ListToStringConverter _converter;

        public ListToStringConverterTests()
        {
            _converter = new ListToStringConverter();
        }

        [Fact]
        public void Convert_WithPopulatedList_ShouldReturnJoinedString()
        {

            var list = new List<string> { "Item1", "Item2" };

            var result = _converter.Convert(list, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be("Item1, Item2");
        }

        [Fact]
        public void Convert_WithCustomSeparator_ShouldReturnJoinedString()
        {

            var list = new List<string> { "Item1", "Item2" };

            var result = _converter.Convert(list, typeof(string), " | ", CultureInfo.InvariantCulture);

            result.Should().Be("Item1 | Item2");
        }

        [Theory]
        [InlineData(null)]
        [InlineData(123)]
        public void Convert_WithInvalidOrEmptyValue_ShouldReturnNone(object? value)
        {

            var result = _converter.Convert(value!, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be("None");
        }

        [Fact]
        public void Convert_WithEmptyList_ShouldReturnNone()
        {

            var list = new List<string>();

            var result = _converter.Convert(list, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be("None");
        }
    }
}

