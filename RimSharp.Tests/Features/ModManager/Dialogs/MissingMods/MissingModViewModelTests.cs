using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.MissingMods;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs.MissingMods
{
    public class MissingModGroupViewModelTests
    {
        [Fact]
        public void Constructor_ShouldSetPackageId()
        {
            var vm = new MissingModGroupViewModel("test.mod");

            vm.PackageId.Should().Be("test.mod");
        }

        [Fact]
        public void Variants_ShouldBeEmptyByDefault()
        {
            var vm = new MissingModGroupViewModel("test.mod");

            vm.Variants.Should().BeEmpty();
        }

        [Fact]
        public void SelectedVariant_ShouldBeNullByDefault()
        {
            var vm = new MissingModGroupViewModel("test.mod");

            vm.SelectedVariant.Should().BeNull();
        }

        [Fact]
        public void IsSelectionMade_WhenSelectedVariantIsNull_ShouldBeFalse()
        {
            var vm = new MissingModGroupViewModel("test.mod");

            vm.IsSelectionMade.Should().BeFalse();
        }

        [Fact]
        public void IsSelectionMade_WhenSelectedVariantIsSet_ShouldBeTrue()
        {
            var vm = new MissingModGroupViewModel("test.mod");
            var variant = new MissingModVariantViewModel(new ModDictionaryEntry { Name = "Test", SteamId = "123", Published = true });

            vm.SelectedVariant = variant;

            vm.IsSelectionMade.Should().BeTrue();
        }

        [Fact]
        public void SelectedVariant_SetProperty_ShouldRaisePropertyChanged()
        {
            var vm = new MissingModGroupViewModel("test.mod");
            var variant = new MissingModVariantViewModel(new ModDictionaryEntry());
            var raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(vm.SelectedVariant)) raised = true; };

            vm.SelectedVariant = variant;

            raised.Should().BeTrue();
        }

        [Fact]
        public void PackageId_SetProperty_ShouldRaisePropertyChanged()
        {
            var vm = new MissingModGroupViewModel("test.mod");
            var raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(vm.PackageId)) raised = true; };

            vm.PackageId = "new.mod";

            raised.Should().BeTrue();
        }

        [Fact]
        public void Variants_CanHaveItemsAdded()
        {
            var vm = new MissingModGroupViewModel("test.mod");
            var variant = new MissingModVariantViewModel(new ModDictionaryEntry { Name = "Mod A", SteamId = "123", Published = true });

            vm.Variants.Add(variant);

            vm.Variants.Should().HaveCount(1);
        }
    }

    public class MissingModVariantViewModelTests
    {
        [Fact]
        public void Constructor_ShouldSetNameFromEntry()
        {
            var entry = new ModDictionaryEntry { Name = "Cool Mod", SteamId = "12345", Published = true };

            var vm = new MissingModVariantViewModel(entry);

            vm.Name.Should().Be("Cool Mod");
        }

        [Fact]
        public void Constructor_WhenNameIsNull_ShouldFallbackToUnknown()
        {
            var entry = new ModDictionaryEntry { Name = null!, SteamId = "12345", Published = true };

            var vm = new MissingModVariantViewModel(entry);

            vm.Name.Should().Be("Unknown Name");
        }

        [Fact]
        public void Constructor_ShouldSetAuthors()
        {
            var entry = new ModDictionaryEntry { Authors = "Author A", SteamId = "12345", Published = true };

            var vm = new MissingModVariantViewModel(entry);

            vm.Authors.Should().Be("Author A");
        }

        [Fact]
        public void Constructor_WhenAuthorsIsNull_ShouldFallback()
        {
            var entry = new ModDictionaryEntry { Authors = null!, Published = true };

            var vm = new MissingModVariantViewModel(entry);

            vm.Authors.Should().Be("Unknown Author");
        }

        [Fact]
        public void Constructor_ShouldSetVersionsString()
        {
            var entry = new ModDictionaryEntry { Versions = new System.Collections.Generic.List<string> { "1.4", "1.5" }, Published = true };

            var vm = new MissingModVariantViewModel(entry);

            vm.VersionsString.Should().Be("1.4, 1.5");
        }

        [Fact]
        public void Constructor_WhenVersionsIsEmpty_ShouldShowUnknownVersion()
        {
            var entry = new ModDictionaryEntry { Versions = new System.Collections.Generic.List<string>(), Published = true };

            var vm = new MissingModVariantViewModel(entry);

            vm.VersionsString.Should().Be("Unknown Version");
        }

        [Fact]
        public void Constructor_ShouldSetSteamId()
        {
            var entry = new ModDictionaryEntry { SteamId = "98765", Published = true };

            var vm = new MissingModVariantViewModel(entry);

            vm.SteamId.Should().Be("98765");
        }

        [Fact]
        public void IsPublished_ShouldReflectEntryPublishedStatus()
        {
            var published = new MissingModVariantViewModel(new ModDictionaryEntry { Published = true });
            var unpublished = new MissingModVariantViewModel(new ModDictionaryEntry { Published = false });

            published.IsPublished.Should().BeTrue();
            unpublished.IsPublished.Should().BeFalse();
        }

        [Fact]
        public void IsSelectable_ShouldBeTrueWhenPublished()
        {
            var vm = new MissingModVariantViewModel(new ModDictionaryEntry { Published = true });

            vm.IsSelectable.Should().BeTrue();
        }

        [Fact]
        public void IsSelectable_ShouldBeFalseWhenNotPublished()
        {
            var vm = new MissingModVariantViewModel(new ModDictionaryEntry { Published = false });

            vm.IsSelectable.Should().BeFalse();
        }
    }
}
