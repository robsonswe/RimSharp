using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.WorkshopDownloader.Services
{
    public class SteamWorkshopQueueProcessorTests
    {
        private readonly ISteamApiClient _mockSteamApiClient;
        private readonly IDownloadQueueService _mockDownloadQueueService;
        private readonly ILoggerService _mockLogger;
        private readonly SteamWorkshopQueueProcessor _processor;

        public SteamWorkshopQueueProcessorTests()
        {
            _mockSteamApiClient = Substitute.For<ISteamApiClient>();
            _mockDownloadQueueService = Substitute.For<IDownloadQueueService>();
            _mockLogger = Substitute.For<ILoggerService>();
            _processor = new SteamWorkshopQueueProcessor(_mockSteamApiClient, _mockDownloadQueueService, _mockLogger);
        }

        [Fact]
        public async Task ProcessAndEnqueueModsAsync_WhenEverythingSucceeds_ShouldReturnSuccess()
        {
            // Arrange
            var steamIds = new[] { "123" };
            var apiResponse = new SteamApiResponse
            {
                Response = new SteamApiResponseDetails
                {
                    PublishedFileDetails = new List<SteamPublishedFileDetails>
                    {
                        new SteamPublishedFileDetails
                        {
                            Result = 1,
                            Title = "Test Mod",
                            TimeUpdated = 1739883600
                        }
                    }
                }
            };

            _mockDownloadQueueService.IsInQueue("123").Returns(false);
            _mockSteamApiClient.GetFileDetailsAsync("123", Arg.Any<CancellationToken>())
                .Returns(apiResponse);
            _mockDownloadQueueService.AddToQueue(Arg.Any<ModInfoDto>()).Returns(true);

            // Act
            var result = await _processor.ProcessAndEnqueueModsAsync(steamIds, null, CancellationToken.None);

            // Assert
            result.SuccessfullyAdded.Should().Be(1);
            result.AlreadyQueued.Should().Be(0);
            result.FailedProcessing.Should().Be(0);
            result.AddedModNames.Should().Contain("Test Mod");
            _mockDownloadQueueService.Received(1).AddToQueue(Arg.Is<ModInfoDto>(m => m.SteamId == "123"));
        }

        [Fact]
        public async Task ProcessAndEnqueueModsAsync_WhenAlreadyInQueue_ShouldSkip()
        {
            // Arrange
            var steamIds = new[] { "123" };
            _mockDownloadQueueService.IsInQueue("123").Returns(true);

            // Act
            var result = await _processor.ProcessAndEnqueueModsAsync(steamIds, null, CancellationToken.None);

            // Assert
            result.SuccessfullyAdded.Should().Be(0);
            result.AlreadyQueued.Should().Be(1);
            _ = _mockSteamApiClient.DidNotReceive().GetFileDetailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessAndEnqueueModsAsync_WhenApiFails_ShouldMarkAsFailed()
        {
            // Arrange
            var steamIds = new[] { "123" };
            _mockDownloadQueueService.IsInQueue("123").Returns(false);
            _mockSteamApiClient.GetFileDetailsAsync("123", Arg.Any<CancellationToken>())
                .ThrowsAsync(new Exception("Network Error"));

            // Act
            var result = await _processor.ProcessAndEnqueueModsAsync(steamIds, null, CancellationToken.None);

            // Assert
            result.SuccessfullyAdded.Should().Be(0);
            result.FailedProcessing.Should().Be(1);
            result.ErrorMessages.Should().Contain(m => m.Contains("Network Error"));
        }

        [Fact]
        public async Task ProcessAndEnqueueModsAsync_WhenCancelled_ShouldSetFlag()
        {
            // Arrange
            var steamIds = new[] { "123", "456" };
            var cts = new CancellationTokenSource();
            
            _mockDownloadQueueService.IsInQueue(Arg.Any<string>()).Returns(false);
            _mockSteamApiClient.GetFileDetailsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(x => { 
                    cts.Cancel(); 
                    return Task.FromResult<SteamApiResponse?>(null); 
                });

            // Act
            var result = await _processor.ProcessAndEnqueueModsAsync(steamIds, null, cts.Token);

            // Assert
            result.WasCancelled.Should().BeTrue();
        }
    }
}
