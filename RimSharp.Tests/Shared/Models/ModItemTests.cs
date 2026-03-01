using System;
using System.Collections.Generic;
using FluentAssertions;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Shared.Models
{
    public class ModItemTests
    {
        [Fact]
        public void InstanceId_ShouldBeUniqueForEachInstance()
        {
            // Arrange & Act
            var mod1 = new ModItem();
            var mod2 = new ModItem();

            // Assert
            mod1.InstanceId.Should().NotBe(Guid.Empty);
            mod2.InstanceId.Should().NotBe(Guid.Empty);
            mod1.InstanceId.Should().NotBe(mod2.InstanceId);
        }

        [Fact]
        public void Equals_ShouldOnlyMatchSameInstance()
        {
            // Arrange
            var mod1 = new ModItem { PackageId = "test" };
            var mod2 = new ModItem { PackageId = "test" }; // Same data, different instance

            // Assert
            mod1.Equals(mod2).Should().BeFalse();
            (mod1 == mod2).Should().BeFalse(); // Reference check still works as expected
            mod1.Equals(mod1).Should().BeTrue();
        }

        [Fact]
        public void GetHashCode_ShouldBeStableAndUniquePerInstance()
        {
            // Arrange
            var mod1 = new ModItem { PackageId = "test" };
            var mod2 = new ModItem { PackageId = "test" };

            // Assert
            mod1.GetHashCode().Should().Be(mod1.GetHashCode());
            mod1.GetHashCode().Should().NotBe(mod2.GetHashCode());
        }

        [Theory]
        [InlineData("Author1, Author2", new[] { "Author1", "Author2" })]
        [InlineData("Author1; Author2", new[] { "Author1", "Author2" })]
        [InlineData("Author1, Author2; Author3", new[] { "Author1", "Author2", "Author3" })]
        [InlineData("Author1, , Author2", new[] { "Author1", "Author2" })]
        [InlineData("  Author1  ,  Author2  ", new[] { "Author1", "Author2" })]
        [InlineData("Author1, author1", new[] { "Author1" })] // Distinct check
        [InlineData("", new string[0])]
        [InlineData(null, new string[0])]
        public void AuthorList_ShouldParseCorrectly(string? authors, string[] expected)
        {
            // Arrange
            var mod = new ModItem { Authors = authors! };

            // Act
            var result = mod.AuthorList;

            // Assert
            result.Should().BeEquivalentTo(expected);
        }

        [Theory]
        [InlineData("Tag1, Tag2", new[] { "Tag1", "Tag2" })]
        [InlineData("Tag1; Tag2", new[] { "Tag1", "Tag2" })]
        public void TagList_ShouldParseCorrectly(string? tags, string[] expected)
        {
            // Arrange
            var mod = new ModItem { Tags = tags! };

            // Act
            var result = mod.TagList;

            // Assert
            result.Should().BeEquivalentTo(expected);
        }

        [Fact]
        public void InvalidateTagListCache_ShouldForceReparse()
        {
            // Arrange
            var mod = new ModItem { Tags = "Tag1" };
            var firstAccess = mod.TagList; // Populate cache

            // Act
            mod.Tags = "Tag2";
            mod.InvalidateTagListCache();
            var secondAccess = mod.TagList;

            // Assert
            secondAccess.Should().ContainSingle().Which.Should().Be("Tag2");
        }
    }

    public class ModDependencyTests
    {
        [Theory]
        [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=2557451735", "2557451735")]
        [InlineData("http://steamcommunity.com/workshop/filedetails/?id=123456789", "123456789")]
        [InlineData("id=987654321", "987654321")]
        [InlineData("invalid url", "")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void SteamId_ShouldExtractFromUrl(string? url, string expectedId)
        {
            // Arrange
            var dependency = new ModDependency { SteamWorkshopUrl = url! };

            // Act
            var result = dependency.SteamId;

            // Assert
            result.Should().Be(expectedId);
        }
    }
}
