using System.Linq;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using RimSharp.Core.Extensions;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Features.WorkshopDownloader.Services
{
    public class DownloadQueueServiceTests
    {
        public DownloadQueueServiceTests()
        {
            TestApp.InitializeTestApp();
            ThreadHelper.Initialize();
        }

        [AvaloniaFact]
        public void AddToQueue_ShouldAddItem()
        {
            // Arrange
            var service = new DownloadQueueService();
            var modInfo = new ModInfoDto { Name = "Test Mod", SteamId = "12345" };

            // Act
            var result = service.AddToQueue(modInfo);

            // Assert
            result.Should().BeTrue();
            service.Items.Should().HaveCount(1);
            service.Items[0].SteamId.Should().Be("12345");
        }

        [AvaloniaFact]
        public void AddToQueue_Duplicate_ShouldReturnFalse()
        {
            // Arrange
            var service = new DownloadQueueService();
            var modInfo = new ModInfoDto { Name = "Test Mod", SteamId = "12345" };
            service.AddToQueue(modInfo);

            // Act
            var result = service.AddToQueue(modInfo);

            // Assert
            result.Should().BeFalse();
            service.Items.Should().HaveCount(1);
        }

        [AvaloniaFact]
        public void IsInQueue_ShouldReturnCorrectValue()
        {
            // Arrange
            var service = new DownloadQueueService();
            service.AddToQueue(new ModInfoDto { SteamId = "123" });

            // Assert
            service.IsInQueue("123").Should().BeTrue();
            service.IsInQueue("456").Should().BeFalse();
        }

        [AvaloniaFact]
        public void RemoveFromQueue_ShouldRemoveItem()
        {
            // Arrange
            var service = new DownloadQueueService();
            var modInfo = new ModInfoDto { SteamId = "123" };
            service.AddToQueue(modInfo);
            var item = service.Items.First();

            // Act
            var result = service.RemoveFromQueue(item);

            // Assert
            result.Should().BeTrue();
            service.Items.Should().BeEmpty();
        }
    }
}
