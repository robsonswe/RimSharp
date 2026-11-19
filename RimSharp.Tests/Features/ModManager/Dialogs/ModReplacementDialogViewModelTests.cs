using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.Replacements;
using RimSharp.Shared.Models;
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
    }
}

