using System;
using System.Collections.Generic;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Numeric;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Numeric
{
    public class MultiplyMultiValueConverterTests
    {
        private readonly MultiplyMultiValueConverter _converter;

        public MultiplyMultiValueConverterTests()
        {
            _converter = new MultiplyMultiValueConverter();
        }

        [Theory]
        [InlineData(new object[] { 2.0, 3.0 }, "1", 6.0)]
        [InlineData(new object[] { 2.0, 5.0 }, "0.5", 5.0)]
        [InlineData(new object[] { 10, 2.5f }, "2", 50.0)]
        public void Convert_ShouldMultiplyAllValues(object[] values, string parameter, double expected)
        {
            // Act
            var result = _converter.Convert(values, typeof(double), parameter, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_WithEmptyList_ShouldReturnOne()
        {
            // Act
            var result = _converter.Convert(new List<object?>(), typeof(double), "1", CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(1.0);
        }
    }
}
