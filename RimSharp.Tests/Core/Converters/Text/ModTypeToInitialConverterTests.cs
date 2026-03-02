using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Text;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Text
{
    public class ModTypeToInitialConverterTests
    {
        private readonly ModTypeToInitialConverter _converter;

        public ModTypeToInitialConverterTests()
        {
            _converter = new ModTypeToInitialConverter();
        }

        [Theory]
        [InlineData(ModType.Core, "C")]
        [InlineData(ModType.Expansion, "E")]
        [InlineData(ModType.Workshop, "W")]
        [InlineData(ModType.WorkshopL, "WL")]
        [InlineData(ModType.Git, "G")]
        [InlineData(ModType.Zipped, "Z")]
        [InlineData((ModType)999, "")]
        [InlineData(null, "")]
        public void Convert_ShouldReturnCorrectInitial(object? value, string expected)
        {

            var result = _converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be(expected);
        }
    }
}

