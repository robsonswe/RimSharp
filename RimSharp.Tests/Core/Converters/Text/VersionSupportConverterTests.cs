using System;
using System.Collections.Generic;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Text;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Text
{
    public class VersionSupportConverterTests
    {
        private readonly VersionSupportConverter _converter;

        public VersionSupportConverterTests()
        {
            _converter = new VersionSupportConverter();
        }

        [Fact]
        public void Convert_WithNullOrEmpty_ShouldReturnNone()
        {
            // Act
            var result = _converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be("None");
        }

        [Fact]
        public void Convert_ToString_ShouldReturnCommaSeparatedList()
        {
            // Arrange
            var versions = new List<VersionSupport>
            {
                new VersionSupport("1.4", VersionSource.Official),
                new VersionSupport("1.5", VersionSource.Mlie, true)
            };

            // Act
            var result = _converter.Convert(versions, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be("1.4, 1.5");
        }

        [Fact]
        public void Convert_ToString_WithOfficialOnlyParameter_ShouldFilterCorrectly()
        {
            // Arrange
            var versions = new List<VersionSupport>
            {
                new VersionSupport("1.4", VersionSource.Official),
                new VersionSupport("1.5", VersionSource.Mlie, true)
            };

            // Act
            var result = _converter.Convert(versions, typeof(string), "OfficialOnly", CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be("1.4");
        }

        [Fact]
        public void Convert_ToString_WithNoMatches_ShouldReturnNone()
        {
            // Arrange
            var versions = new List<VersionSupport>
            {
                new VersionSupport("1.5", VersionSource.Mlie, true)
            };

            // Act
            var result = _converter.Convert(versions, typeof(string), "OfficialOnly", CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be("None");
        }
    }
}
