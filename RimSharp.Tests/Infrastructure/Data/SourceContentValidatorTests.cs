#nullable enable
using FluentAssertions;
using RimSharp.Infrastructure.Data;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Data
{
    /// <summary>
    /// Format validation of raw downloaded source content. Valid documents
    /// (including the intentionally near-empty bundled seeds) must be accepted;
    /// malformed or wrong-shape documents must be rejected with a reason.
    /// </summary>
    public class SourceContentValidatorTests
    {
        [Theory]
        [InlineData(@"{ ""rules"": {} }")]
        [InlineData(@"{ ""rules"": { ""mod.a"": {} } }")]
        [InlineData(@"{ ""timestamp"": 1789239946, ""rules"": { ""mod.a"": { ""incompatibleWith"": {} } } }")]
        [InlineData(@"{}")]
        public void ValidateRules_ShouldAcceptValidDocuments(string content)
        {
            SourceContentValidator.ValidateRules(content).Should().BeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[]")]
        [InlineData(@"{ ""rules"": [] }")]
        [InlineData(@"{ ""rules"": ""oops"" }")]
        [InlineData(@"{ ""timestamp"": ""yesterday"" }")]
        public void ValidateRules_ShouldRejectInvalidDocuments(string content)
        {
            SourceContentValidator.ValidateRules(content).Should().NotBeNull();
        }

        [Theory]
        [InlineData(@"{ ""mods"": {} }")]
        [InlineData(@"{ ""mods"": { ""pkg.a"": { ""123"": { ""name"": ""A"" } } } }")]
        [InlineData(@"{}")]
        public void ValidateModInfo_ShouldAcceptValidDocuments(string content)
        {
            SourceContentValidator.ValidateModInfo(content).Should().BeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData("<html/>")]
        [InlineData(@"{ ""mods"": [] }")]
        public void ValidateModInfo_ShouldRejectInvalidDocuments(string content)
        {
            SourceContentValidator.ValidateModInfo(content).Should().NotBeNull();
        }

        [Theory]
        [InlineData(@"{ ""database"": { ""123"": { ""packageId"": ""mod.a"", ""name"": ""A"" } } }")]
        [InlineData(@"{ ""version"": 1790337009, ""database"": { ""123"": { ""packageId"": ""mod.a"" }, ""456"": { ""packageId"": ""mod.b"" } } }")]
        [InlineData(@"{ ""version"": 1790337009, ""database"": { ""294100"": { ""appid"": true, ""packageid"": ""ludeon.rimworld"", ""packageId"": ""ludeon.rimworld"" } } }")]
        public void ValidateSteamWorkshopDb_ShouldAcceptValidDocuments(string content)
        {
            SourceContentValidator.ValidateSteamWorkshopDb(content).Should().BeNull();
        }

        [Fact]
        public void ValidateSteamWorkshopDb_ShouldAcceptLegacyFlatShape()
        {
            var content = @"{ ""version"": 1790337009, ""123"": { ""packageId"": ""mod.a"" } }";

            SourceContentValidator.ValidateSteamWorkshopDb(content).Should().BeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData("<html/>")]
        [InlineData("[]")]
        [InlineData(@"{ ""version"": 1 }")]
        [InlineData(@"{ ""database"": {} }")]
        [InlineData(@"{ ""database"": { ""123"": ""not an object"" } }")]
        [InlineData(@"{ ""database"": { ""123"": { ""name"": ""no package id or appid"" } } }")]
        [InlineData(@"{ ""version"": ""yesterday"", ""database"": { ""123"": { ""packageId"": ""a.b"" } } }")]
        public void ValidateSteamWorkshopDb_ShouldRejectInvalidDocuments(string content)
        {
            SourceContentValidator.ValidateSteamWorkshopDb(content).Should().NotBeNull();
        }

        [Theory]
        [InlineData(@"{ ""rules"": [] }")]
        [InlineData(@"{ ""rules"": [ { ""oldWorkshopId"": ""123"" } ] }")]
        [InlineData(@"{ ""mods"": {} }")]
        [InlineData(@"{}")]
        public void ValidateReplacements_ShouldAcceptValidDocuments(string content)
        {
            SourceContentValidator.ValidateReplacements(content).Should().BeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData(@"{ ""rules"": {} }")]
        [InlineData(@"{ ""mods"": [] }")]
        public void ValidateReplacements_ShouldRejectInvalidDocuments(string content)
        {
            SourceContentValidator.ValidateReplacements(content).Should().NotBeNull();
        }

        [Theory]
        [InlineData("<ModIdsToFix></ModIdsToFix>")]
        [InlineData("<ModIdsToFix><li>mod.a</li><li>mod.b</li></ModIdsToFix>")]
        public void ValidateNoVersionWarningXml_ShouldAcceptValidDocuments(string content)
        {
            SourceContentValidator.ValidateNoVersionWarningXml(content).Should().BeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData("not xml")]
        [InlineData("<ModIdsToFix><li>unclosed</li>")]
        public void ValidateNoVersionWarningXml_ShouldRejectInvalidDocuments(string content)
        {
            SourceContentValidator.ValidateNoVersionWarningXml(content).Should().NotBeNull();
        }
    }
}
