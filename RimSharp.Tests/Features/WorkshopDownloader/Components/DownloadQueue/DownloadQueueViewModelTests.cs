using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.WorkshopDownloader.Components.Browser;
using RimSharp.Features.WorkshopDownloader.Components.DownloadQueue;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Features.WorkshopDownloader.ViewModels;
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.WorkshopDownloader.Components.DownloadQueue
{
    public class DownloadQueueViewModelTests
    {
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
        private readonly BrowserViewModel _browserViewModel;

        public DownloadQueueViewModelTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();
            
            _mockQueueService = Substitute.For<IDownloadQueueService>();
            _mockQueueService.Items.Returns(new ObservableCollection<DownloadItem>());
            
            _mockModService = Substitute.For<IModService>();
            _mockDialogService = Substitute.For<IDialogService>();
            _mockUpdateChecker = Substitute.For<IWorkshopUpdateCheckerService>();
            _mockSteamCmdService = Substitute.For<ISteamCmdService>();
            _mockModListManager = Substitute.For<IModListManager>();
            _mockQueueProcessor = Substitute.For<ISteamWorkshopQueueProcessor>();
            _mockLogger = Substitute.For<ILoggerService>();
            _mockSteamApiClient = Substitute.For<ISteamApiClient>();
            
            _enricher = new ModInfoEnricher(_mockModListManager);

            var mockNavigation = Substitute.For<IWebNavigationService>();
            var mockDownloaderParent = Substitute.For<RimSharp.Features.WorkshopDownloader.ViewModels.DownloaderViewModel>(
                mockNavigation,
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

            _browserViewModel = new BrowserViewModel(mockNavigation, mockDownloaderParent);
        }

        private DownloadQueueViewModel CreateViewModel()
        {
            return new DownloadQueueViewModel(
                _mockQueueService,
                _mockModService,
                _mockDialogService,
                _mockUpdateChecker,
                _mockSteamCmdService,
                _browserViewModel,
                () => CancellationToken.None,
                _mockModListManager,
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
            vm.DownloadList.Should().NotBeNull();
            vm.IsSteamCmdReady.Should().BeFalse();
            vm.IsOperationInProgress.Should().BeFalse();
        }

        [Fact]
        public async Task IsOperationInProgress_WhenChanged_ShouldRefreshDependentProperties()
        {
            // Arrange
            var vm = CreateViewModel();

            // Act
            vm.IsOperationInProgress = true;

            // Assert
            await WaitUntil(() => !vm.CanDownload);
            vm.CanDownload.Should().BeFalse();
            vm.CanAddMod.Should().BeFalse();
        }

        [Fact]
        public async Task SteamCmdReady_WhenChanged_ShouldUpdateIsSteamCmdReady()
        {
            // Arrange
            var vm = CreateViewModel();
            _mockSteamCmdService.IsSetupComplete.Returns(true);

            // Act
            _mockSteamCmdService.SetupStateChanged += Raise.Event<EventHandler<bool>>(_mockSteamCmdService, true);

            // Assert
            await WaitUntil(() => vm.IsSteamCmdReady);
            vm.IsSteamCmdReady.Should().BeTrue();
        }

        private async Task WaitUntil(Func<bool> condition, int timeoutMs = 2000)
        {
            var start = DateTime.Now;
            while (!condition() && (DateTime.Now - start).TotalMilliseconds < timeoutMs)
            {
                await Task.Delay(10);
            }
        }
    }
}
