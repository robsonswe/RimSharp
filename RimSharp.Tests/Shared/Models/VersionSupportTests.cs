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

            var v1 = new VersionSupport("1.4", VersionSource.Official);
            var v2 = new VersionSupport("1.4", VersionSource.Database, true);

            v1.Equals(v2).Should().BeTrue();
            (v1 == v2).Should().BeFalse(); // Reference equality check
        }

        [Fact]
        public void GetHashCode_WhenVersionsMatch_ShouldBeSame()
        {

            var v1 = new VersionSupport("1.5", VersionSource.Official);
            var v2 = new VersionSupport("1.5 ", VersionSource.Mlie);

            v1.GetHashCode().Should().Be(v2.GetHashCode());
        }

        [Fact]
        public void Constructor_WithNullVersion_ShouldThrow()
        {

            Action act = () => new VersionSupport(null!, VersionSource.Official);

            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Equals_WhenVersionsDiffer_ShouldBeFalse()
        {

            var v1 = new VersionSupport("1.4", VersionSource.Official);
            var v2 = new VersionSupport("1.5", VersionSource.Official);

            v1.Equals(v2).Should().BeFalse();
        }

        [Fact]
        public void Equals_WhenComparedWithNull_ShouldBeFalse()
        {

            var v1 = new VersionSupport("1.4", VersionSource.Official);

            v1.Equals(null).Should().BeFalse();
        }

        [Fact]
        public void GetHashCode_WhenVersionsDiffer_ShouldDiffer()
        {

            var v1 = new VersionSupport("1.4", VersionSource.Official);
            var v2 = new VersionSupport("1.5", VersionSource.Official);

            v1.GetHashCode().Should().NotBe(v2.GetHashCode());
        }

        [Fact]
        public void Version_ShouldBeTrimmed()
        {

            var v = new VersionSupport("  1.4  ", VersionSource.Official);

            v.Version.Should().Be("1.4");
        }
    }
}

