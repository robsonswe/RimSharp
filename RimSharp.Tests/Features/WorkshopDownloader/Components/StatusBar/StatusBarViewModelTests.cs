using Avalonia.Headless.XUnit;
using FluentAssertions;
using RimSharp.Features.WorkshopDownloader.Components.StatusBar;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Features.WorkshopDownloader.Components.StatusBar
{
    public class StatusBarViewModelTests
    {
        public StatusBarViewModelTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaFact]
        public void SetStatus_ShouldUpdateMessage()
        {
            // Arrange
            var vm = new StatusBarViewModel();

            // Act
            vm.SetStatus("Ready");

            // Assert
            vm.StatusMessage.Should().Be("Ready");
        }

        [AvaloniaFact]
        public void ShowProgressBar_ShouldUpdateVisibility()
        {
            // Arrange
            var vm = new StatusBarViewModel();

            // Act
            vm.ShowProgressBar(false);

            // Assert
            vm.IsProgressBarVisible.Should().BeTrue();
            vm.IsIndeterminate.Should().BeFalse();
        }

        [AvaloniaFact]
        public void Reset_ShouldClearAll()
        {
            // Arrange
            var vm = new StatusBarViewModel();
            vm.SetStatus("Busy");
            vm.ShowProgressBar();

            // Act
            vm.Reset();

            // Assert
            vm.StatusMessage.Should().BeEmpty();
            vm.IsProgressBarVisible.Should().BeFalse();
            vm.Progress.Should().Be(0);
        }
    }
}
