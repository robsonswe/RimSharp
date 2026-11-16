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
        [InlineData(ModType.Expansion, "DLC")]
        [InlineData(ModType.Workshop, "Workshop")]
        [InlineData(ModType.Tracked, "Local (Tracked)")]
        [InlineData(ModType.Git, "Local (Git)")]
        [InlineData(ModType.Untracked, "Local (Untracked)")]
        [InlineData((ModType)999, "Unknown")]
        [InlineData(null, "")]
        [InlineData("not a mod type", "")]
        public void Convert_ShouldReturnCorrectDescription(object? value, string expected)
        {

            var result = _converter.Convert(value!, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be(expected);
        }
    }
}

