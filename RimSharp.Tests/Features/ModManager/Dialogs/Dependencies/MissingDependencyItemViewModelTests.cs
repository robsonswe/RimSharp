using System.Collections.Generic;
using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.Dependencies;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs.Dependencies
{
    public class MissingDependencyItemViewModelTests
    {
        [Fact]
        public void Constructor_ShouldSetDisplayNameToPackageIdWhenDisplayNameIsNull()
        {
            var vm = new MissingDependencyItemViewModel(null, "my.package.id", null, null);

            vm.DisplayName.Should().Be("my.package.id");
        }

        [Fact]
        public void Constructor_ShouldUseProvidedDisplayName()
        {
            var vm = new MissingDependencyItemViewModel("My Mod", "my.package.id", null, null);

            vm.DisplayName.Should().Be("My Mod");
        }

        [Fact]
        public void Constructor_WhenBothDisplayNameAndPackageIdAreNull_ShouldFallbackToUnknown()
        {
            var vm = new MissingDependencyItemViewModel(null, null!, null, null);

            vm.DisplayName.Should().Be("Unknown Dependency");
        }

        [Fact]
        public void Constructor_ShouldExtractSteamIdFromWorkshopUrl()
        {
            var vm = new MissingDependencyItemViewModel("Mod", "mod.id",
                "https://steamcommunity.com/sharedfiles/filedetails/?id=12345678", null);

            vm.SteamId.Should().Be("12345678");
        }

        [Fact]
        public void Constructor_WhenUrlHasNoId_ShouldSetSteamIdToNull()
        {
            var vm = new MissingDependencyItemViewModel("Mod", "mod.id", "https://example.com/notsteam", null);

            vm.SteamId.Should().BeNull();
        }

        [Fact]
        public void Constructor_WhenUrlIsNull_ShouldSetSteamIdToNull()
        {
            var vm = new MissingDependencyItemViewModel("Mod", "mod.id", null, null);

            vm.SteamId.Should().BeNull();
        }

        [Fact]
        public void IsSelectable_WhenHasSteamId_ShouldBeTrue()
        {
            var vm = new MissingDependencyItemViewModel("Mod", "mod.id",
                "https://steamcommunity.com/sharedfiles/filedetails/?id=12345678", null);

            vm.IsSelectable.Should().BeTrue();
        }

        [Fact]
        public void IsSelectable_WhenNoSteamId_ShouldBeFalse()
        {
            var vm = new MissingDependencyItemViewModel("Mod", "mod.id", null, null);

            vm.IsSelectable.Should().BeFalse();
        }

        [Fact]
        public void IsSelected_DefaultsToTrueWhenSelectable()
        {
            var vm = new MissingDependencyItemViewModel("Mod", "mod.id",
                "https://steamcommunity.com/sharedfiles/filedetails/?id=12345678", null);

            vm.IsSelected.Should().BeTrue();
        }

        [Fact]
        public void IsSelected_DefaultsToFalseWhenNotSelectable()
        {
            var vm = new MissingDependencyItemViewModel("Mod", "mod.id", null, null);

            vm.IsSelected.Should().BeFalse();
        }

        [Fact]
        public void IsSelected_CannotBeSetToTrueWhenNotSelectable()
        {
            var vm = new MissingDependencyItemViewModel("Mod", "mod.id", null, null);

            vm.IsSelected = true;

            vm.IsSelected.Should().BeFalse();
        }

        [Fact]
        public void IsSelected_CanBeSetToFalseWhenSelectable()
        {
            var vm = new MissingDependencyItemViewModel("Mod", "mod.id",
                "https://steamcommunity.com/sharedfiles/filedetails/?id=12345678", null);

            vm.IsSelected = false;

            vm.IsSelected.Should().BeFalse();
        }

        [Fact]
        public void Constructor_ShouldPopulateRequiredByDisplay()
        {
            var requiredBy = new List<string> { "mod.a", "mod.b" };

            var vm = new MissingDependencyItemViewModel("Mod", "mod.id", null, requiredBy);

            vm.RequiredByDisplay.Should().HaveCount(2);
            vm.RequiredByDisplay.Should().Contain("mod.a");
            vm.RequiredByDisplay.Should().Contain("mod.b");
        }

        [Fact]
        public void Constructor_WhenRequiredByIsNull_ShouldUseEmptyList()
        {
            var vm = new MissingDependencyItemViewModel("Mod", "mod.id", null, null);

            vm.RequiredByDisplay.Should().BeEmpty();
        }

        [Fact]
        public void SteamWorkshopUrl_ShouldBeSetFromConstructor()
        {
            var url = "https://steamcommunity.com/sharedfiles/filedetails/?id=12345678";
            var vm = new MissingDependencyItemViewModel("Mod", "mod.id", url, null);

            vm.SteamWorkshopUrl.Should().Be(url);
        }

        [Fact]
        public void SteamWorkshopUrl_WhenNullProvided_ShouldBeEmptyString()
        {
            var vm = new MissingDependencyItemViewModel("Mod", "mod.id", null, null);

            vm.SteamWorkshopUrl.Should().BeEmpty();
        }
    }
}
