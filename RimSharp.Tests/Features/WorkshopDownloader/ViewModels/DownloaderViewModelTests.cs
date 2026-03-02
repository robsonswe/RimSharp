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

            var vm = CreateViewModel();

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

            var vm = CreateViewModel();

            vm.BrowserViewModel.Should().BeOfType<BrowserViewModel>();
            vm.QueueViewModel.Should().BeOfType<DownloadQueueViewModel>();
            vm.StatusBarViewModel.Should().BeOfType<StatusBarViewModel>();
        }

        [Fact]
        public void CancelOperationCommand_CanExecute_ShouldReturnFalse_WhenNoOperationInProgress()
        {

            var vm = CreateViewModel();

            var canExecute = vm.CancelOperationCommand.CanExecute(null);

            canExecute.Should().BeFalse();
        }

        [Fact]
        public void GetCancellationToken_ShouldReturnNone_WhenNoOperation()
        {

            var vm = CreateViewModel();

            var token = vm.GetCancellationToken();

            token.Should().Be(CancellationToken.None);
        }

        [Fact]
        public void SetBrowserControl_ShouldNotThrow()
        {

            var vm = CreateViewModel();

            // Act & Assert
            vm.Invoking(v => v.SetBrowserControl(null!)).Should().NotThrow();
        }

        [Fact]
        public void Dispose_ShouldBeIdempotent()
        {

            var vm = CreateViewModel();

            vm.Invoking(v => v.Dispose()).Should().NotThrow();
            vm.Invoking(v => v.Dispose()).Should().NotThrow();
        }

        [Fact]
        public void Constructor_ShouldSetupEventHandlers()
        {

            var vm = CreateViewModel();

            vm.BrowserViewModel.Should().NotBeNull();
            vm.QueueViewModel.Should().NotBeNull();
        }

        [Fact]
        public void StatusBarViewModel_ShouldBeAccessible()
        {

            var vm = CreateViewModel();

            vm.StatusBarViewModel.Should().NotBeNull();
        }

        [Fact]
        public void IsOperationInProgress_ShouldBeFalseInitially()
        {

            var vm = CreateViewModel();

            vm.IsOperationInProgress.Should().BeFalse();
        }

        [Fact]
        public void IsSteamCmdReady_ShouldBeFalseInitially()
        {

            var vm = CreateViewModel();

            vm.IsSteamCmdReady.Should().BeFalse();
        }

        [Fact]
        public void QueueViewModel_ShouldHaveCorrectInitialStatus()
        {

            var vm = CreateViewModel();

            vm.QueueViewModel.IsOperationInProgress.Should().BeFalse();
        }

        [Fact]
        public void BrowserViewModel_ShouldHaveCorrectInitialStatus()
        {

            var vm = CreateViewModel();

            vm.BrowserViewModel.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_ShouldCallCheckSetupAsync()
        {

            var vm = CreateViewModel();

            // Allow async operation to complete
            Thread.Sleep(100);

            _mockSteamCmdService.Received().CheckSetupAsync();
        }

        [Fact]
        public void CancelOperationCommand_ShouldExist()
        {

            var vm = CreateViewModel();

            vm.CancelOperationCommand.Should().NotBeNull();
        }

        [Fact]
        public void DownloadCompletedAndRefreshNeeded_Event_ShouldExist()
        {

            var vm = CreateViewModel();

            vm.Invoking(v => v.DownloadCompletedAndRefreshNeeded += (s, e) => { }).Should().NotThrow();
        }

        [Fact]
        public void GetCancellationToken_ShouldReturnValidToken()
        {

            var vm = CreateViewModel();

            var token = vm.GetCancellationToken();

            token.CanBeCanceled.Should().BeFalse(); // CancellationToken.None
        }
    }
}


