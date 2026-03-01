using System;
using FluentAssertions;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Shared.Models
{
    public class VersionSupportTests
    {
        [Fact]
        public void Equals_WhenVersionsMatch_ShouldBeTrue()
        {
            // Arrange
            var v1 = new VersionSupport("1.4", VersionSource.Official);
            var v2 = new VersionSupport("1.4", VersionSource.Database, true);

            // Assert
            v1.Equals(v2).Should().BeTrue();
            (v1 == v2).Should().BeFalse(); // Reference equality check
        }

        [Fact]
        public void GetHashCode_WhenVersionsMatch_ShouldBeSame()
        {
            // Arrange
            var v1 = new VersionSupport("1.5", VersionSource.Official);
            var v2 = new VersionSupport("1.5 ", VersionSource.Mlie);

            // Assert
            v1.GetHashCode().Should().Be(v2.GetHashCode());
        }

        [Fact]
        public void Constructor_WithNullVersion_ShouldThrow()
        {
            // Act
            Action act = () => new VersionSupport(null!, VersionSource.Official);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }
    }
}
