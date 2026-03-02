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

            var values = new object?[] { "TooltipText", true };

            var result = _converter.Convert(values, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be("TooltipText");
        }

        [Fact]
        public void Convert_WithEmptyValues_ShouldReturnNull()
        {

            var values = new List<object?>();

            var result = _converter.Convert(values, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().BeNull();
        }
    }
}

