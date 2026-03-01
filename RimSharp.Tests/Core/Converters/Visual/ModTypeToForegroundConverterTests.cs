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
    public class ModTypeToForegroundConverterTests
    {
        public ModTypeToForegroundConverterTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaFact]
        public void Convert_WithWorkshopL_ShouldReturnBlack()
        {
            // Arrange
            var converter = new ModTypeToForegroundConverter();
            var blackBrush = (IBrush)Avalonia.Application.Current!.Resources["RimworldBlackBrush"]!;

            // Act
            var result = converter.Convert(ModType.WorkshopL, typeof(IBrush), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(blackBrush);
        }

        [AvaloniaTheory]
        [InlineData(ModType.Core)]
        [InlineData(ModType.Expansion)]
        [InlineData(ModType.Workshop)]
        [InlineData(ModType.Git)]
        [InlineData(ModType.Zipped)]
        public void Convert_WithOtherModTypes_ShouldReturnWhite(ModType type)
        {
            // Arrange
            var converter = new ModTypeToForegroundConverter();
            var whiteBrush = (IBrush)Avalonia.Application.Current!.Resources["RimworldWhiteBrush"]!;

            // Act
            var result = converter.Convert(type, typeof(IBrush), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(whiteBrush);
        }
    }
}
