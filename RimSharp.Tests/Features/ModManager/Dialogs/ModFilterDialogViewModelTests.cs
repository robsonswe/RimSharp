using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.Filter;
using RimSharp.Shared.Models;
using Xunit;

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
                .Should().Be(VersionFilterItemViewModel.VersionMode.Include);
            vm.AvailableVersions.Single(v => v.Item == "1.4").Mode
                .Should().Be(VersionFilterItemViewModel.VersionMode.Exclude);
            vm.AvailableVersions.Single(v => v.Item == "1.6").Mode
                .Should().Be(VersionFilterItemViewModel.VersionMode.Any);
        }

        [Fact]
        public void SetVersionModeCommand_ShouldUpdateItemMode()
        {

            var vm = new ModFilterDialogViewModel(new ModFilterCriteria(), new[] { "1.5" }, new string[0], new string[0]);

            vm.SetVersionModeCommand.Execute("1.5|Include");

            vm.AvailableVersions.Single().Mode.Should().Be(VersionFilterItemViewModel.VersionMode.Include);
        }

        [Fact]
        public void ApplyFilters_ShouldMapVersionModesIntoCriteria()
        {

            var vm = new ModFilterDialogViewModel(new ModFilterCriteria(), new[] { "1.4", "1.5" }, new string[0], new string[0]);
            vm.SetVersionModeCommand.Execute("1.5|Include");
            vm.SetVersionModeCommand.Execute("1.4|Exclude");

            vm.ApplyCommand.Execute(null);

            vm.CurrentCriteria!.IncludedSupportedVersions.Should().BeEquivalentTo("1.5");
            vm.CurrentCriteria.ExcludedSupportedVersions.Should().BeEquivalentTo("1.4");
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

            vm.AvailableVersions.Single().Mode.Should().Be(VersionFilterItemViewModel.VersionMode.Any);
            vm.CurrentCriteria!.IncludedSupportedVersions.Should().BeEmpty();
        }
    }
}

