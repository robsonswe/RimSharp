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
            // Arrange
            var criteria = new ModFilterCriteria();

            // Act & Assert
            criteria.IsActive().Should().BeFalse();
        }

        [Theory]
        [InlineData("test")]
        [InlineData(" ")] // WhiteSpace still counts as inactive if using string.IsNullOrWhiteSpace
        public void IsActive_WhenSearchTextSet_ShouldBeCorrect(string text)
        {
            // Arrange
            var criteria = new ModFilterCriteria { SearchText = text };

            // Act & Assert
            criteria.IsActive().Should().Be(!string.IsNullOrWhiteSpace(text));
        }

        [Fact]
        public void IsActive_WhenModTypesSet_ShouldBeTrue()
        {
            // Arrange
            var criteria = new ModFilterCriteria { SelectedModTypes = new List<ModType> { ModType.Core } };

            // Act & Assert
            criteria.IsActive().Should().BeTrue();
        }

        [Fact]
        public void IsActive_WhenTristateSet_ShouldBeTrue()
        {
            // Arrange
            var criteria = new ModFilterCriteria { IsOutdatedFilter = true };

            // Act & Assert
            criteria.IsActive().Should().BeTrue();
        }

        [Fact]
        public void Clear_ShouldResetAllValues()
        {
            // Arrange
            var criteria = new ModFilterCriteria
            {
                SearchText = "test",
                IsOutdatedFilter = true,
                SelectedModTypes = new List<ModType> { ModType.Core }
            };

            // Act
            criteria.Clear();

            // Assert
            criteria.SearchText.Should().BeEmpty();
            criteria.IsOutdatedFilter.Should().BeNull();
            criteria.SelectedModTypes.Should().BeEmpty();
            criteria.IsActive().Should().BeFalse();
        }

        [Fact]
        public void Clone_ShouldCreateDeepCopyOfLists()
        {
            // Arrange
            var criteria = new ModFilterCriteria
            {
                SelectedModTypes = new List<ModType> { ModType.Core }
            };

            // Act
            var clone = criteria.Clone();
            criteria.SelectedModTypes.Add(ModType.Workshop);

            // Assert
            clone.SelectedModTypes.Should().HaveCount(1);
            clone.SelectedModTypes.Should().Contain(ModType.Core);
            criteria.SelectedModTypes.Should().HaveCount(2);
        }
    }
}
