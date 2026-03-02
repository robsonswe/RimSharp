using Avalonia.Headless.XUnit;
using FluentAssertions;
using RimSharp.AppDir.Dialogs;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.AppDir.Dialogs
{
    public class InputDialogViewModelTests
    {
        public InputDialogViewModelTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaFact]
        public void OkCommand_WhenInputIsEmpty_ShouldBeDisabled()
        {

            var vm = new InputDialogViewModel("Title", "Message");

            vm.Input = "";

            vm.OkCommand.CanExecute(null).Should().BeFalse();
        }

        [AvaloniaFact]
        public void OkCommand_WhenInputIsNotEmpty_ShouldBeEnabled()
        {

            var vm = new InputDialogViewModel("Title", "Message");

            vm.Input = "Some text";

            vm.OkCommand.CanExecute(null).Should().BeTrue();
        }

        [AvaloniaFact]
        public void OkCommand_Execute_ShouldSetResultAndClose()
        {

            var vm = new InputDialogViewModel("Title", "Message", "Default");
            bool closeRequested = false;
            vm.RequestCloseDialog += (s, e) => closeRequested = true;

            vm.OkCommand.Execute(null);

            vm.DialogResult.Should().Be(MessageDialogResult.OK);
            vm.DialogResultForWindow.Should().BeTrue();
            closeRequested.Should().BeTrue();
        }

        [AvaloniaFact]
        public void CancelCommand_Execute_ShouldSetResultAndClose()
        {

            var vm = new InputDialogViewModel("Title", "Message");
            bool closeRequested = false;
            vm.RequestCloseDialog += (s, e) => closeRequested = true;

            vm.CancelCommand.Execute(null);

            vm.DialogResult.Should().Be(MessageDialogResult.Cancel);
            vm.DialogResultForWindow.Should().BeFalse();
            closeRequested.Should().BeTrue();
        }
    }
}

