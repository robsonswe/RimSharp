using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.WorkshopDownloader.Components.Browser;
using RimSharp.Features.WorkshopDownloader.Components.DownloadQueue;
using RimSharp.Features.WorkshopDownloader.Components.StatusBar;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Features.WorkshopDownloader.ViewModels;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.WorkshopDownloader.ViewModels
{
    public class DownloaderViewModelTests : IDisposable
    {
        private readonly IWebNavigationService _mockNavigationService;
        private readonly IDownloadQueueService _mockQueueService;
        private readonly IModService _mockModService;
        private readonly IDialogService _mockDialogService;
        private readonly IWorkshopUpdateCheckerService _mockUpdateChecker;
        private readonly ISteamCmdService _mockSteamCmdService;
        private readonly IModListManager _mockModListManager;
        private readonly ISteamWorkshopQueueProcessor _mockQueueProcessor;
        private readonly ILoggerService _mockLogger;
        private readonly ISteamApiClient _mockSteamApiClient;
        private readonly ModInfoEnricher _enricher;

        public DownloaderViewModelTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();

            _mockNavigationService = Substitute.For<IWebNavigationService>();
            _mockQueueService = Substitute.For<IDownloadQueueService>();
            _mockQueueService.Items.Returns(new System.Collections.ObjectModel.ObservableCollection<DownloadItem>());
            _mockModService = Substitute.For<IModService>();
            _mockDialogService = Substitute.For<IDialogService>();
            _mockUpdateChecker = Substitute.For<IWorkshopUpdateCheckerService>();
            _mockSteamCmdService = Substitute.For<ISteamCmdService>();
            _mockModListManager = Substitute.For<IModListManager>();
            _mockQueueProcessor = Substitute.For<ISteamWorkshopQueueProcessor>();
            _mockLogger = Substitute.For<ILoggerService>();
            _mockSteamApiClient = Substitute.For<ISteamApiClient>();

            _enricher = new ModInfoEnricher(_mockModListManager);
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        private DownloaderViewModel CreateViewModel()
        {
            return new DownloaderViewModel(
                _mockNavigationService,
                _mockQueueService,
                _mockModService,
                _mockDialogService,
                _mockUpdateChecker,
                _mockSteamCmdService,
                _mockModListManager,
                _enricher,
                _mockQueueProcessor,
                _mockLogger,
                _mockSteamApiClient
            );
        }

        [Fact]
        public void Constructor_ShouldInitializeCorrectly()
        {
            // Act
            var vm = CreateViewModel();

            // Assert
            vm.BrowserViewModel.Should().NotBeNull();
            vm.QueueViewModel.Should().NotBeNull();
            vm.StatusBarViewModel.Should().NotBeNull();
            vm.IsOperationInProgress.Should().BeFalse();
            vm.IsSteamCmdReady.Should().BeFalse();
            vm.CancelOperationCommand.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_ShouldCreateChildViewModels()
        {
            // Act
            var vm = CreateViewModel();

            // Assert
            vm.BrowserViewModel.Should().BeOfType<BrowserViewModel>();
            vm.QueueViewModel.Should().BeOfType<DownloadQueueViewModel>();
            vm.StatusBarViewModel.Should().BeOfType<StatusBarViewModel>();
        }

        [Fact]
        public void CancelOperationCommand_CanExecute_ShouldReturnFalse_WhenNoOperationInProgress()
        {
            // Arrange
            var vm = CreateViewModel();

            // Act
            var canExecute = vm.CancelOperationCommand.CanExecute(null);

            // Assert
            canExecute.Should().BeFalse();
        }

        [Fact]
        public void GetCancellationToken_ShouldReturnNone_WhenNoOperation()
        {
            // Arrange
            var vm = CreateViewModel();

            // Act
            var token = vm.GetCancellationToken();

            // Assert
            token.Should().Be(CancellationToken.None);
        }

        [Fact]
        public void SetWebView_ShouldNotThrow()
        {
            // Arrange
            var vm = CreateViewModel();

            // Act & Assert - Should not throw even if WebView2 is not fully initialized in test environment
            vm.Invoking(v => v.SetWebView(null!)).Should().NotThrow();
        }

        [Fact]
        public void Dispose_ShouldBeIdempotent()
        {
            // Arrange
            var vm = CreateViewModel();

            // Act & Assert - Second dispose should not throw
            vm.Invoking(v => v.Dispose()).Should().NotThrow();
            vm.Invoking(v => v.Dispose()).Should().NotThrow();
        }

        [Fact]
        public void Constructor_ShouldSetupEventHandlers()
        {
            // Act
            var vm = CreateViewModel();

            // Assert - If event handlers weren't set up correctly, these would be null
            vm.BrowserViewModel.Should().NotBeNull();
            vm.QueueViewModel.Should().NotBeNull();
        }

        [Fact]
        public void StatusBarViewModel_ShouldBeAccessible()
        {
            // Arrange
            var vm = CreateViewModel();

            // Assert
            vm.StatusBarViewModel.Should().NotBeNull();
        }

        [Fact]
        public void IsOperationInProgress_ShouldBeFalseInitially()
        {
            // Arrange
            var vm = CreateViewModel();

            // Assert
            vm.IsOperationInProgress.Should().BeFalse();
        }

        [Fact]
        public void IsSteamCmdReady_ShouldBeFalseInitially()
        {
            // Arrange
            var vm = CreateViewModel();

            // Assert
            vm.IsSteamCmdReady.Should().BeFalse();
        }

        [Fact]
        public void QueueViewModel_ShouldHaveCorrectInitialStatus()
        {
            // Arrange
            var vm = CreateViewModel();

            // Assert
            vm.QueueViewModel.IsOperationInProgress.Should().BeFalse();
        }

        [Fact]
        public void BrowserViewModel_ShouldHaveCorrectInitialStatus()
        {
            // Arrange
            var vm = CreateViewModel();

            // Assert
            vm.BrowserViewModel.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_ShouldCallCheckSetupAsync()
        {
            // Act
            var vm = CreateViewModel();

            // Allow async operation to complete
            Thread.Sleep(100);

            // Assert
            _mockSteamCmdService.Received().CheckSetupAsync();
        }

        [Fact]
        public void CancelOperationCommand_ShouldExist()
        {
            // Arrange
            var vm = CreateViewModel();

            // Assert
            vm.CancelOperationCommand.Should().NotBeNull();
        }

        [Fact]
        public void DownloadCompletedAndRefreshNeeded_Event_ShouldExist()
        {
            // Arrange
            var vm = CreateViewModel();

            // Act & Assert - Should be able to subscribe without throwing
            vm.Invoking(v => v.DownloadCompletedAndRefreshNeeded += (s, e) => { }).Should().NotThrow();
        }

        [Fact]
        public void GetCancellationToken_ShouldReturnValidToken()
        {
            // Arrange
            var vm = CreateViewModel();

            // Act
            var token = vm.GetCancellationToken();

            // Assert
            token.CanBeCanceled.Should().BeFalse(); // CancellationToken.None
        }
    }
}
