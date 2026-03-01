using System;
using FluentAssertions;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Shared.Models
{
    public class ModIssueTests
    {
        [Fact]
        public void Constructor_ShouldInitializeProperties()
        {
            // Arrange
            var mod = new ModItem { Name = "Test Mod" };
            var type = ModIssueType.Dependency;
            var desc = "Missing something";

            // Act
            var issue = new ModIssue(mod, type, desc);

            // Assert
            issue.Mod.Should().Be(mod);
            issue.Type.Should().Be(type);
            issue.Description.Should().Be(desc);
        }

        [Fact]
        public void Constructor_WithNullMod_ShouldThrow()
        {
            // Act
            Action act = () => new ModIssue(null!, ModIssueType.Sorting, "desc");

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }
    }
}
