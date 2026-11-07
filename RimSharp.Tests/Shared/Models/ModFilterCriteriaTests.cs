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
        public void IsActive_WhenIncludedTagSet_ShouldBeTrue()
        {

            var criteria = new ModFilterCriteria { IncludedTags = new List<string> { "Performance" } };

            criteria.IsActive().Should().BeTrue();
        }

        [Fact]
        public void IsActive_WhenExcludedTagSet_ShouldBeTrue()
        {

            var criteria = new ModFilterCriteria { ExcludedTags = new List<string> { "Performance" } };

            criteria.IsActive().Should().BeTrue();
        }

        [Fact]
        public void IsActive_WhenIncludedVersionSet_ShouldBeTrue()
        {

            var criteria = new ModFilterCriteria { IncludedSupportedVersions = new List<string> { "1.5" } };

            criteria.IsActive().Should().BeTrue();
        }

        [Fact]
        public void IsActive_WhenExcludedVersionSet_ShouldBeTrue()
        {

            var criteria = new ModFilterCriteria { ExcludedSupportedVersions = new List<string> { "1.5" } };

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
                IncludedTags = new List<string> { "tag" },
                ExcludedTags = new List<string> { "other" },
                IncludedSupportedVersions = new List<string> { "1.5" },
                ExcludedSupportedVersions = new List<string> { "1.6" }
            };

            criteria.Clear();

            criteria.SearchText.Should().BeEmpty();
            criteria.AuthorFilterText.Should().BeEmpty();
            criteria.IsSupportedFilter.Should().BeNull();
            criteria.HasAssembliesFilter.Should().BeNull();
            criteria.HasTexturesFilter.Should().BeNull();
            criteria.IsFavoriteFilter.Should().BeNull();
            criteria.SelectedModTypes.Should().BeEmpty();
            criteria.IncludedTags.Should().BeEmpty();
            criteria.ExcludedTags.Should().BeEmpty();
            criteria.IncludedSupportedVersions.Should().BeEmpty();
            criteria.ExcludedSupportedVersions.Should().BeEmpty();
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
                IncludedTags = new List<string> { "Library" },
                ExcludedTags = new List<string> { "Beta" },
                IncludedSupportedVersions = new List<string> { "1.5" },
                ExcludedSupportedVersions = new List<string> { "1.6" }
            };

            var clone = criteria.Clone();
            criteria.IncludedTags.Add("Performance");
            criteria.ExcludedTags.Add(" unstable");
            criteria.IncludedSupportedVersions.Add("1.4");
            criteria.ExcludedSupportedVersions.Add("1.3");

            clone.IncludedTags.Should().HaveCount(1);
            clone.ExcludedTags.Should().HaveCount(1);
            clone.IncludedSupportedVersions.Should().HaveCount(1);
            clone.ExcludedSupportedVersions.Should().HaveCount(1);
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

