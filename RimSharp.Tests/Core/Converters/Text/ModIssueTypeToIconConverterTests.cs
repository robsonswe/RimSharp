using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Text;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Text
{
    public class ModIssueTypeToIconConverterTests
    {
        private readonly ModIssueTypeToIconConverter _converter;

        public ModIssueTypeToIconConverterTests()
        {
            _converter = new ModIssueTypeToIconConverter();
        }

        [Theory]
        [InlineData(ModIssueType.Sorting, "fa-arrow-down-z-a")]
        [InlineData(ModIssueType.Dependency, "fa-box")]
        [InlineData(ModIssueType.Incompatibility, "fa-ban")]
        [InlineData(ModIssueType.SoftIncompatibility, "fa-triangle-exclamation")]
        [InlineData(ModIssueType.HardIncompatibility, "fa-ban")]
        [InlineData(ModIssueType.VersionMismatch, "fa-triangle-exclamation")]
        [InlineData(ModIssueType.Duplicate, "fa-clone")]
        [InlineData((ModIssueType)999, "fa-question")]
        [InlineData(null, "")]
        public void Convert_ShouldReturnCorrectIcon(object? value, string expected)
        {

            var result = _converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be(expected);
        }
    }
}

