using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.Filter;
using RimSharp.Shared.Models;
using Xunit;
using VersionFilterMode = RimSharp.Features.ModManager.Dialogs.Filter.TriStateFilterItemViewModel.FilterMode;
using TagFilterMode = RimSharp.Features.ModManager.Dialogs.Filter.TriStateFilterItemViewModel.FilterMode;

namespace RimSharp.Tests.Features.ModManager.Dialogs
{
    public class ModFilterDialogViewModelTests
    {
        public ModFilterDialogViewModelTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();
        }

        [Fact]
        public void ApplyFilters_ShouldPopulateCurrentCriteria()
        {

            var criteria = new ModFilterCriteria { SearchText = "test" };
            var vm = new ModFilterDialogViewModel(criteria, new[] { "1.5" }, new[] { "tag1" }, new[] { "Author1" });
            vm.IsFavoriteFilter = true;
            vm.AvailableModTypes.First().IsSelected = true;

            vm.ApplyCommand.Execute(null);

            vm.DialogResult.Should().Be(ModFilterDialogResult.Apply);
            vm.CurrentCriteria!.SearchText.Should().Be("test");
            vm.CurrentCriteria.IsFavoriteFilter.Should().BeTrue();
            vm.CurrentCriteria.SelectedModTypes.Should().HaveCount(1);
        }

        [Fact]
        public void ClearFilters_ShouldResetProperties()
        {

            var criteria = new ModFilterCriteria { SearchText = "test" };
            var vm = new ModFilterDialogViewModel(criteria, new[] { "1.5" }, new[] { "tag1" }, new[] { "Author1" });
            vm.IsFavoriteFilter = true;

            vm.ClearCommand.Execute(null);

            vm.DialogResult.Should().Be(ModFilterDialogResult.Clear);
            vm.SearchText.Should().BeEmpty();
            vm.IsFavoriteFilter.Should().BeNull();
            vm.CurrentCriteria!.SearchText.Should().BeEmpty();
        }

        [Fact]
        public void Constructor_ShouldRestoreVersionModesFromCriteria()
        {

            var criteria = new ModFilterCriteria
            {
                IncludedSupportedVersions = new List<string> { "1.5" },
                ExcludedSupportedVersions = new List<string> { "1.4" }
            };

            var vm = new ModFilterDialogViewModel(criteria, new[] { "1.4", "1.5", "1.6" }, new string[0], new string[0]);

            vm.AvailableVersions.Single(v => v.Item == "1.5").Mode
                .Should().Be(VersionFilterMode.Include);
            vm.AvailableVersions.Single(v => v.Item == "1.4").Mode
                .Should().Be(VersionFilterMode.Exclude);
            vm.AvailableVersions.Single(v => v.Item == "1.6").Mode
                .Should().Be(VersionFilterMode.Any);
        }

        [Fact]
        public void SetFilterModeCommand_ShouldUpdateVersionItemMode()
        {

            var vm = new ModFilterDialogViewModel(new ModFilterCriteria(), new[] { "1.5" }, new string[0], new string[0]);

            vm.SetFilterModeCommand.Execute("Version|1.5|Include");

            vm.AvailableVersions.Single().Mode.Should().Be(VersionFilterMode.Include);
        }

        [Fact]
        public void Constructor_ShouldRestoreTagModesFromCriteria()
        {

            var criteria = new ModFilterCriteria
            {
                IncludedTags = new List<string> { "Library" },
                ExcludedTags = new List<string> { "Performance" }
            };

            var vm = new ModFilterDialogViewModel(criteria, new string[0], new[] { "Library", "Performance", "UI" }, new string[0]);

            vm.AvailableTags.Single(t => t.Item == "Library").Mode
                .Should().Be(TagFilterMode.Include);
            vm.AvailableTags.Single(t => t.Item == "Performance").Mode
                .Should().Be(TagFilterMode.Exclude);
            vm.AvailableTags.Single(t => t.Item == "UI").Mode
                .Should().Be(TagFilterMode.Any);
        }

        [Fact]
        public void SetFilterModeCommand_ShouldUpdateTagItemMode()
        {

            var vm = new ModFilterDialogViewModel(new ModFilterCriteria(), new string[0], new[] { "Library" }, new string[0]);

            vm.SetFilterModeCommand.Execute("Tag|Library|Exclude");

            vm.AvailableTags.Single().Mode.Should().Be(TagFilterMode.Exclude);
        }

        [Fact]
        public void ApplyFilters_ShouldMapVersionModesIntoCriteria()
        {

            var vm = new ModFilterDialogViewModel(new ModFilterCriteria(), new[] { "1.4", "1.5" }, new string[0], new string[0]);
            vm.SetFilterModeCommand.Execute("Version|1.5|Include");
            vm.SetFilterModeCommand.Execute("Version|1.4|Exclude");

            vm.ApplyCommand.Execute(null);

            vm.CurrentCriteria!.IncludedSupportedVersions.Should().BeEquivalentTo("1.5");
            vm.CurrentCriteria.ExcludedSupportedVersions.Should().BeEquivalentTo("1.4");
        }

        [Fact]
        public void ApplyFilters_ShouldMapTagModesIntoCriteria()
        {

            var vm = new ModFilterDialogViewModel(new ModFilterCriteria(), new string[0], new[] { "Library", "Performance" }, new string[0]);
            vm.SetFilterModeCommand.Execute("Tag|Library|Include");
            vm.SetFilterModeCommand.Execute("Tag|Performance|Exclude");

            vm.ApplyCommand.Execute(null);

            vm.CurrentCriteria!.IncludedTags.Should().BeEquivalentTo("Library");
            vm.CurrentCriteria.ExcludedTags.Should().BeEquivalentTo("Performance");
        }

        [Fact]
        public void ClearFilters_ShouldResetVersionModesToAny()
        {

            var criteria = new ModFilterCriteria
            {
                IncludedSupportedVersions = new List<string> { "1.5" }
            };
            var vm = new ModFilterDialogViewModel(criteria, new[] { "1.5" }, new string[0], new string[0]);

            vm.ClearCommand.Execute(null);

            vm.AvailableVersions.Single().Mode.Should().Be(VersionFilterMode.Any);
            vm.CurrentCriteria!.IncludedSupportedVersions.Should().BeEmpty();
        }

        [Fact]
        public void ClearFilters_ShouldResetTagModesToAny()
        {

            var criteria = new ModFilterCriteria
            {
                IncludedTags = new List<string> { "Library" }
            };
            var vm = new ModFilterDialogViewModel(criteria, new string[0], new[] { "Library" }, new string[0]);

            vm.ClearCommand.Execute(null);

            vm.AvailableTags.Single().Mode.Should().Be(TagFilterMode.Any);
            vm.CurrentCriteria!.IncludedTags.Should().BeEmpty();
        }
    }
}

