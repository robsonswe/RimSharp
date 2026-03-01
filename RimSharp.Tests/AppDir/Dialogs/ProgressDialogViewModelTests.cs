using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using RimSharp.AppDir.Dialogs;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.AppDir.Dialogs
{
    public class ProgressDialogViewModelTests
    {
        public ProgressDialogViewModelTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaFact]
        public void UpdateProgress_ShouldUpdateProperties()
        {
            // Arrange
            var vm = new ProgressDialogViewModel("Title", "Message");

            // Act
            vm.UpdateProgress(50, "New Message");

            // Assert
            vm.Progress.Should().Be(50);
            vm.Message.Should().Be("New Message");
            vm.IsIndeterminate.Should().BeFalse();
        }

        [AvaloniaFact]
        public void CompleteOperation_ShouldSetResultAndClose()
        {
            // Arrange
            var vm = new ProgressDialogViewModel("Title", "Message");
            bool closeRequested = false;
            vm.RequestCloseDialog += (s, e) => closeRequested = true;

            // Act
            vm.CompleteOperation("Done");

            // Assert
            vm.DialogResult.Should().BeTrue();
            vm.DialogResultForWindow.Should().BeTrue();
            vm.Message.Should().Be("Done");
            closeRequested.Should().BeTrue();
        }

        [AvaloniaFact]
        public void CancelCommand_Execute_ShouldCancelAndClose()
        {
            // Arrange
            var vm = new ProgressDialogViewModel("Title", "Message", canCancel: true);
            bool closeRequested = false;
            bool cancelNotified = false;
            vm.RequestCloseDialog += (s, e) => closeRequested = true;
            vm.Cancelled += (s, e) => cancelNotified = true;

            // Act
            vm.CancelCommand.Execute(null);

            // Assert
            vm.CancellationToken.IsCancellationRequested.Should().BeTrue();
            vm.DialogResult.Should().BeFalse();
            vm.DialogResultForWindow.Should().BeFalse();
            cancelNotified.Should().BeTrue();
            closeRequested.Should().BeTrue();
        }

        [AvaloniaFact]
        public void Dispose_ShouldCancelToken()
        {
            // Arrange
            var vm = new ProgressDialogViewModel("Title", "Message");
            var token = vm.CancellationToken;

            // Act
            vm.Dispose();

            // Assert
            token.IsCancellationRequested.Should().BeTrue();
        }
    }
}
