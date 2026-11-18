using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.Replacements;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs.Replacements
{
    public class ModReplacementItemTests
    {
        private static ModReplacementItem CreateItem(ReplacementSource source) =>
            new(new ModItem(), new ModReplacementInfo
            {
                ModId = "orig.mod",
                ReplacementModId = "repl.mod",
                ReplacementVersions = "1.5, 1.6",
                Source = source
            });

        [Theory]
        [InlineData(ReplacementSource.Database, "RimSharpDB")]
        [InlineData(ReplacementSource.UseThisInstead, "the installed 'Use This Instead' mod")]
        [InlineData(ReplacementSource.UseThisInsteadRepo, "'Use This Instead' (GitHub snapshot)")]
        public void ReplacementSupportedVersions_CarryExternalSourceName(ReplacementSource source, string expectedName)
        {
            var versions = CreateItem(source).ReplacementSupportedVersions;

            versions.Should().HaveCount(2);
            versions.Should().OnlyContain(v => v.Source == VersionSource.External && v.Unofficial);
            versions.Should().OnlyContain(v => v.ExternalSourceName == expectedName);
        }

        [Fact]
        public void ReplacementSupportedVersions_UnknownSource_LeavesNameNull()
        {
            var versions = CreateItem((ReplacementSource)999).ReplacementSupportedVersions;

            versions.Should().HaveCount(2);
            versions.Should().OnlyContain(v => v.Source == VersionSource.External && v.ExternalSourceName == null);
        }

        [Fact]
        public void ReplacementSupportedVersions_NoReplacementVersions_ReturnsEmpty()
        {
            var item = new ModReplacementItem(new ModItem(),
                new ModReplacementInfo { ModId = "orig.mod", ReplacementVersions = "" });

            item.ReplacementSupportedVersions.Should().BeEmpty();
        }
    }
}