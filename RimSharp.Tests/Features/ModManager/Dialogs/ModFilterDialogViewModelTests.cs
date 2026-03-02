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
    }
}

