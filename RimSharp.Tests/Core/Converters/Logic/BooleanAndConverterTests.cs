using System;
using System.Collections.Generic;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Logic;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Logic
{
    public class BooleanAndConverterTests
    {
        private readonly BooleanAndConverter _converter;

        public BooleanAndConverterTests()
        {
            _converter = new BooleanAndConverter();
        }

        [Theory]
        [InlineData(new object[] { true, true }, true)]
        [InlineData(new object[] { true, false }, false)]
        [InlineData(new object[] { false, false }, false)]
        [InlineData(new object[] { true, true, true }, true)]
        public void Convert_ShouldReturnCorrectBooleanAnd(object[] values, bool expected)
        {

            var result = _converter.Convert(values, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_WithNonBooleanValues_ShouldReturnFalse()
        {

            var values = new object[] { true, "not a bool" };

            var result = _converter.Convert(values, typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(false);
        }

        [Fact]
        public void Convert_WithEmptyList_ShouldReturnTrue()
        {

            var result = _converter.Convert(new List<object?>(), typeof(bool), null, CultureInfo.InvariantCulture);

            result.Should().Be(true);
        }
    }
}


