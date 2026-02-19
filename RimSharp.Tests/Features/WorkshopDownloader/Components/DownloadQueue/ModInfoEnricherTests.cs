using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.WorkshopDownloader.Components.DownloadQueue;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.WorkshopDownloader.Components.DownloadQueue
{
    public class ModInfoEnricherTests
    {
        private readonly IModListManager _mockModListManager;
        private readonly ModInfoEnricher _enricher;

        public ModInfoEnricherTests()
        {
            _mockModListManager = Substitute.For<IModListManager>();
            _enricher = new ModInfoEnricher(_mockModListManager);
        }

        [Fact]
        public void EnrichDownloadItem_WhenLocalMatchExists_ShouldPopulateData()
        {
            // Arrange
            var steamId = "12345";
            var localMod = new ModItem 
            { 
                SteamId = steamId, 
                DateStamp = "2024-01-01", 
                IsOutdatedRW = true,
                IsFavorite = true
            };
            _mockModListManager.GetAllMods().Returns(new List<ModItem> { localMod });
            _mockModListManager.IsModActive(localMod).Returns(true);

            var downloadItem = new DownloadItem { SteamId = steamId };

            // Act
            _enricher.EnrichDownloadItem(downloadItem);

            // Assert
            downloadItem.IsInstalled.Should().BeTrue();
            downloadItem.LocalDateStamp.Should().Be("2024-01-01");
            downloadItem.IsActive.Should().BeTrue();
            downloadItem.IsLocallyOutdatedRW.Should().BeTrue();
            downloadItem.IsFavorite.Should().BeTrue();
        }

        [Fact]
        public void EnrichDownloadItem_WhenNoLocalMatch_ShouldClearInfo()
        {
            // Arrange
            _mockModListManager.GetAllMods().Returns(new List<ModItem>());
            var downloadItem = new DownloadItem 
            { 
                SteamId = "999", 
                IsInstalled = true // Pretend it was set before
            };

            // Act
            _enricher.EnrichDownloadItem(downloadItem);

            // Assert
            downloadItem.IsInstalled.Should().BeFalse();
            downloadItem.LocalDateStamp.Should().BeNull();
        }

        [Fact]
        public void EnrichAllDownloadItems_ShouldProcessAllItems()
        {
            // Arrange
            var mod1 = new ModItem { SteamId = "1" };
            var mod2 = new ModItem { SteamId = "2" };
            _mockModListManager.GetAllMods().Returns(new List<ModItem> { mod1, mod2 });

            var items = new List<DownloadItem> 
            { 
                new DownloadItem { SteamId = "1" }, 
                new DownloadItem { SteamId = "2" },
                new DownloadItem { SteamId = "3" }
            };

            // Act
            _enricher.EnrichAllDownloadItems(items);

            // Assert
            items[0].IsInstalled.Should().BeTrue();
            items[1].IsInstalled.Should().BeTrue();
            items[2].IsInstalled.Should().BeFalse();
        }
    }
}
