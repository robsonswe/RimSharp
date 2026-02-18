using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Features.WorkshopDownloader.Services
{
    public class WorkshopUpdateCheckerServiceTests
    {
        private readonly ISteamApiClient _mockSteamApiClient;
        private readonly IDownloadQueueService _mockDownloadQueueService;
        private readonly WorkshopUpdateCheckerService _service;

        public WorkshopUpdateCheckerServiceTests()
        {
            _mockSteamApiClient = Substitute.For<ISteamApiClient>();
            _mockDownloadQueueService = Substitute.For<IDownloadQueueService>();
            _service = new WorkshopUpdateCheckerService(_mockSteamApiClient, _mockDownloadQueueService);
        }

        [Fact]
        public async Task CheckForUpdatesAsync_WhenUpdateFound_ShouldAddToQueue()
        {
            // Arrange
            // 18/02/2026 12:00:00 UTC
            var localDate = "18/02/2026 12:00:00"; 
            var mod = new ModItem { Name = "Test Mod", SteamId = "123", UpdateDate = localDate };
            
            // API reports 1 hour later (18/02/2026 13:00:00 UTC)
            var apiUpdateDate = new DateTimeOffset(2026, 2, 18, 13, 0, 0, TimeSpan.Zero);
            var apiUnix = apiUpdateDate.ToUnixTimeSeconds();

            var apiResponse = new SteamApiResponse
            {
                Response = new SteamApiResponseDetails
                {
                    PublishedFileDetails = new List<SteamPublishedFileDetails>
                    {
                        new SteamPublishedFileDetails
                        {
                            Result = 1,
                            ConsumerAppId = 294100,
                            Title = "Test Mod",
                            TimeUpdated = apiUnix
                        }
                    }
                }
            };

            _mockSteamApiClient.GetFileDetailsAsync("123", Arg.Any<CancellationToken>())
                .Returns(apiResponse);

            // Act
            var result = await _service.CheckForUpdatesAsync(new[] { mod });

            // Assert
            result.UpdatesFound.Should().Be(1);
            _mockDownloadQueueService.Received(1).AddToQueue(Arg.Is<ModInfoDto>(m => m.SteamId == "123"));
        }

        [Fact]
        public async Task CheckForUpdatesAsync_WhenUpToDate_ShouldNotAddToQueue()
        {
            // Arrange
            var localDate = "18/02/2026 12:00:00";
            var mod = new ModItem { Name = "Test Mod", SteamId = "123", UpdateDate = localDate };
            
            // API reports same time
            var apiUpdateDate = new DateTimeOffset(2026, 2, 18, 12, 0, 0, TimeSpan.Zero);
            var apiUnix = apiUpdateDate.ToUnixTimeSeconds();

            var apiResponse = new SteamApiResponse
            {
                Response = new SteamApiResponseDetails
                {
                    PublishedFileDetails = new List<SteamPublishedFileDetails>
                    {
                        new SteamPublishedFileDetails
                        {
                            Result = 1,
                            ConsumerAppId = 294100,
                            TimeUpdated = apiUnix
                        }
                    }
                }
            };

            _mockSteamApiClient.GetFileDetailsAsync("123", Arg.Any<CancellationToken>())
                .Returns(apiResponse);

            // Act
            var result = await _service.CheckForUpdatesAsync(new[] { mod });

            // Assert
            result.UpdatesFound.Should().Be(0);
            _mockDownloadQueueService.DidNotReceive().AddToQueue(Arg.Any<ModInfoDto>());
        }

        [Fact]
        public async Task CheckForUpdatesAsync_WhenTimezoneArtifact_ShouldNotAddToQueue()
        {
            // Arrange
            var localDate = "18/02/2026 12:00:00";
            var mod = new ModItem { Name = "Test Mod", SteamId = "123", UpdateDate = localDate };
            
            // API reports 1 minute later (within 2 min threshold)
            var apiUpdateDate = new DateTimeOffset(2026, 2, 18, 12, 1, 0, TimeSpan.Zero);
            var apiUnix = apiUpdateDate.ToUnixTimeSeconds();

            var apiResponse = new SteamApiResponse
            {
                Response = new SteamApiResponseDetails
                {
                    PublishedFileDetails = new List<SteamPublishedFileDetails>
                    {
                        new SteamPublishedFileDetails
                        {
                            Result = 1,
                            ConsumerAppId = 294100,
                            TimeUpdated = apiUnix
                        }
                    }
                }
            };

            _mockSteamApiClient.GetFileDetailsAsync("123", Arg.Any<CancellationToken>())
                .Returns(apiResponse);

            // Act
            var result = await _service.CheckForUpdatesAsync(new[] { mod });

            // Assert
            result.UpdatesFound.Should().Be(0);
        }
    }
}
