using System;
using System.Collections.Generic;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.ViewVisibility;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Core.Converters.ViewVisibility
{
    public class ContextMenuVisibilityConverterTests
    {
        private readonly ContextMenuVisibilityConverter _converter;

        public ContextMenuVisibilityConverterTests()
        {
            _converter = new ContextMenuVisibilityConverter();
        }

        [Theory]
        [InlineData(ModType.Core, false)]
        [InlineData(ModType.Expansion, false)]
        [InlineData(ModType.Workshop, true)]
        [InlineData(ModType.WorkshopL, true)]
        [InlineData(ModType.Git, true)]
        [InlineData(ModType.Zipped, true)]
        [InlineData(null, true)]
        public void Convert_ShouldReturnCorrectVisibility(object? modType, bool expected)
        {
            // Act
            var result = _converter.Convert(new List<object?> { modType }, typeof(bool), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void Convert_WithEmptyValues_ShouldReturnTrue()
        {
            // Act
            var result = _converter.Convert(new List<object?>(), typeof(bool), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(true);
        }
    }

    public class ModTypeToRedownloadVisibilityConverterTests
    {
        private readonly ModTypeToRedownloadVisibilityConverter _converter;

        public ModTypeToRedownloadVisibilityConverterTests()
        {
            _converter = new ModTypeToRedownloadVisibilityConverter();
        }

        [Theory]
        [InlineData(ModType.WorkshopL, true)]
        [InlineData(ModType.Workshop, false)]
        [InlineData(ModType.Core, false)]
        [InlineData(null, false)]
        public void Convert_ShouldReturnTrueOnlyForWorkshopL(object? value, bool expected)
        {
            // Act
            var result = _converter.Convert(value, typeof(bool), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }
    }

    public class ModTypeToDeleteVisibilityConverterTests
    {
        private readonly ModTypeToDeleteVisibilityConverter _converter;

        public ModTypeToDeleteVisibilityConverterTests()
        {
            _converter = new ModTypeToDeleteVisibilityConverter();
        }

        [Theory]
        [InlineData(ModType.WorkshopL, true)]
        [InlineData(ModType.Git, true)]
        [InlineData(ModType.Zipped, true)]
        [InlineData(ModType.Workshop, false)]
        [InlineData(ModType.Core, false)]
        [InlineData(ModType.Expansion, false)]
        [InlineData(null, false)]
        public void Convert_ShouldReturnCorrectVisibility(object? value, bool expected)
        {
            // Act
            var result = _converter.Convert(value, typeof(bool), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }
    }
}
