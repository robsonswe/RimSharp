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
        [InlineData(ModType.Core, "BrushStateDanger")]
        [InlineData(ModType.Expansion, "BrushStateSuccess")]
        [InlineData(ModType.Workshop, "BrushStatePrimary")]
        [InlineData(ModType.Tracked, "BrushStateHighlight")]
        [InlineData(ModType.Git, "BrushStateGit")]
        [InlineData(ModType.Untracked, "BrushStateNeutral")]
        public void Convert_ShouldReturnCorrectBrushBasedOnModType(ModType type, string expectedKey)
        {

            var converter = new ModTypeToColorConverter();
            var expectedBrush = (IBrush)Avalonia.Application.Current!.Resources[expectedKey]!;

            var result = converter.Convert(type, typeof(IBrush), null, CultureInfo.InvariantCulture);

            result.Should().Be(expectedBrush);
        }

        [AvaloniaFact]
        public void Convert_WithInvalidValue_ShouldReturnTransparent()
        {

            var converter = new ModTypeToColorConverter();

            var result = converter.Convert("not a mod type", typeof(IBrush), null, CultureInfo.InvariantCulture);

            result.Should().Be(Brushes.Transparent);
        }
    }
}
