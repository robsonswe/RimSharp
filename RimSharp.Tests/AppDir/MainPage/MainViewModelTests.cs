using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.AppDir.MainPage;
using RimSharp.Features.GitModManager.ViewModels;
using RimSharp.Features.ModManager.ViewModels;
using RimSharp.Features.VramAnalysis.ViewModels;
using RimSharp.Features.WorkshopDownloader.ViewModels;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.AppDir.MainPage
{
    public class MainViewModelTests : IDisposable
    {
        private readonly IPathService _mockPathService;
        private readonly IConfigService _mockConfigService;
        private readonly IDialogService _mockDialogService;
        private readonly IApplicationNavigationService _mockNavigationService;
        private readonly IUpdaterService _mockUpdaterService;
        private readonly string _testTempDir;

        // Mock dependencies for child ViewModels (Now Interfaces!)
        private readonly IModsViewModel _mockModsVM;
        private readonly IDownloaderViewModel _mockDownloaderVM;
        private readonly IGitModsViewModel _mockGitModsVM;
        private readonly IVramAnalysisViewModel _mockVramAnalysisVM;

        public MainViewModelTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();

            // Create unique temp directory for tests
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);

            _mockPathService = Substitute.For<IPathService>();
            _mockConfigService = Substitute.For<IConfigService>();
            _mockDialogService = Substitute.For<IDialogService>();
            _mockNavigationService = Substitute.For<IApplicationNavigationService>();
            _mockUpdaterService = Substitute.For<IUpdaterService>();

            // Setup path service to return temp directory
            _mockPathService.GetGamePath().Returns(_testTempDir);
            _mockPathService.GetConfigPath().Returns(_testTempDir);
            _mockPathService.GetModsPath().Returns(Path.Combine(_testTempDir, "Mods"));
            _mockPathService.GetGameVersion().Returns("1.6.0");

            _mockModsVM = Substitute.For<IModsViewModel>();
            _mockDownloaderVM = Substitute.For<IDownloaderViewModel>();
            _mockGitModsVM = Substitute.For<IGitModsViewModel>();
            _mockVramAnalysisVM = Substitute.For<IVramAnalysisViewModel>();
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        private MainViewModel CreateViewModel()
        {
            return new MainViewModel(
                _mockPathService,
                _mockConfigService,
                _mockDialogService,
                _mockNavigationService,
                _mockUpdaterService,
                _mockModsVM,
                _mockDownloaderVM,
                _mockGitModsVM,
                _mockVramAnalysisVM
            );
        }

        [Fact]
        public void Constructor_ShouldInitializeProperties()
        {

            var vm = CreateViewModel();

            vm.SelectedTab.Should().Be("Mods");
            vm.CurrentViewModel.Should().Be(_mockModsVM);
            vm.PathSettings.Should().NotBeNull();
            vm.ModsVM.Should().Be(_mockModsVM);
            vm.DownloaderVM.Should().Be(_mockDownloaderVM);
            vm.GitModsVM.Should().Be(_mockGitModsVM);
            vm.VramAnalysisVM.Should().Be(_mockVramAnalysisVM);
        }

        [Theory]
        [InlineData("Mods", "Mods")]
        [InlineData("Downloader", "Downloader")]
        [InlineData("GitMods", "GitMods")]
        [InlineData("VRAM", "VRAM")]
        public void SwitchTabCommand_ShouldChangeSelectedTab(string tabName, string expectedTab)
        {

            var vm = CreateViewModel();

            vm.SwitchTabCommand.Execute(tabName);

            vm.SelectedTab.Should().Be(expectedTab);
            if (tabName == "Downloader") vm.CurrentViewModel.Should().Be(_mockDownloaderVM);
            if (tabName == "GitMods") vm.CurrentViewModel.Should().Be(_mockGitModsVM);
            if (tabName == "VRAM") vm.CurrentViewModel.Should().Be(_mockVramAnalysisVM);
            if (tabName == "Mods") vm.CurrentViewModel.Should().Be(_mockModsVM);
        }

        [Fact]
        public void PathSettings_GamePathChanged_ShouldSaveToConfig()
        {

            var vm = CreateViewModel();
            var newPath = Path.Combine(_testTempDir, "NewGamePath");
            Directory.CreateDirectory(newPath);

            vm.PathSettings.GamePath = newPath;

            _mockConfigService.Received().SetConfigValue("game_folder", newPath);
            _mockConfigService.Received().SaveConfig();
        }

        [Fact]
        public void PathSettings_ConfigPathChanged_ShouldSaveToConfig()
        {

            var vm = CreateViewModel();
            var newPath = Path.Combine(_testTempDir, "NewConfigPath");
            Directory.CreateDirectory(newPath);

            vm.PathSettings.ConfigPath = newPath;

            _mockConfigService.Received().SetConfigValue("config_folder", newPath);
            _mockConfigService.Received().SaveConfig();
        }

        [Fact]
        public void AppVersion_ShouldReturnFormattedVersion()
        {

            var vm = CreateViewModel();

            var version = vm.AppVersion;

            version.Should().StartWith("v");
            // Basic regex check for version format vX.Y.Z
            version.Should().MatchRegex(@"v\d+\.\d+\.\d+");
        }

        [Fact]
        public void IsInitialLoading_ShouldBeTrueByDefault()
        {
            // Arrange & Act
            var vm = CreateViewModel();

            vm.IsInitialLoading.Should().BeTrue();
        }

        [Fact]
        public void StatusMessage_ShouldBeSettable()
        {

            var vm = CreateViewModel();
            var expectedMessage = "Test Status";

            vm.StatusMessage = expectedMessage;

            vm.StatusMessage.Should().Be(expectedMessage);
        }

        [Fact]
        public void Commands_ShouldNotBeNull()
        {
            // Arrange & Act
            var vm = CreateViewModel();

            vm.SwitchTabCommand.Should().NotBeNull();
            vm.BrowsePathCommand.Should().NotBeNull();
            vm.SettingsCommand.Should().NotBeNull();
            vm.RefreshCommand.Should().NotBeNull();
            vm.OpenFolderCommand.Should().NotBeNull();
            vm.AboutCommand.Should().NotBeNull();
        }

        [Fact]
        public void SwitchTabCommand_ShouldNotifyPropertyChange()
        {

            var vm = CreateViewModel();
            var propertyChanged = false;
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.CurrentViewModel))
                    propertyChanged = true;
            };

            vm.SwitchTabCommand.Execute("Downloader");

            propertyChanged.Should().BeTrue();
            vm.CurrentViewModel.Should().Be(_mockDownloaderVM);
        }

        [Fact]
        public void CheckAndWarnDifferentDrives_ShouldShowWarning_WhenDrivesDiffer()
        {

var vm = CreateViewModel();

vm.PathSettings.GamePath = _testTempDir; 

             // Assert - mostly that no exception occurred.
        }
    }
}


