using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Services.Workshop;
using Xunit;

namespace RimSharp.Tests.Services.Workshop
{
    /// <summary>
    /// Tests for SteamSubscriptionService using Steamworks.NET.
    /// These tests mock the Steamworks API via interface abstraction where possible,
    /// and test the service logic without requiring a running Steam client.
    /// </summary>
    public class SteamSubscriptionServiceTests
    {
        private readonly ILoggerService _logger = Substitute.For<ILoggerService>();
        private readonly SteamWorkshopApiClient _apiClient = Substitute.For<SteamWorkshopApiClient>(Substitute.For<System.Net.Http.HttpClient>());
        private readonly GameInstance _gameInstance = new GameInstance
        {
            GameFolder = @"C:\Test\RimWorld",
            WorkshopFolder = @"C:\Test\Steam\steamapps\workshop\content\294100",
            IsSteamInstallation = true,
            SteamClientIntegration = true
        };

        private SteamSubscriptionService CreateService()
        {
            var pathService = Substitute.For<IPathService>();
            return new SteamSubscriptionService(_gameInstance, _logger, _apiClient, pathService);
        }

        [Fact]
        public async Task GetDownloadProgressAsync_WhenSteamInitFails_ShouldReturnNullAndAttemptInitOnce()
        {
            var initCalls = 0;
            var pathService = Substitute.For<IPathService>();
            var service = new SteamSubscriptionService(
                _gameInstance, _logger, _apiClient, pathService,
                steamApiInit: () => { initCalls++; return false; },
                initRetryCooldown: TimeSpan.FromSeconds(30));

            var result = await service.GetDownloadProgressAsync("123");

            result.Should().BeNull();
            initCalls.Should().Be(1);
        }

        [Fact]
        public async Task GetDownloadProgressAsync_WhenInitFailsAndCooldownActive_ShouldNotRetryImmediately()
        {
            var initCalls = 0;
            var pathService = Substitute.For<IPathService>();
            var service = new SteamSubscriptionService(
                _gameInstance, _logger, _apiClient, pathService,
                steamApiInit: () => { initCalls++; return false; },
                initRetryCooldown: TimeSpan.FromSeconds(30));

            await service.GetDownloadProgressAsync("123");
            await service.GetDownloadProgressAsync("124");

            initCalls.Should().Be(1, "the second action inside the cooldown window must not hammer SteamAPI.Init");
        }

        [Fact]
        public async Task GetDownloadProgressAsync_WhenInitFailsAndCooldownElapsed_ShouldRetryInit()
        {
            var initCalls = 0;
            var pathService = Substitute.For<IPathService>();
            var service = new SteamSubscriptionService(
                _gameInstance, _logger, _apiClient, pathService,
                steamApiInit: () => { initCalls++; return false; },
                initRetryCooldown: TimeSpan.Zero);

            await service.GetDownloadProgressAsync("123");
            await service.GetDownloadProgressAsync("124");

            initCalls.Should().Be(2, "a user who started Steam after the failure must be able to retry");
        }

        [Fact]
        public void Constructor_ValidArguments_CreatesInstance()
        {
            // Act
            var service = CreateService();

            // Assert
            service.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_NullGameInstance_ThrowsArgumentNullException()
        {
            // Act
            var pathService = Substitute.For<IPathService>();
            Action act = () => new SteamSubscriptionService(null!, _logger, _apiClient, pathService);

            // Assert
            act.Should().Throw<ArgumentNullException>().WithParameterName("gameInstance");
        }

        [Fact]
        public void Constructor_NullLogger_ThrowsArgumentNullException()
        {
            // Act
            var pathService = Substitute.For<IPathService>();
            Action act = () => new SteamSubscriptionService(_gameInstance, null!, _apiClient, pathService);

            // Assert
            act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        }

        [Fact]
        public void Constructor_NullApiClient_ThrowsArgumentNullException()
        {
            // Act
            var pathService = Substitute.For<IPathService>();
            Action act = () => new SteamSubscriptionService(_gameInstance, _logger, null!, pathService);

            // Assert
            act.Should().Throw<ArgumentNullException>().WithParameterName("apiClient");
        }

        [Fact]
        public void Constructor_NullPathService_ThrowsArgumentNullException()
        {
            // Act
            Action act = () => new SteamSubscriptionService(_gameInstance, _logger, _apiClient, null!);

            // Assert
            act.Should().Throw<ArgumentNullException>().WithParameterName("pathService");
        }

        [Fact]
        public async Task SubscribeAsync_InvalidSteamId_ReturnsFalseAndLogsError()
        {
            // Arrange
            var service = CreateService();

            // Act
            var result = await service.SubscribeAsync("invalid-id", null, TestContext.Current.CancellationToken);

            // Assert
            result.Should().BeFalse();
            _logger.Received().LogError(Arg.Is<string>(s => s.Contains("Invalid Steam ID")), "SteamSubscriptionService");
        }

        [Fact]
        public async Task SubscribeAsync_EmptySteamId_ReturnsFalseAndLogsError()
        {
            // Arrange
            var service = CreateService();

            // Act
            var result = await service.SubscribeAsync("", null, TestContext.Current.CancellationToken);

            // Assert
            result.Should().BeFalse();
            _logger.Received().LogError(Arg.Is<string>(s => s.Contains("Invalid Steam ID")), "SteamSubscriptionService");
        }

        [Fact]
        public async Task SubscribeAsync_NonSteamInstallation_ReturnsFalseAndLogsError()
        {
            // Arrange
            var service = CreateService();
            _gameInstance.IsSteamInstallation = false;

            // Act
            var result = await service.SubscribeAsync("123456789", null, TestContext.Current.CancellationToken);

            // Assert
            result.Should().BeFalse();
            _logger.Received().LogError(Arg.Is<string>(s => s.Contains("Steam installation not configured")), "SteamSubscriptionService");
        }

        [Fact]
        public async Task SubscribeAsync_NoWorkshopFolder_ReturnsFalseAndLogsError()
        {
            // Arrange
            var service = CreateService();
            _gameInstance.WorkshopFolder = null;

            // Act
            var result = await service.SubscribeAsync("123456789", null, TestContext.Current.CancellationToken);

            // Assert
            result.Should().BeFalse();
            _logger.Received().LogError(Arg.Is<string>(s => s.Contains("Steam installation not configured")), "SteamSubscriptionService");
        }

        [Fact]
        public async Task UnsubscribeAsync_InvalidSteamId_ReturnsFalseAndLogsError()
        {
            // Arrange
            var service = CreateService();

            // Act
            var result = await service.UnsubscribeAsync("invalid-id", null, TestContext.Current.CancellationToken);

            // Assert
            result.Should().BeFalse();
            _logger.Received().LogError(Arg.Is<string>(s => s.Contains("Invalid Steam ID")), "SteamSubscriptionService");
        }

        [Fact]
        public async Task UnsubscribeAsync_NonSteamInstallation_ReturnsFalseAndLogsError()
        {
            // Arrange
            var service = CreateService();
            _gameInstance.IsSteamInstallation = false;

            // Act
            var result = await service.UnsubscribeAsync("123456789", null, TestContext.Current.CancellationToken);

            // Assert
            result.Should().BeFalse();
            _logger.Received().LogError(Arg.Is<string>(s => s.Contains("Steam installation not configured")), "SteamSubscriptionService");
        }

        [Fact]
        public async Task IsSubscribedAsync_InvalidSteamId_ReturnsFalse()
        {
            // Arrange
            var service = CreateService();

            // Act
            var result = await service.IsSubscribedAsync("invalid-id");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task IsSubscribedAsync_EmptySteamId_ReturnsFalse()
        {
            // Arrange
            var service = CreateService();

            // Act
            var result = await service.IsSubscribedAsync("");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task GetSubscribedModsAsync_NoWorkshopFolder_ReturnsEmptyList()
        {
            // Arrange
            var service = CreateService();
            _gameInstance.WorkshopFolder = null;

            // Act
            var result = await service.GetSubscribedModsAsync();

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task RefreshSubscriptionsAsync_NoWorkshopFolder_ReturnsZero()
        {
            // Arrange
            var service = CreateService();
            _gameInstance.WorkshopFolder = null;

            // Act
            var result = await service.RefreshSubscriptionsAsync(null, TestContext.Current.CancellationToken);

            // Assert
            result.Should().Be(0);
        }

        [Fact]
        public async Task ResubscribeAsync_EmptyList_ReturnsZero()
        {
            // Arrange
            var service = CreateService();

            // Act
            var result = await service.ResubscribeAsync(Enumerable.Empty<string>(), null, TestContext.Current.CancellationToken);

            // Assert
            result.Should().Be(0);
        }

        [Fact]
        public async Task ResubscribeAsync_NullList_ReturnsZero()
        {
            // Arrange
            var service = CreateService();

            // Act
            var result = await service.ResubscribeAsync(null!, null, TestContext.Current.CancellationToken);

            // Assert
            result.Should().Be(0);
        }

        [Fact]
        public async Task ResubscribeAsync_NonSteamInstallation_ReturnsZeroAndLogsError()
        {
            // Arrange
            var service = CreateService();
            _gameInstance.IsSteamInstallation = false;

            // Act
            var result = await service.ResubscribeAsync(new[] { "123", "456" }, null, TestContext.Current.CancellationToken);

            // Assert
            result.Should().Be(0);
            _logger.Received().LogError(Arg.Is<string>(s => s.Contains("Steamworks not initialized")), "SteamSubscriptionService");
        }

        [Fact]
        public void Dispose_DoesNotThrow()
        {
            // Arrange
            var service = CreateService();

            // Act
            Action act = () => service.Dispose();

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            // Arrange
            var service = CreateService();

            // Act
            service.Dispose();
            Action act = () => service.Dispose();

            // Assert
            act.Should().NotThrow();
        }

        // Ownership gate tests
        [Fact]
        public async Task CheckOwnershipAsync_NonSteamInstallation_ReturnsFalseAndLogsWarning()
        {
            // Arrange
            var service = CreateService();
            _gameInstance.IsSteamInstallation = false;

            // Act
            var result = await service.CheckOwnershipAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Should().BeFalse();
            _logger.Received().LogWarning(Arg.Is<string>(s => s.Contains("Steamworks not initialized")), "SteamSubscriptionService");
        }

        [Fact]
        public async Task CheckOwnershipAsync_NoWorkshopFolder_ReturnsFalseAndLogsWarning()
        {
            // Arrange
            var service = CreateService();
            _gameInstance.WorkshopFolder = null;

            // Act
            var result = await service.CheckOwnershipAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Should().BeFalse();
            _logger.Received().LogWarning(Arg.Is<string>(s => s.Contains("Steamworks not initialized")), "SteamSubscriptionService");
        }

        [Fact]
        public async Task CheckOwnershipAsync_SteamworksNotInitialized_ReturnsFalseAndLogsWarning()
        {
            // Arrange
            var service = CreateService();
            // Steamworks will fail to initialize in test environment (no Steam running)
            // The service will log a warning and return false

            // Act
            var result = await service.CheckOwnershipAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Should().BeFalse();
            _logger.Received().LogWarning(Arg.Is<string>(s => s.Contains("Steamworks not initialized")), "SteamSubscriptionService");
        }

        // ── Unsupported runtime (macOS) graceful degradation tests ─────────────────
        // These tests use the TestSteamworksSupportedOverride seam to simulate
        // the macOS arm64 environment where Steamworks.NET lacks native assets.
        // The service must return the same failure shapes as "non-Steam installation"
        // and "not initialized" paths: false/zero/null + log line, no exceptions.

        private SteamSubscriptionService CreateServiceWithSteamworksSupport(bool supported)
        {
            var originalOverride = SteamSubscriptionService.TestSteamworksSupportedOverride;
            SteamSubscriptionService.TestSteamworksSupportedOverride = () => supported;
            var service = CreateService();
            SteamSubscriptionService.TestSteamworksSupportedOverride = originalOverride;
            return service;
        }

        [Fact]
        public async Task SubscribeAsync_UnsupportedRuntime_ReturnsFalseAndLogsWarning()
        {
            // Arrange
            var service = CreateServiceWithSteamworksSupport(false);

            // Act
            var result = await service.SubscribeAsync("123456789", null, TestContext.Current.CancellationToken);

            // Assert
            result.Should().BeFalse();
            _logger.Received().LogWarning(Arg.Is<string>(s => s.Contains("Steamworks not supported on this runtime")), "SteamSubscriptionService");
        }

        [Fact]
        public async Task UnsubscribeAsync_UnsupportedRuntime_ReturnsFalseAndLogsWarning()
        {
            // Arrange
            var service = CreateServiceWithSteamworksSupport(false);

            // Act
            var result = await service.UnsubscribeAsync("123456789", null, TestContext.Current.CancellationToken);

            // Assert
            result.Should().BeFalse();
            _logger.Received().LogWarning(Arg.Is<string>(s => s.Contains("Steamworks not supported on this runtime")), "SteamSubscriptionService");
        }

        [Fact]
        public async Task IsSubscribedAsync_UnsupportedRuntime_ReturnsFalse()
        {
            // Arrange
            var service = CreateServiceWithSteamworksSupport(false);

            // Act
            var result = await service.IsSubscribedAsync("123456789");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task GetSubscribedModsAsync_UnsupportedRuntime_ReturnsEmptyList()
        {
            // Arrange
            var service = CreateServiceWithSteamworksSupport(false);

            // Act
            var result = await service.GetSubscribedModsAsync();

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task RefreshSubscriptionsAsync_UnsupportedRuntime_ReturnsZero()
        {
            // Arrange
            var service = CreateServiceWithSteamworksSupport(false);

            // Act
            var result = await service.RefreshSubscriptionsAsync(null, TestContext.Current.CancellationToken);

            // Assert
            result.Should().Be(0);
        }

        [Fact]
        public async Task ResubscribeAsync_UnsupportedRuntime_ReturnsZeroAndLogsWarning()
        {
            // Arrange
            var service = CreateServiceWithSteamworksSupport(false);

            // Act
            var result = await service.ResubscribeAsync(new[] { "123", "456" }, null, TestContext.Current.CancellationToken);

            // Assert
            result.Should().Be(0);
            _logger.Received().LogWarning(Arg.Is<string>(s => s.Contains("Steamworks not supported on this runtime")), "SteamSubscriptionService");
        }

        [Fact]
        public async Task GetDownloadProgressAsync_UnsupportedRuntime_ReturnsNull()
        {
            // Arrange
            var service = CreateServiceWithSteamworksSupport(false);

            // Act
            var result = await service.GetDownloadProgressAsync("123456789");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task UpdateModAsync_UnsupportedRuntime_ReturnsFalseAndLogsWarning()
        {
            // Arrange
            var service = CreateServiceWithSteamworksSupport(false);

            // Act
            var result = await service.UpdateModAsync("123456789", null, TestContext.Current.CancellationToken);

            // Assert
            result.Should().BeFalse();
            _logger.Received().LogWarning(Arg.Is<string>(s => s.Contains("Steamworks not supported on this runtime")), "SteamSubscriptionService");
        }

        [Fact]
        public async Task CheckOwnershipAsync_UnsupportedRuntime_ReturnsFalseAndLogsWarning()
        {
            // Arrange
            var service = CreateServiceWithSteamworksSupport(false);

            // Act
            var result = await service.CheckOwnershipAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Should().BeFalse();
            _logger.Received().LogWarning(Arg.Is<string>(s => s.Contains("Steamworks not supported on this runtime")), "SteamSubscriptionService");
        }

        [Fact]
        public async Task VerifyGameFilesAsync_UnsupportedRuntime_ReturnsFalseAndLogsWarning()
        {
            // Arrange
            var service = CreateServiceWithSteamworksSupport(false);

            // Act
            var result = await service.VerifyGameFilesAsync();

            // Assert
            result.Should().BeFalse();
            _logger.Received().LogWarning(Arg.Is<string>(s => s.Contains("Steamworks not supported on this runtime")), "SteamSubscriptionService");
        }

        [Fact]
        public void Dispose_UnsupportedRuntime_DoesNotThrow()
        {
            // Arrange
            var service = CreateServiceWithSteamworksSupport(false);

            // Act
            Action act = () => service.Dispose();

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void Dispose_UnsupportedRuntime_CalledTwice_DoesNotThrow()
        {
            // Arrange
            var service = CreateServiceWithSteamworksSupport(false);

            // Act
            service.Dispose();
            Action act = () => service.Dispose();

            // Assert
            act.Should().NotThrow();
        }
    }
}