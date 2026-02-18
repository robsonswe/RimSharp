using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Text;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Text
{
    public class ModTypeToDescriptionConverterTests
    {
        private readonly ModTypeToDescriptionConverter _converter;

        public ModTypeToDescriptionConverterTests()
        {
            _converter = new ModTypeToDescriptionConverter();
        }

        [Theory]
        [InlineData(ModType.Core, "Core Game")]
        [InlineData(ModType.Expansion, "Expansion/DLC")]
        [InlineData(ModType.Workshop, "Workshop Mod")]
        [InlineData(ModType.WorkshopL, "Workshop Mod (Local)")]
        [InlineData(ModType.Git, "Git Repository")]
        [InlineData(ModType.Zipped, "Local/Zipped Mod")]
        [InlineData((ModType)999, "Unknown Mod Type")]
        [InlineData(null, "")]
        [InlineData("not a mod type", "")]
        public void Convert_ShouldReturnCorrectDescription(object? value, string expected)
        {
            // Act
            var result = _converter.Convert(value!, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }
    }
}
