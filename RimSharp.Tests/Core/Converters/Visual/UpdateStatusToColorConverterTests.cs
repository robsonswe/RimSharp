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
    public class UpdateStatusToColorConverterTests
    {
        public UpdateStatusToColorConverterTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaTheory]
        [InlineData("Up to date", "BrushStateSuccess")]
        [InlineData("Update available", "BrushStateHighlight")]
        [InlineData("Download failed", "BrushStateDangerStrong")]
        [InlineData("Checking...", "BrushStateWarning")]
        [InlineData("Unknown status", "BrushStateNeutral")]
        public void Convert_ShouldReturnCorrectBrushBasedOnStatus(string status, string expectedKey)
        {

            var converter = new UpdateStatusToColorConverter();
            var expectedBrush = (IBrush)Avalonia.Application.Current!.Resources[expectedKey]!;

            var result = converter.Convert(status, typeof(IBrush), null, CultureInfo.InvariantCulture);

            result.Should().Be(expectedBrush);
        }

        [AvaloniaFact]
        public void Convert_WithNullStatus_ShouldReturnGray()
        {

            var converter = new UpdateStatusToColorConverter();

            var result = converter.Convert(null, typeof(IBrush), null, CultureInfo.InvariantCulture);

            result.Should().Be(Brushes.Gray);
        }
    }
}

