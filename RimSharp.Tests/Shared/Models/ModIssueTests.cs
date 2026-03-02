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

            var mod = new ModItem { Name = "Test Mod" };
            var type = ModIssueType.Dependency;
            var desc = "Missing something";

            var issue = new ModIssue(mod, type, desc);

            issue.Mod.Should().Be(mod);
            issue.Type.Should().Be(type);
            issue.Description.Should().Be(desc);
        }

        [Fact]
        public void Constructor_WithNullMod_ShouldThrow()
        {

            Action act = () => new ModIssue(null!, ModIssueType.Sorting, "desc");

            act.Should().Throw<ArgumentNullException>();
        }
    }
}

