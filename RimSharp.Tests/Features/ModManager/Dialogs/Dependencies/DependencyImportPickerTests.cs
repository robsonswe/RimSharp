using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.MissingMods;
using RimSharp.Features.ModManager.ViewModels.Actions;
using RimSharp.Infrastructure.Mods.ModInfo;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs.Dependencies
{
    /// <summary>
    /// Verifies that the Resolve Dependencies flow reuses the proven import
    /// "missing mods" picker: resolver candidates become grouped variants,
    /// URL deps become a single variant, zero-candidate deps become unknown ids,
    /// and the picker's DownloadCommand yields the SelectedSteamIds that are
    /// queued for download.
    /// </summary>
    public class DependencyImportPickerTests
    {
        public DependencyImportPickerTests()
        {
            TestApp.InitializeTestApp();
        }

        private static (string displayName, string packageId, string steamUrl, List<string> requiredBy, IReadOnlyList<ModInfoResolutionCandidate> candidates)
            Row(string displayName, string packageId, string steamUrl = "", IReadOnlyList<ModInfoResolutionCandidate>? candidates = null)
            => (displayName, packageId, steamUrl, new List<string> { "requiring.mod" }, candidates ?? Array.Empty<ModInfoResolutionCandidate>());

        [AvaloniaFact]
        public void ResolverCandidates_BecomeVariants_DefaultFirstPublished()
        {
            var candidates = new List<ModInfoResolutionCandidate>
            {
                new() { SteamId = "111", DisplayName = "Old Version", Published = false },
                new() { SteamId = "222", DisplayName = "Current Version", Published = true },
                new() { SteamId = "333", DisplayName = "Beta", Published = true },
            };
            var data = new[] { Row("Mod", "mod.id", candidates: candidates) };

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(data);

            var group = dialog.ModGroups.Should().ContainSingle().Subject;
            group.PackageId.Should().Be("mod.id");
            group.Variants.Should().HaveCount(3);
            group.HasSelectableVariants.Should().BeTrue();
            group.SelectedVariant!.SteamId.Should().Be("222");
            group.SelectedVariant.Name.Should().Be("Current Version");
            dialog.UnknownModIds.Should().BeEmpty();
        }

        [AvaloniaFact]
        public void CandidateSteamId_IsUsed_EvenWithoutDictionaryEntry()
        {
            // Candidates built by tests (or a stale dictionary) may carry a SteamId
            // but no DictionaryEntry; the variant must still expose that SteamId.
            var candidates = new List<ModInfoResolutionCandidate>
            {
                new() { SteamId = "999", DisplayName = "Resolved", Published = true }
            };
            var data = new[] { Row("Mod", "mod.id", candidates: candidates) };

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(data);

            var group = dialog.ModGroups.Should().ContainSingle().Subject;
            group.SelectedVariant!.SteamId.Should().Be("999");
        }

        [AvaloniaFact]
        public void UrlDep_BecomesSinglePublishedVariant()
        {
            var data = new[] { Row("Mod", "mod.id", "https://steamcommunity.com/sharedfiles/filedetails/?id=42") };

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(data);

            var group = dialog.ModGroups.Should().ContainSingle().Subject;
            group.Variants.Should().ContainSingle();
            group.Variants[0].SteamId.Should().Be("42");
            group.Variants[0].IsPublished.Should().BeTrue();
            group.SelectedVariant!.SteamId.Should().Be("42");
        }

        [AvaloniaFact]
        public void ZeroCandidates_GoesToUnknownModIds()
        {
            var data = new[] { Row("Mod", "mod.id") };

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(data);

            dialog.ModGroups.Should().BeEmpty();
            dialog.UnknownModIds.Should().ContainSingle().Which.Should().Be("mod.id");
            dialog.HasUnknownMods.Should().BeTrue();
            dialog.HasKnownMods.Should().BeFalse();
        }

        [AvaloniaFact]
        public void MixedDeps_AreSplitIntoGroupsAndUnknown()
        {
            var candidates = new List<ModInfoResolutionCandidate>
            {
                new() { SteamId = "222", DisplayName = "Resolved", Published = true }
            };
            var data = new[]
            {
                Row("Url Mod", "url.mod", "https://steamcommunity.com/sharedfiles/filedetails/?id=11"),
                Row("Resolved Mod", "resolved.mod", candidates: candidates),
                Row("Unknown Mod", "unknown.mod")
            };

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(data);

            dialog.ModGroups.Should().HaveCount(2);
            dialog.ModGroups.Select(g => g.PackageId).Should().BeEquivalentTo(new[] { "url.mod", "resolved.mod" });
            dialog.UnknownModIds.Should().ContainSingle().Which.Should().Be("unknown.mod");
        }

        [AvaloniaFact]
        public void DownloadCommand_YieldsSelectedSteamIds_ForChosenVariants()
        {
            var candidatesA = new List<ModInfoResolutionCandidate>
            {
                new() { SteamId = "111", DisplayName = "First", Published = true },
                new() { SteamId = "222", DisplayName = "Second", Published = true },
            };
            var candidatesB = new List<ModInfoResolutionCandidate>
            {
                new() { SteamId = "333", DisplayName = "Only", Published = true }
            };
            var data = new[]
            {
                Row("Mod A", "a.mod", candidates: candidatesA),
                Row("Mod B", "b.mod", candidates: candidatesB)
            };

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(data);
            dialog.DownloadCommand.CanExecute(null).Should().BeTrue();

            // User overrides the default on group A to the second variant.
            var groupA = dialog.ModGroups.Single(g => g.PackageId == "a.mod");
            groupA.SelectedVariant = groupA.Variants.Single(v => v.SteamId == "222");

            dialog.DownloadCommand.Execute(null);

            dialog.DialogResult.Should().NotBeNull();
            dialog.DialogResult!.Result.Should().Be(MissingModSelectionResult.Download);
            dialog.DialogResult.SelectedSteamIds.Should().BeEquivalentTo(new[] { "222", "333" });
        }

        [AvaloniaFact]
        public void DependencyBuiltGroups_CarryRequiredByNames()
        {
            var candidates = new List<ModInfoResolutionCandidate>
            {
                new() { SteamId = "222", DisplayName = "Resolved", Published = true }
            };
            // Row() defaults requiredBy to ["requiring.mod"].
            var data = new[]
            {
                Row("Mod A", "a.mod", candidates: candidates),
                Row("Url Mod", "b.mod", "https://steamcommunity.com/sharedfiles/filedetails/?id=11")
            };

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(data);

            var groupA = dialog.ModGroups.Single(g => g.PackageId == "a.mod");
            groupA.HasRequiredBy.Should().BeTrue();
            groupA.RequiredBy.Should().Contain("requiring.mod");
            groupA.RequiredByDisplay.Should().Be("requiring.mod");

            var groupB = dialog.ModGroups.Single(g => g.PackageId == "b.mod");
            groupB.HasRequiredBy.Should().BeTrue();
            groupB.RequiredBy.Should().Contain("requiring.mod");
        }

        [AvaloniaFact]
        public void Group_WithEmptyRequiredBy_HasNoRequiredByFlag()
        {
            var candidates = new List<ModInfoResolutionCandidate>
            {
                new() { SteamId = "222", DisplayName = "Resolved", Published = true }
            };
            var data = new[]
            {
                (displayName: "Mod", packageId: "mod.id", steamUrl: "", requiredBy: new List<string>(), candidates: (IReadOnlyList<ModInfoResolutionCandidate>)candidates)
            };

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(data);

            var group = dialog.ModGroups.Should().ContainSingle().Subject;
            group.HasRequiredBy.Should().BeFalse();
            group.RequiredBy.Should().BeEmpty();
            group.RequiredByDisplay.Should().BeEmpty();
        }

        [AvaloniaFact]
        public void DeclaredPublishedPfid_IsAutoSelected()
        {
            var candidates = new List<ModInfoResolutionCandidate>
            {
                new() { SteamId = "111", DisplayName = "First", Published = true },
                new() { SteamId = "222", DisplayName = "Declared", Published = true },
                new() { SteamId = "333", DisplayName = "Third", Published = true },
            };
            var data = new[] { Row("Mod", "mod.id", "steam://url/CommunityFilePage/222", candidates) };

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(data);

            var group = dialog.ModGroups.Should().ContainSingle().Subject;
            group.Variants.Should().HaveCount(3);
            group.SelectedVariant!.SteamId.Should().Be("222");
        }

        [AvaloniaFact]
        public void DeclaredUnpublishedVariant_IsNotAutoSelected_FallsBackToFirstPublished()
        {
            var candidates = new List<ModInfoResolutionCandidate>
            {
                new() { SteamId = "111", DisplayName = "Declared Unpublished", Published = false },
                new() { SteamId = "222", DisplayName = "First Published", Published = true },
                new() { SteamId = "333", DisplayName = "Second Published", Published = true },
            };
            var data = new[] { Row("Mod", "mod.id", "steam://url/CommunityFilePage/111", candidates) };

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(data);

            var group = dialog.ModGroups.Should().ContainSingle().Subject;
            group.SelectedVariant!.SteamId.Should().Be("222");
            group.SelectedVariant.IsPublished.Should().BeTrue();
        }

        [AvaloniaFact]
        public void DeclaredPfidAbsent_FallsBackToFirstPublished()
        {
            var candidates = new List<ModInfoResolutionCandidate>
            {
                new() { SteamId = "111", DisplayName = "First", Published = true },
                new() { SteamId = "222", DisplayName = "Second", Published = true },
            };
            var data = new[] { Row("Mod", "mod.id", "steam://url/CommunityFilePage/999", candidates) };

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(data);

            var group = dialog.ModGroups.Should().ContainSingle().Subject;
            group.SelectedVariant!.SteamId.Should().Be("111");
        }

        [AvaloniaFact]
        public void AuthorsFromCandidate_ReachesVariantViewModel()
        {
            var candidates = new List<ModInfoResolutionCandidate>
            {
                new()
                {
                    SteamId = "222",
                    DisplayName = "Resolved",
                    Published = true,
                    Author = "Jane Doe",
                    DictionaryEntry = new RimSharp.Shared.Models.ModDictionaryEntry
                    {
                        PackageId = "mod.id", SteamId = "222", Authors = ""
                    }
                }
            };
            var data = new[] { Row("Mod", "mod.id", candidates: candidates) };

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(data);

            var group = dialog.ModGroups.Should().ContainSingle().Subject;
            group.Variants.Should().ContainSingle();
            group.Variants[0].Authors.Should().Be("Jane Doe");
        }

        [AvaloniaFact]
        public void MissingAuthor_ShowsUnknown_InsteadOfBlank()
        {
            // A candidate with no resolved author (e.g. a fallback whose Steam
            // persona lookup failed) must still show "Unknown" so the view does
            // not hide the author line.
            var candidates = new List<ModInfoResolutionCandidate>
            {
                new()
                {
                    SteamId = "222",
                    DisplayName = "Resolved",
                    Published = true,
                    DictionaryEntry = new RimSharp.Shared.Models.ModDictionaryEntry
                    {
                        PackageId = "mod.id", SteamId = "222", Authors = ""
                    }
                }
            };
            var data = new[] { Row("Mod", "mod.id", candidates: candidates) };

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(data);

            var group = dialog.ModGroups.Should().ContainSingle().Subject;
            group.Variants.Should().ContainSingle();
            group.Variants[0].Authors.Should().Be("Unknown");
        }

        [AvaloniaFact]
        public void SteamProtocolUrl_IsParsedForDeclaredId_NotTreatedAsSingleVariant()
        {
            var candidates = new List<ModInfoResolutionCandidate>
            {
                new() { SteamId = "111", DisplayName = "First", Published = true },
                new() { SteamId = "222", DisplayName = "Second", Published = true },
                new() { SteamId = "333", DisplayName = "Declared", Published = true },
            };
            var data = new[] { Row("Mod", "mod.id", "steam://url/CommunityFilePage/333", candidates) };

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(data);

            var group = dialog.ModGroups.Should().ContainSingle().Subject;
            // All candidates become variants; the steam:// URL is not a single-variant URL.
            group.Variants.Should().HaveCount(3);
            group.SelectedVariant!.SteamId.Should().Be("333");
        }

        [AvaloniaFact]
        public void SteamProtocolShortUrl_IsParsedForDeclaredId()
        {
            var candidates = new List<ModInfoResolutionCandidate>
            {
                new() { SteamId = "111", DisplayName = "First", Published = true },
                new() { SteamId = "12345", DisplayName = "Declared", Published = true },
            };
            var data = new[] { Row("Mod", "mod.id", "steam://12345", candidates) };

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(data);

            var group = dialog.ModGroups.Should().ContainSingle().Subject;
            group.Variants.Should().HaveCount(2);
            group.SelectedVariant!.SteamId.Should().Be("12345");
        }
    }
}
