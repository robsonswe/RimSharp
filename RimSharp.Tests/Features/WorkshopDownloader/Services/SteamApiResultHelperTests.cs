using System.Collections.Generic;
using FluentAssertions;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using Xunit;

namespace RimSharp.Tests.Features.WorkshopDownloader.Services
{
    public class SteamApiResultHelperTests
    {
        [Theory]
        [InlineData(1, "Success.")]
        [InlineData(2, "Generic failure reported by Steam.")]
        [InlineData(9, "Workshop item not found (Item is deleted or unlisted).")]
        [InlineData(999, "Unknown or unhandled Steam API result code (999).")]
        public void GetDescription_ShouldReturnCorrectString(int code, string expected)
        {
            // Act
            var result = SteamApiResultHelper.GetDescription(code);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void ExtractAndSortVersionTags_ShouldSortNumerically()
        {
            // Arrange
            var tags = new List<SteamTag>
            {
                new SteamTag { Tag = "1.10" },
                new SteamTag { Tag = "1.2" },
                new SteamTag { Tag = "Mod" }, // Should be ignored
                new SteamTag { Tag = "1.5" }
            };

            // Act
            var result = SteamApiResultHelper.ExtractAndSortVersionTags(tags);

            // Assert
            result.Should().HaveCount(3);
            result.Should().ContainInOrder("1.2", "1.5", "1.10");
        }

        [Fact]
        public void ExtractAndSortVersionTags_WhenEmpty_ShouldReturnEmpty()
        {
            // Act
            var result = SteamApiResultHelper.ExtractAndSortVersionTags(null);

            // Assert
            result.Should().BeEmpty();
        }
    }
}
