using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Media;
using FluentAssertions;
using RimSharp.Core.Converters.Visual;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Visual
{
    public class ModWeightConverterTests
    {
        private readonly ModWeightConverter _converter;

        public ModWeightConverterTests()
        {
            _converter = new ModWeightConverter();
        }

        [Theory]
        [InlineData(true, ModType.Workshop, FontWeight.Bold)]
        [InlineData(false, ModType.Core, FontWeight.Bold)]
        [InlineData(false, ModType.Expansion, FontWeight.Bold)]
        [InlineData(false, ModType.Workshop, FontWeight.Normal)]
        [InlineData(false, ModType.Git, FontWeight.Normal)]
        public void Convert_ShouldReturnCorrectWeight(bool isFavorite, ModType type, FontWeight expected)
        {
            // Act
            var result = _converter.Convert(new List<object?> { isFavorite, type }, typeof(FontWeight), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_WithInsufficientValues_ShouldReturnNormal()
        {
            // Act
            var result = _converter.Convert(new List<object?> { false }, typeof(FontWeight), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(FontWeight.Normal);
        }
    }
}
