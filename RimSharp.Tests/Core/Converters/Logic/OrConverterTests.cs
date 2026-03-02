using System;
using System.Collections.Generic;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Logic;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Logic
{
    public class OrConverterTests
    {
        private readonly OrConverter _converter;

        public OrConverterTests()
        {
            _converter = new OrConverter();
        }

        [Theory]
        [InlineData(new object[] { true, false }, true)]
        [InlineData(new object[] { false, false }, false)]
        [InlineData(new object[] { true, true }, true)]
        [InlineData(new object[] { false, true, false }, true)]
        public void Convert_ShouldReturnCorrectOr(object[] values, bool expected)
        {

            var result = _converter.Convert(values, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_WithNonBooleanValues_ShouldHandleCorrectly()
        {

            var values = new object[] { "not a bool", true };

            var result = _converter.Convert(values, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(true);
        }

        [Fact]
        public void Convert_WithEmptyList_ShouldReturnFalse()
        {

            var result = _converter.Convert(new List<object?>(), typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(false);
        }
    }
}


