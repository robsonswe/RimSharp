using System.Collections.Generic;
using FluentAssertions;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Features.WorkshopDownloader.Models
{
    public class DownloadItemTests
    {
        [Fact]
        public void ShouldShowVersionInfo_WhenNotInstalled_ShouldBeTrue()
        {
            // Arrange
            var item = new DownloadItem { IsInstalled = false };

            // Assert
            item.ShouldShowVersionInfo.Should().BeTrue();
        }

        [Fact]
        public void ShouldShowVersionInfo_WhenInstalledAndVersionsMatch_ShouldBeFalse()
        {
            // Arrange
            var item = new DownloadItem
            {
                IsInstalled = true,
                LatestVersions = new List<string> { "1.4", "1.5" },
                InstalledVersions = new List<VersionSupport>
                {
                    new VersionSupport("1.4", VersionSource.Official),
                    new VersionSupport("1.5", VersionSource.Official)
                }
            };

            // Assert
            item.ShouldShowVersionInfo.Should().BeFalse();
        }

        [Fact]
        public void ShouldShowVersionInfo_WhenInstalledAndVersionsMismatch_ShouldBeTrue()
        {
            // Arrange
            var item = new DownloadItem
            {
                IsInstalled = true,
                LatestVersions = new List<string> { "1.5" },
                InstalledVersions = new List<VersionSupport>
                {
                    new VersionSupport("1.4", VersionSource.Official)
                }
            };

            // Assert
            item.ShouldShowVersionInfo.Should().BeTrue();
        }

        [Fact]
        public void ClearLocalInfo_ShouldResetProperties()
        {
            // Arrange
            var item = new DownloadItem
            {
                IsInstalled = true,
                IsActive = true,
                IsFavorite = true,
                InstalledVersions = new List<VersionSupport> { new VersionSupport("1.4", VersionSource.Official) }
            };

            // Act
            item.ClearLocalInfo();

            // Assert
            item.IsInstalled.Should().BeFalse();
            item.IsActive.Should().BeFalse();
            item.IsFavorite.Should().BeFalse();
            item.InstalledVersions.Should().BeNull();
        }
    }
}
