using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.ModManager.Dialogs.Replacements;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs
{
    public class ModReplacementDialogViewModelTests
    {
        public ModReplacementDialogViewModelTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();
        }

        [Fact]
        public void Constructor_ShouldIdentifyAlreadyInstalledReplacements()
        {

            var original = new ModItem { Name = "Old Mod", SteamId = "1" };
            var replacementInfo = new ModReplacementInfo { ReplacementSteamId = "2", ReplacementName = "New Mod" };
            var installed = new List<ModItem> { new ModItem { SteamId = "2" } }; // Replacement already installed
            
            var data = new List<(ModItem Original, ModReplacementInfo Replacement, long OriginalUpdate, long ReplacementUpdate, string ReplacementSteamTitle, string ReplacementSteamAuthor)>
            {
                (original, replacementInfo, 100, 200, "New Mod", "Zaljerem")
            };

            var vm = new ModReplacementDialogViewModel(data, installed);

            vm.Replacements.Should().BeEmpty();
            vm.AlreadyInstalledReplacements.Should().HaveCount(1);
            vm.HasAlreadyInstalledReplacements.Should().BeTrue();
        }

        [Fact]
        public void MultipleSuggestionsForSameMod_ShouldProduceOneRowEach()
        {
            // Two sources suggested different targets for the same original mod;
            // the analysis passes one tuple per suggestion, so the dialog shows
            // one row per (mod, suggestion) using the existing row construction.
            var original = new ModItem { Name = "Old Mod", SteamId = "1" };
            var data = new List<(ModItem Original, ModReplacementInfo Replacement, long OriginalUpdate, long ReplacementUpdate, string ReplacementSteamTitle, string ReplacementSteamAuthor)>
            {
                (original, new ModReplacementInfo { ReplacementSteamId = "2", ReplacementName = "First", Source = ReplacementSource.UseThisInsteadRepo }, 0, 0, "First", "Author A"),
                (original, new ModReplacementInfo { ReplacementSteamId = "3", ReplacementName = "Second", Source = ReplacementSource.IndexedRecords, SourceName = "Community Index (Reasons)", Reason = "Curated reason" }, 0, 0, "Second", "Author B")
            };

            var vm = new ModReplacementDialogViewModel(data, new List<ModItem>());

            var rows = vm.Replacements!;
            rows.Should().HaveCount(2);
            rows.Select(r => r.ReplacementInfo.ReplacementSteamId)
                .Should().BeEquivalentTo(new[] { "2", "3" });
            rows.Single(r => r.ReplacementInfo.ReplacementSteamId == "3")
                .ReasonDisplay.Should().Be("Curated reason");
            vm.GetSelectedReplacements().Should().HaveCount(2);
        }

        [Fact]
        public void SelectAll_ShouldUpdateSelectedCount()
        {

            var data = new List<(ModItem Original, ModReplacementInfo Replacement, long OriginalUpdate, long ReplacementUpdate, string ReplacementSteamTitle, string ReplacementSteamAuthor)>
            {
                (new ModItem(), new ModReplacementInfo { ReplacementSteamId = "1" }, 0, 0, "", ""),
                (new ModItem(), new ModReplacementInfo { ReplacementSteamId = "2" }, 0, 0, "", "")
            };
            var vm = new ModReplacementDialogViewModel(data, new List<ModItem>());

            vm.SelectAllCommand.Execute(null);

            vm.SelectedCount.Should().Be(2);
            vm.GetSelectedReplacements().Should().HaveCount(2);
        }

        private static List<(ModItem Original, ModReplacementInfo Replacement, long OriginalUpdate, long ReplacementUpdate, string ReplacementSteamTitle, string ReplacementSteamAuthor)> DataFor(
            ModReplacementInfo replacementInfo) =>
            new()
            {
                (new ModItem { Name = "Old Mod", SteamId = "1" }, replacementInfo, 0, 0, "New Mod", "Author")
            };

        [Fact]
        public void MissingPackageId_ResolvesFromDictionaryBySteamId_WithoutMutatingCache()
        {
            // SteamId carries surrounding whitespace to exercise the defensive Trim()
            // at the resolution point (GetEntryBySteamId itself does not trim).
            var replacementInfo = new ModReplacementInfo
            {
                ReplacementSteamId = " 2 ",
                ReplacementName = "New Mod",
                ReplacementModId = string.Empty
            };

            var dictionary = Substitute.For<IModDictionaryService>();
            dictionary.GetEntryBySteamId("2").Returns(new ModDictionaryEntry { PackageId = "resolved.mod" });

            var vm = new ModReplacementDialogViewModel(DataFor(replacementInfo), new List<ModItem>(), dictionary);

            vm.Replacements.Should().HaveCount(1);
            vm.Replacements![0].ReplacementPackageIdDisplay.Should().Be("resolved.mod");
            // The shared/cached replacement info must not be mutated.
            replacementInfo.ReplacementModId.Should().BeEmpty();
        }

        [Fact]
        public void MissingPackageId_InstalledModWinsOverDictionary()
        {
            var replacementInfo = new ModReplacementInfo
            {
                ReplacementSteamId = "2",
                ReplacementName = "New Mod",
                ReplacementModId = string.Empty
            };
            var installed = new List<ModItem> { new ModItem { SteamId = "2", PackageId = "installed.mod" } };

            var dictionary = Substitute.For<IModDictionaryService>();
            dictionary.GetEntryBySteamId("2").Returns(new ModDictionaryEntry { PackageId = "resolved.mod" });

            var vm = new ModReplacementDialogViewModel(DataFor(replacementInfo), installed, dictionary);

            vm.AlreadyInstalledReplacements.Should().HaveCount(1);
            vm.AlreadyInstalledReplacements[0].ReplacementPackageIdDisplay.Should().Be("installed.mod");
            // Installed mod is source 1 and wins, so the dictionary is never consulted.
            dictionary.DidNotReceive().GetEntryBySteamId(Arg.Any<string>());
        }

        [Fact]
        public void ExplicitPackageId_DictionaryNotConsulted()
        {
            var replacementInfo = new ModReplacementInfo
            {
                ReplacementSteamId = "2",
                ReplacementModId = "explicit.mod"
            };

            var dictionary = Substitute.For<IModDictionaryService>();
            dictionary.GetEntryBySteamId(Arg.Any<string>()).Returns(new ModDictionaryEntry { PackageId = "resolved.mod" });

            var vm = new ModReplacementDialogViewModel(DataFor(replacementInfo), new List<ModItem>(), dictionary);

            vm.Replacements![0].ReplacementPackageIdDisplay.Should().Be("explicit.mod");
            dictionary.DidNotReceive().GetEntryBySteamId(Arg.Any<string>());
        }

        [Fact]
        public void MissingPackageId_DictionaryReturnsNull_IsNullSafe()
        {
            var replacementInfo = new ModReplacementInfo
            {
                ReplacementSteamId = "2",
                ReplacementModId = string.Empty
            };

            var dictionary = Substitute.For<IModDictionaryService>();
            dictionary.GetEntryBySteamId("2").Returns((ModDictionaryEntry?)null);

            var vm = new ModReplacementDialogViewModel(DataFor(replacementInfo), new List<ModItem>(), dictionary);

            vm.Replacements![0].ReplacementPackageIdDisplay.Should().Be("(No Package ID)");
        }

        [Fact]
        public void FetchedSteamVersions_UsedForReplacement_OriginalStaysLocal()
        {
            var original = new ModItem
            {
                Name = "Old Mod",
                SteamId = "1",
                SupportedVersions = new List<VersionSupport> { new("1.5", VersionSource.Official) }
            };
            var replacementInfo = new ModReplacementInfo
            {
                ReplacementSteamId = "2",
                ReplacementName = "New Mod",
                ReplacementVersions = "2.0"
            };
            var data = new List<(ModItem Original, ModReplacementInfo Replacement, long OriginalUpdate, long ReplacementUpdate, string ReplacementSteamTitle, string ReplacementSteamAuthor)>
            {
                (original, replacementInfo, 0, 0, "New Mod", "Author")
            };
            var fetched = new Dictionary<string, IReadOnlyList<string>>
            {
                ["2"] = new[] { "1.7", "1.8" }
            };

            var vm = new ModReplacementDialogViewModel(data, new List<ModItem>(), fetchedVersionsBySteamId: fetched);

            var row = vm.Replacements!.Single();
            row.ReplacementVersions.Should().BeEquivalentTo(new[] { "1.7", "1.8" });
            // The original/replaced mod keeps its local versions only.
            row.OriginalVersions.Should().BeEquivalentTo(new[] { "1.5" });
        }

        [Fact]
        public void FetchedSteamVersions_KeyedByTrimmedSteamId()
        {
            var replacementInfo = new ModReplacementInfo
            {
                ReplacementSteamId = " 2 ",
                ReplacementName = "New Mod"
            };
            var fetched = new Dictionary<string, IReadOnlyList<string>>
            {
                ["2"] = new[] { "1.7" }
            };

            var vm = new ModReplacementDialogViewModel(
                DataFor(replacementInfo), new List<ModItem>(), fetchedVersionsBySteamId: fetched);

            vm.Replacements!.Single().ReplacementVersions.Should().BeEquivalentTo(new[] { "1.7" });
        }

        [Fact]
        public void ModInfoVersions_UsedWhenSteamAndInstalledEmpty()
        {
            var replacementInfo = new ModReplacementInfo
            {
                ReplacementSteamId = "2",
                ReplacementModId = string.Empty,
                ReplacementVersions = "2.0"
            };
            var dictionary = Substitute.For<IModDictionaryService>();
            dictionary.GetEntryBySteamId("2").Returns(new ModDictionaryEntry
            {
                PackageId = "resolved.mod",
                Versions = new List<string> { "4.0", "4.1" }
            });

            var vm = new ModReplacementDialogViewModel(DataFor(replacementInfo), new List<ModItem>(), dictionary);

            var row = vm.Replacements!.Single();
            row.ReplacementVersions.Should().BeEquivalentTo(new[] { "4.0", "4.1" });
            row.OriginalVersions.Should().BeEmpty();
        }
    }
}

