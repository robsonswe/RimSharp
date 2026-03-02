using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.AppDir.Dialogs
{
    public class AboutDialogViewModelTests
    {
        public AboutDialogViewModelTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaFact]
        public async Task CheckForUpdatesAsync_WhenUpdateAvailable_ShouldUpdateProperties()
        {

            var mockService = Substitute.For<IAppUpdaterService>();
            mockService.CheckForAppUpdateAsync().Returns((true, "v1.1.0", "https://github.com/release"));

            var vm = new AboutDialogViewModel(mockService);
            await Task.Delay(100);

            vm.IsNewVersionAvailable.Should().BeTrue();
            vm.UpdateStatus.Should().Contain("v1.1.0");
            vm.StatusColor.Should().Be("RimworldRedBrush");
            vm.ReleaseUrl.Should().Be("https://github.com/release");
        }

        [AvaloniaFact]
        public async Task CheckForUpdatesAsync_WhenUpToDate_ShouldUpdateProperties()
        {

            var mockService = Substitute.For<IAppUpdaterService>();
            mockService.CheckForAppUpdateAsync().Returns((false, "v1.0.0", null));

            var vm = new AboutDialogViewModel(mockService);
            await Task.Delay(100);

            vm.IsNewVersionAvailable.Should().BeFalse();
            vm.UpdateStatus.Should().Be("RimSharp is up to date");
            vm.StatusColor.Should().Be("RimworldDarkGreenBrush");
        }

        [AvaloniaFact]
        public void Close_ShouldSetDialogResult()
        {

            var mockService = Substitute.For<IAppUpdaterService>();
            var vm = new AboutDialogViewModel(mockService);
            bool closeRequested = false;
            vm.RequestCloseDialog += (s, e) => closeRequested = true;

            vm.Close();

            vm.DialogResult.Should().BeTrue();
            vm.DialogResultForWindow.Should().BeTrue();
            closeRequested.Should().BeTrue();
        }
    }
}


