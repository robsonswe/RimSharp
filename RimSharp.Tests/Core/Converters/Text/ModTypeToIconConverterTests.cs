using System;
using RimSharp.Core.Converters.Text;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Text
{
    public class ModTypeToIconConverterTests
    {
        private readonly ModTypeToIconConverter _converter;

        public ModTypeToIconConverterTests()
        {
            _converter = new ModTypeToIconConverter();
        }

        [Theory]
        [InlineData(ModType.Core, "fa-earth-americas")]
        [InlineData(ModType.Expansion, "fa-puzzle-piece")]
        [InlineData(ModType.Workshop, "fa-steam")]
        [InlineData(ModType.Tracked, "fa-eye")]
        [InlineData(ModType.Git, "fa-git-alt")]
        [InlineData(ModType.Untracked, "fa-eye-slash")]
        [InlineData((ModType)999, "fa-question")]
        [InlineData(null, "")]
        [InlineData("not a mod type", "")]
        public void Convert_ShouldReturnCorrectIcon(object? value, string expected)
        {
            var result = _converter.Convert(value, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(expected, result);
        }
    }
}
