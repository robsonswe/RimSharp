using System;
using System.Globalization;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using FluentAssertions;
using RimSharp.Core.Converters.Visual;
using RimSharp.Shared.Models;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Visual
{
    public class ModTypeToColorConverterTests
    {
        public ModTypeToColorConverterTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaTheory]
        [InlineData(ModType.Core, "RimworldRedBrush")]
        [InlineData(ModType.Expansion, "RimworldDarkGreenBrush")]
        [InlineData(ModType.Workshop, "RimworldBrownBrush")]
        [InlineData(ModType.WorkshopL, "RimworldHighlightBrush")]
        [InlineData(ModType.Git, "RimworldDarkBeigeBrush")]
        [InlineData(ModType.Zipped, "RimworldGrayBrush")]
        public void Convert_ShouldReturnCorrectBrushBasedOnModType(ModType type, string expectedKey)
        {
            // Arrange
            var converter = new ModTypeToColorConverter();
            var expectedBrush = (IBrush)Avalonia.Application.Current!.Resources[expectedKey]!;

            // Act
            var result = converter.Convert(type, typeof(IBrush), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expectedBrush);
        }

        [AvaloniaFact]
        public void Convert_WithInvalidValue_ShouldReturnTransparent()
        {
            // Arrange
            var converter = new ModTypeToColorConverter();

            // Act
            var result = converter.Convert("not a mod type", typeof(IBrush), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(Brushes.Transparent);
        }
    }
}
