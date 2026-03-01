using System;
using System.Globalization;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using FluentAssertions;
using RimSharp.Core.Converters.Visual;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Visual
{
    public class ResourceKeyToBrushConverterTests
    {
        public ResourceKeyToBrushConverterTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaFact]
        public void Convert_ShouldReturnBrushFromResource()
        {
            // Arrange
            var converter = new ResourceKeyToBrushConverter();
            var expectedBrush = (IBrush)Avalonia.Application.Current!.Resources["RimworldRedBrush"]!;

            // Act
            var result = converter.Convert("RimworldRedBrush", typeof(IBrush), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expectedBrush);
        }

        [AvaloniaFact]
        public void Convert_WithMissingKey_ShouldReturnTransparent()
        {
            // Arrange
            var converter = new ResourceKeyToBrushConverter();

            // Act
            var result = converter.Convert("NonExistentKey", typeof(IBrush), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(Brushes.Transparent);
        }
    }
}
