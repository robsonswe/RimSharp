using System.Collections.Generic;
using FluentAssertions;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Shared.Models
{
    public class ModFilterCriteriaTests
    {
        [Fact]
        public void IsActive_WhenEmpty_ShouldBeFalse()
        {

            var criteria = new ModFilterCriteria();

            // Act & Assert
            criteria.IsActive().Should().BeFalse();
        }

        [Theory]
        [InlineData("test")]
        [InlineData(" ")] // WhiteSpace still counts as inactive if using string.IsNullOrWhiteSpace
        public void IsActive_WhenSearchTextSet_ShouldBeCorrect(string text)
        {

            var criteria = new ModFilterCriteria { SearchText = text };

            // Act & Assert
            criteria.IsActive().Should().Be(!string.IsNullOrWhiteSpace(text));
        }

        [Fact]
        public void IsActive_WhenModTypesSet_ShouldBeTrue()
        {

            var criteria = new ModFilterCriteria { SelectedModTypes = new List<ModType> { ModType.Core } };

            // Act & Assert
            criteria.IsActive().Should().BeTrue();
        }

        [Fact]
        public void IsActive_WhenTristateSet_ShouldBeTrue()
        {

            var criteria = new ModFilterCriteria { IsSupportedFilter = true };

            // Act & Assert
            criteria.IsActive().Should().BeTrue();
        }

        [Fact]
        public void IsActive_WhenAuthorFilterTextSet_ShouldBeTrue()
        {

            var criteria = new ModFilterCriteria { AuthorFilterText = "pardeike" };

            criteria.IsActive().Should().BeTrue();
        }

        [Fact]
        public void IsActive_WhenHasAssembliesFilterSet_ShouldBeTrue()
        {

            var criteria = new ModFilterCriteria { HasAssembliesFilter = true };

            criteria.IsActive().Should().BeTrue();
        }

        [Fact]
        public void IsActive_WhenHasTexturesFilterSet_ShouldBeTrue()
        {

            var criteria = new ModFilterCriteria { HasTexturesFilter = false };

            criteria.IsActive().Should().BeTrue();
        }

        [Fact]
        public void IsActive_WhenSelectedTagsSet_ShouldBeTrue()
        {

            var criteria = new ModFilterCriteria { SelectedTags = new List<string> { "Performance" } };

            criteria.IsActive().Should().BeTrue();
        }

        [Fact]
        public void IsActive_WhenSelectedVersionsSet_ShouldBeTrue()
        {

            var criteria = new ModFilterCriteria { SelectedSupportedVersions = new List<string> { "1.5" } };

            criteria.IsActive().Should().BeTrue();
        }

        [Fact]
        public void Clear_ShouldResetAllValues()
        {

            var criteria = new ModFilterCriteria
            {
                SearchText = "test",
                AuthorFilterText = "someone",
                IsSupportedFilter = true,
                HasAssembliesFilter = false,
                HasTexturesFilter = true,
                IsFavoriteFilter = true,
                SelectedModTypes = new List<ModType> { ModType.Core },
                SelectedTags = new List<string> { "tag" },
                SelectedSupportedVersions = new List<string> { "1.5" }
            };

            criteria.Clear();

            criteria.SearchText.Should().BeEmpty();
            criteria.AuthorFilterText.Should().BeEmpty();
            criteria.IsSupportedFilter.Should().BeNull();
            criteria.HasAssembliesFilter.Should().BeNull();
            criteria.HasTexturesFilter.Should().BeNull();
            criteria.IsFavoriteFilter.Should().BeNull();
            criteria.SelectedModTypes.Should().BeEmpty();
            criteria.SelectedTags.Should().BeEmpty();
            criteria.SelectedSupportedVersions.Should().BeEmpty();
            criteria.IsActive().Should().BeFalse();
        }

        [Fact]
        public void Clone_ShouldCreateDeepCopyOfLists()
        {

            var criteria = new ModFilterCriteria
            {
                SelectedModTypes = new List<ModType> { ModType.Core }
            };

            var clone = criteria.Clone();
            criteria.SelectedModTypes.Add(ModType.Workshop);

            clone.SelectedModTypes.Should().HaveCount(1);
            clone.SelectedModTypes.Should().Contain(ModType.Core);
            criteria.SelectedModTypes.Should().HaveCount(2);
        }

        [Fact]
        public void Clone_ShouldDeepCopyTagsAndVersionLists()
        {

            var criteria = new ModFilterCriteria
            {
                SelectedTags = new List<string> { "Library" },
                SelectedSupportedVersions = new List<string> { "1.5" }
            };

            var clone = criteria.Clone();
            criteria.SelectedTags.Add("Performance");
            criteria.SelectedSupportedVersions.Add("1.4");

            clone.SelectedTags.Should().HaveCount(1);
            clone.SelectedSupportedVersions.Should().HaveCount(1);
        }

        [Fact]
        public void Clone_ShouldCopyAuthorFilterText()
        {

            var criteria = new ModFilterCriteria { AuthorFilterText = "Pardeike" };

            var clone = criteria.Clone();

            clone.AuthorFilterText.Should().Be("Pardeike");
        }
    }
}

