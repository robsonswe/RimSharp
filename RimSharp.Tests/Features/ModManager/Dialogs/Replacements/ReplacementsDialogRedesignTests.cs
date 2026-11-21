using System.Collections.Generic;
using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.Replacements;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs.Replacements
{
    public class ReplacementsDialogRedesignTests
    {
        public ReplacementsDialogRedesignTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();
        }

        private static ModReplacementItem CreateItem() => new(
            new ModItem
            {
                PackageId = "orig.mod",
                SteamId = "111",
                SupportedVersions = new List<VersionSupport>
                {
                    new("1.6", VersionSource.Official),
                    new("1.5", VersionSource.Official),
                    new("1.6", VersionSource.Official),
                    new("  ", VersionSource.Official)
                }
            },
            new ModReplacementInfo
            {
                ReplacementModId = "repl.mod",
                ReplacementSteamId = "222",
                ReplacementVersions = "2.0, 2.1",
                Source = ReplacementSource.IndexedRecords
            });

        [Fact]
        public void OriginalVersions_AreDistinctAndNonBlank()
        {
            var item = CreateItem();

            item.OriginalVersions.Should().BeEquivalentTo(new[] { "1.6", "1.5" });
            item.HasOriginalVersions.Should().BeTrue();
        }

        [Fact]
        public void ReplacementVersions_PreferInstalledThenFallBackToCurated()
        {
            // With no Steam/ModInfo data, the locally installed replacement's
            // versions win over the curated rule list.
            var item = CreateItem();
            item.InstalledReplacement = new ModItem
            {
                SupportedVersions = new List<VersionSupport> { new("3.0", VersionSource.Official) }
            };
            item.ReplacementVersions.Should().BeEquivalentTo(new[] { "3.0" });
            item.HasReplacementVersions.Should().BeTrue();

            // Without an installed mod, the curated rule list is the final fallback.
            var curatedOnly = CreateItem();
            curatedOnly.ReplacementVersions.Should().BeEquivalentTo(new[] { "2.0", "2.1" });

            var empty = new ModReplacementItem(new ModItem(), new ModReplacementInfo());
            empty.ReplacementVersions.Should().BeEmpty();
            empty.HasReplacementVersions.Should().BeFalse();
        }

        [Fact]
        public void PackageIdDisplays_SteamFlags_SourceAndBadge()
        {
            var item = CreateItem();

            item.OriginalPackageIdDisplay.Should().Be("orig.mod");
            item.ReplacementPackageIdDisplay.Should().Be("repl.mod");
            item.HasOriginalSteamId.Should().BeTrue();
            item.HasReplacementSteamId.Should().BeTrue();
            item.IsIndexedOrDbSource.Should().BeTrue();
            item.TargetBadgeText.Should().Be("Replacement");

            item.ReplacementAlreadyInstalled = true;
            item.TargetBadgeText.Should().Be("Already Installed");

            var blank = new ModReplacementItem(new ModItem(), new ModReplacementInfo
            {
                Source = ReplacementSource.UseThisInstead
            });
            blank.OriginalPackageIdDisplay.Should().Be("(No Package ID)");
            blank.ReplacementPackageIdDisplay.Should().Be("(No Package ID)");
            blank.HasOriginalSteamId.Should().BeFalse();
            blank.HasReplacementSteamId.Should().BeFalse();
            blank.IsIndexedOrDbSource.Should().BeFalse();
        }

        private static ModReplacementDialogViewModel CreateViewModel() =>
            new(
                new List<(ModItem, ModReplacementInfo, long, long, string, string)>
                {
                    (new ModItem { Name = "Old" }, new ModReplacementInfo { ReplacementSteamId = "2" }, 0, 0, "", ""),
                    (new ModItem { Name = "Old2" }, new ModReplacementInfo { ReplacementSteamId = "3" }, 0, 0, "", "")
                },
                new List<ModItem> { new() { SteamId = "3" } });

        [Fact]
        public void FilterState_DrivesSectionVisibilityAndLabels()
        {
            var vm = CreateViewModel();

            vm.ActiveFilter.Should().Be(ReplacementFilterKind.All);
            vm.IsFilterAll.Should().BeTrue();
            vm.RegularCount.Should().Be(1);
            vm.AlreadyInstalledCount.Should().Be(1);
            vm.TotalCount.Should().Be(2);
            vm.FilterAllLabel.Should().Be("All (2)");
            vm.FilterToDownloadLabel.Should().Be("To Download (1)");
            vm.FilterInstalledLabel.Should().Be("Already Installed (1)");
            vm.ShowRegularSection.Should().BeTrue();
            vm.ShowInstalledSection.Should().BeTrue();
            vm.HasNoVisibleItems.Should().BeFalse();

            vm.SetFilterCommand.Execute("ToDownload");
            vm.IsFilterToDownload.Should().BeTrue();
            vm.ShowRegularSection.Should().BeTrue();
            vm.ShowInstalledSection.Should().BeFalse();

            vm.SetFilterCommand.Execute("AlreadyInstalled");
            vm.IsFilterAlreadyInstalled.Should().BeTrue();
            vm.ShowRegularSection.Should().BeFalse();
            vm.ShowInstalledSection.Should().BeTrue();

            vm.SetFilterCommand.Execute("All");
            vm.IsFilterAll.Should().BeTrue();
            vm.ShowRegularSection.Should().BeTrue();
            vm.ShowInstalledSection.Should().BeTrue();
        }

        [Fact]
        public void FooterComputedProperties_FollowSelection()
        {
            var vm = CreateViewModel();

            vm.SelectedCount.Should().Be(1);
            vm.HasSelection.Should().BeTrue();
            vm.SelectedSummaryText.Should().Be("1 replacement selected");
            vm.DownloadButtonText.Should().Be("Download Selected (1)");

            vm.SelectAllCommand.Execute(null);
            vm.SelectedSummaryText.Should().Be("1 replacement selected"); // only regular rows are selectable

            vm.SelectNoneCommand.Execute(null);
            vm.SelectedCount.Should().Be(0);
            vm.HasSelection.Should().BeFalse();
            vm.SelectedSummaryText.Should().Be("0 replacements selected");
            vm.DownloadButtonText.Should().Be("Download Selected (0)");
        }
    }
}
