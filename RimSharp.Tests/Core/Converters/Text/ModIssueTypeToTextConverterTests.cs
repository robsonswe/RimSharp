using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Text;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Text
{
    public class ModIssueTypeToTextConverterTests
    {
        private readonly ModIssueTypeToTextConverter _converter;

        public ModIssueTypeToTextConverterTests()
        {
            _converter = new ModIssueTypeToTextConverter();
        }

        [Theory]
        [InlineData(ModIssueType.Sorting, "Sorting Order")]
        [InlineData(ModIssueType.Dependency, "Missing Dependency")]
        [InlineData(ModIssueType.Incompatibility, "Incompatibility")]
        [InlineData(ModIssueType.SoftIncompatibility, "Soft Incompatibility")]
        [InlineData(ModIssueType.HardIncompatibility, "Hard Incompatibility")]
        [InlineData(ModIssueType.VersionMismatch, "Version Mismatch")]
        [InlineData(ModIssueType.Duplicate, "Duplicate Mod")]
        [InlineData((ModIssueType)999, "999")]
        [InlineData(null, "")]
        public void Convert_ShouldReturnCorrectText(object? value, string expected)
        {
            // Act
            var result = _converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }
    }
}
