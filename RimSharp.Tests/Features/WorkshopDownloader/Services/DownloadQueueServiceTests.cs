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

            var service = new DownloadQueueService();
            var modInfo = new ModInfoDto { Name = "Test Mod", SteamId = "12345" };

            var result = service.AddToQueue(modInfo);

            result.Should().BeTrue();
            service.Items.Should().HaveCount(1);
            service.Items[0].SteamId.Should().Be("12345");
        }

        [AvaloniaFact]
        public void AddToQueue_Duplicate_ShouldReturnFalse()
        {

            var service = new DownloadQueueService();
            var modInfo = new ModInfoDto { Name = "Test Mod", SteamId = "12345" };
            service.AddToQueue(modInfo);

            var result = service.AddToQueue(modInfo);

            result.Should().BeFalse();
            service.Items.Should().HaveCount(1);
        }

        [AvaloniaFact]
        public void IsInQueue_ShouldReturnCorrectValue()
        {

            var service = new DownloadQueueService();
            service.AddToQueue(new ModInfoDto { SteamId = "123" });

            service.IsInQueue("123").Should().BeTrue();
            service.IsInQueue("456").Should().BeFalse();
        }

        [AvaloniaFact]
        public void RemoveFromQueue_ShouldRemoveItem()
        {

            var service = new DownloadQueueService();
            var modInfo = new ModInfoDto { SteamId = "123" };
            service.AddToQueue(modInfo);
            var item = service.Items.First();

            var result = service.RemoveFromQueue(item);

            result.Should().BeTrue();
            service.Items.Should().BeEmpty();
        }
    }
}

