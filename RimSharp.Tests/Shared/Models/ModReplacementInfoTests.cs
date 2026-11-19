using System.Text.Json;
using FluentAssertions;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Shared.Models
{
    public class ModReplacementInfoTests
    {
        [Fact]
        public void OriginalVersionList_ShouldParseCorrectly()
        {

            var info = new ModReplacementInfo { Versions = "1.4, 1.5, " };

            // Act & Assert
            info.OriginalVersionList.Should().HaveCount(2);
            info.OriginalVersionList.Should().Contain("1.4");
            info.OriginalVersionList.Should().Contain("1.5");
        }

        [Fact]
        public void ReplacementSteamUrl_ShouldReturnCorrectUrl()
        {

            var info = new ModReplacementInfo { ReplacementSteamId = "12345" };

            // Act & Assert
            info.ReplacementSteamUrl.Should().Be("https://steamcommunity.com/sharedfiles/filedetails/?id=12345");
        }

        [Fact]
        public void ReplacementSteamUrl_WithEmptyId_ShouldReturnNull()
        {

            var info = new ModReplacementInfo { ReplacementSteamId = "" };

            // Act & Assert
            info.ReplacementSteamUrl.Should().BeNull();
        }

        [Fact]
        public void Reason_WhenAbsent_ShouldStayAbsentInJson()
        {
            var json = JsonSerializer.Serialize(new ModReplacementInfo { ModId = "some.mod" });

            json.Should().NotContain("Reason");
        }

        [Fact]
        public void Reason_WhenPresent_ShouldBeSerialized()
        {
            var json = JsonSerializer.Serialize(new ModReplacementInfo
            {
                ModId = "some.mod",
                Reason = "Better performance, same features."
            });

            json.Should().Contain("\"Reason\":\"Better performance, same features.\"");
        }
    }
}

