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

            var vm = new StatusBarViewModel();

            vm.SetStatus("Ready");

            vm.StatusMessage.Should().Be("Ready");
        }

        [AvaloniaFact]
        public void ShowProgressBar_ShouldUpdateVisibility()
        {

            var vm = new StatusBarViewModel();

            vm.ShowProgressBar(false);

            vm.IsProgressBarVisible.Should().BeTrue();
            vm.IsIndeterminate.Should().BeFalse();
        }

        [AvaloniaFact]
        public void Reset_ShouldClearAll()
        {

            var vm = new StatusBarViewModel();
            vm.SetStatus("Busy");
            vm.ShowProgressBar();

            vm.Reset();

            vm.StatusMessage.Should().BeEmpty();
            vm.IsProgressBarVisible.Should().BeFalse();
            vm.Progress.Should().Be(0);
        }
    }
}

