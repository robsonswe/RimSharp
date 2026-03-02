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
    public class BooleanToBrushConverterTests
    {
        public BooleanToBrushConverterTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaTheory]
        [InlineData(true, "RimworldRedBrush|RimworldDarkGreenBrush", "RimworldDarkGreenBrush")]
        [InlineData(false, "RimworldRedBrush|RimworldDarkGreenBrush", "RimworldRedBrush")]
        public void Convert_ShouldReturnCorrectBrushFromResources(bool value, string parameter, string expectedKey)
        {

            var converter = new BooleanToBrushConverter();
            var expectedBrush = (IBrush)Avalonia.Application.Current!.Resources[expectedKey]!;

            var result = converter.Convert(value, typeof(IBrush), parameter, CultureInfo.InvariantCulture);

            result.Should().Be(expectedBrush);
        }

        [AvaloniaFact]
        public void Convert_WithMissingResource_ShouldReturnTransparent()
        {

            var converter = new BooleanToBrushConverter();

            var result = converter.Convert(true, typeof(IBrush), "NonExistent|AlsoMissing", CultureInfo.InvariantCulture);

            result.Should().Be(Brushes.Transparent);
        }
    }
}

