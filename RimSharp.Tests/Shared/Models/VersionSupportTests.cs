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
            var v2 = new VersionSupport("1.4", VersionSource.External, true);

            v1.Equals(v2).Should().BeTrue();
            (v1 == v2).Should().BeFalse(); // Reference equality check
        }

        [Fact]
        public void GetHashCode_WhenVersionsMatch_ShouldBeSame()
        {

            var v1 = new VersionSupport("1.5", VersionSource.Official);
            var v2 = new VersionSupport("1.5 ", VersionSource.External);

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

        [Fact]
        public void Constructor_ShouldStoreExternalSourceName()
        {
            var v = new VersionSupport("1.5", VersionSource.External, true, "Community NVW");

            v.ExternalSourceName.Should().Be("Community NVW");
        }

        [Fact]
        public void Constructor_WithoutExternalSourceName_ShouldDefaultToNull()
        {
            var v = new VersionSupport("1.5", VersionSource.External, true);

            v.ExternalSourceName.Should().BeNull();
        }

        [Fact]
        public void EqualityAndToString_ShouldIgnoreExternalSourceName()
        {
            var v1 = new VersionSupport("1.5", VersionSource.External, true, "Source A");
            var v2 = new VersionSupport("1.5", VersionSource.External, true, "Source B");

            v1.Equals(v2).Should().BeTrue();
            v1.GetHashCode().Should().Be(v2.GetHashCode());
            v1.ToString().Should().Be("1.5");
        }
    }
}
