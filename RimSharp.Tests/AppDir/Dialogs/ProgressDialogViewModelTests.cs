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

            var vm = new ProgressDialogViewModel("Title", "Message");

            vm.UpdateProgress(50, "New Message");

            vm.Progress.Should().Be(50);
            vm.Message.Should().Be("New Message");
            vm.IsIndeterminate.Should().BeFalse();
        }

        [AvaloniaFact]
        public void CompleteOperation_ShouldSetResultAndClose()
        {

            var vm = new ProgressDialogViewModel("Title", "Message");
            bool closeRequested = false;
            vm.RequestCloseDialog += (s, e) => closeRequested = true;

            vm.CompleteOperation("Done");

            vm.DialogResult.Should().BeTrue();
            vm.DialogResultForWindow.Should().BeTrue();
            vm.Message.Should().Be("Done");
            closeRequested.Should().BeTrue();
        }

        [AvaloniaFact]
        public void CancelCommand_Execute_ShouldCancelAndClose()
        {

            var vm = new ProgressDialogViewModel("Title", "Message", canCancel: true);
            bool closeRequested = false;
            bool cancelNotified = false;
            vm.RequestCloseDialog += (s, e) => closeRequested = true;
            vm.Cancelled += (s, e) => cancelNotified = true;

            vm.CancelCommand.Execute(null);

            vm.CancellationToken.IsCancellationRequested.Should().BeTrue();
            vm.DialogResult.Should().BeFalse();
            vm.DialogResultForWindow.Should().BeFalse();
            cancelNotified.Should().BeTrue();
            closeRequested.Should().BeTrue();
        }

        [AvaloniaFact]
        public void Dispose_ShouldCancelToken()
        {

            var vm = new ProgressDialogViewModel("Title", "Message");
            var token = vm.CancellationToken;

            vm.Dispose();

            token.IsCancellationRequested.Should().BeTrue();
        }
    }
}

