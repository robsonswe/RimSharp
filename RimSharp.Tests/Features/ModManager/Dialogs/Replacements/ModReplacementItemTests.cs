using System;
using System.Collections.Generic;
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

        [Theory]
        [InlineData(ReplacementSource.Database, "RimSharpDB")]
        [InlineData(ReplacementSource.UseThisInstead, "the installed 'Use This Instead' mod")]
        [InlineData(ReplacementSource.UseThisInsteadRepo, "'Use This Instead' (GitHub snapshot)")]
        public void SourceDisplayName_MapsFixedSources(ReplacementSource source, string expected)
        {
            CreateItem(source).SourceDisplayName.Should().Be(expected);
        }

        [Fact]
        public void SourceDisplayName_LocalFile_UsesRegistrySourceName()
        {
            var item = new ModReplacementItem(new ModItem(), new ModReplacementInfo
            {
                Source = ReplacementSource.LocalFile,
                SourceName = "My Local Rules"
            });

            item.SourceDisplayName.Should().Be("My Local Rules");
        }

        [Fact]
        public void SourceDisplayName_IndexedRecords_UsesRegistrySourceName()
        {
            var item = new ModReplacementItem(new ModItem(), new ModReplacementInfo
            {
                Source = ReplacementSource.IndexedRecords,
                SourceName = "Community Index (Reasons)"
            });

            item.SourceDisplayName.Should().Be("Community Index (Reasons)");
        }

        [Fact]
        public void ReasonDisplay_ShownOnlyWhenPresent()
        {
            var withReason = new ModReplacementItem(new ModItem(), new ModReplacementInfo
            {
                Reason = "Better performance, same features."
            });
            withReason.HasReason.Should().BeTrue();
            withReason.ReasonDisplay.Should().Be("Better performance, same features.");

            var withoutReason = new ModReplacementItem(new ModItem(), new ModReplacementInfo());
            withoutReason.HasReason.Should().BeFalse();
            withoutReason.ReasonDisplay.Should().BeNull();
        }

        [Fact]
        public void ReplacementPackageIdDisplay_PrefersExplicitId_ThenResolved_ThenPlaceholder()
        {
            // Explicit ReplacementModId wins even when a resolved value is present.
            var withExplicit = new ModReplacementItem(new ModItem(), new ModReplacementInfo
            {
                ReplacementModId = "explicit.mod"
            })
            {
                ResolvedReplacementPackageId = "resolved.mod"
            };
            withExplicit.ReplacementPackageIdDisplay.Should().Be("explicit.mod");

            // Resolved value is used when the explicit id is blank; values are trimmed.
            var withResolved = new ModReplacementItem(new ModItem(), new ModReplacementInfo
            {
                ReplacementModId = "   "
            })
            {
                ResolvedReplacementPackageId = "  resolved.mod  "
            };
            withResolved.ReplacementPackageIdDisplay.Should().Be("resolved.mod");

            // Placeholder when neither source supplies a value.
            var withNeither = new ModReplacementItem(new ModItem(), new ModReplacementInfo());
            withNeither.ReplacementPackageIdDisplay.Should().Be("(No Package ID)");
        }

        [Fact]
        public void ReplacementVersions_PreferFetchedSteamVersions()
        {
            // Steam-fetched versions are authoritative and beat every other tier.
            var item = new ModReplacementItem(
                new ModItem
                {
                    SupportedVersions = new List<VersionSupport> { new("1.6", VersionSource.Official) }
                },
                new ModReplacementInfo { ReplacementVersions = "2.0, 2.1" })
            {
                InstalledReplacement = new ModItem
                {
                    SupportedVersions = new List<VersionSupport> { new("3.0", VersionSource.Official) }
                },
                ModInfoReplacementVersions = new[] { "4.0" },
                FetchedReplacementVersions = new[] { "1.7", "1.8" }
            };

            item.ReplacementVersions.Should().BeEquivalentTo(new[] { "1.7", "1.8" });
            item.HasFetchedReplacementVersions.Should().BeTrue();
            item.HasReplacementVersions.Should().BeTrue();
        }

        [Fact]
        public void OriginalVersions_UnaffectedByFetchedReplacementVersions()
        {
            // Explicit asymmetry: only the replacement side uses Steam data;
            // the original/replaced mod keeps its local versions.
            var item = new ModReplacementItem(
                new ModItem
                {
                    SupportedVersions = new List<VersionSupport>
                    {
                        new("1.5", VersionSource.Official),
                        new("1.6", VersionSource.Official)
                    }
                },
                new ModReplacementInfo { ReplacementVersions = "2.0" })
            {
                FetchedReplacementVersions = new[] { "1.7", "1.8" },
                ModInfoReplacementVersions = new[] { "4.0" }
            };

            item.OriginalVersions.Should().BeEquivalentTo(new[] { "1.5", "1.6" });
            item.ReplacementVersions.Should().BeEquivalentTo(new[] { "1.7", "1.8" });
        }

        [Fact]
        public void ReplacementVersions_FallbackChain_InstalledThenModInfoThenCurated()
        {
            // Fetched empty -> installed wins over ModInfo and curated.
            var withInstalled = new ModReplacementItem(
                new ModItem(),
                new ModReplacementInfo { ReplacementVersions = "2.0" })
            {
                InstalledReplacement = new ModItem
                {
                    SupportedVersions = new List<VersionSupport> { new("3.0", VersionSource.Official) }
                },
                FetchedReplacementVersions = Array.Empty<string>(),
                ModInfoReplacementVersions = new[] { "4.0" }
            };
            withInstalled.ReplacementVersions.Should().BeEquivalentTo(new[] { "3.0" });

            // No installed mod -> ModInfo wins over curated.
            var withModInfo = new ModReplacementItem(
                new ModItem(),
                new ModReplacementInfo { ReplacementVersions = "2.0" })
            {
                ModInfoReplacementVersions = new[] { "4.0" }
            };
            withModInfo.ReplacementVersions.Should().BeEquivalentTo(new[] { "4.0" });

            // No fetched/installed/ModInfo -> curated final fallback.
            var curatedOnly = new ModReplacementItem(
                new ModItem(),
                new ModReplacementInfo { ReplacementVersions = "2.0, 2.1" });
            curatedOnly.ReplacementVersions.Should().BeEquivalentTo(new[] { "2.0", "2.1" });

            // Nothing -> empty.
            var empty = new ModReplacementItem(new ModItem(), new ModReplacementInfo());
            empty.ReplacementVersions.Should().BeEmpty();
            empty.HasReplacementVersions.Should().BeFalse();
        }
    }
}