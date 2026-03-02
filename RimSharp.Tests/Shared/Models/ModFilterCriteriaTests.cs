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
        public void Clear_ShouldResetAllValues()
        {

            var criteria = new ModFilterCriteria
            {
                SearchText = "test",
                IsSupportedFilter = true,
                SelectedModTypes = new List<ModType> { ModType.Core }
            };

            criteria.Clear();

            criteria.SearchText.Should().BeEmpty();
            criteria.IsSupportedFilter.Should().BeNull();
            criteria.SelectedModTypes.Should().BeEmpty();
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
    }
}

