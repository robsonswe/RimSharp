using Avalonia.Headless.XUnit;
using FluentAssertions;
using RimSharp.AppDir.Dialogs;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.AppDir.Dialogs
{
    public class MessageDialogViewModelTests
    {
        public MessageDialogViewModelTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaFact]
        public void OkCommand_Execute_ShouldSetResultAndClose()
        {

            var vm = new MessageDialogViewModel("Title", "Message");
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

            var vm = new MessageDialogViewModel("Title", "Message");
            bool closeRequested = false;
            vm.RequestCloseDialog += (s, e) => closeRequested = true;

            vm.CancelCommand.Execute(null);

            vm.DialogResult.Should().Be(MessageDialogResult.Cancel);
            vm.DialogResultForWindow.Should().BeFalse();
            closeRequested.Should().BeTrue();
        }

        [AvaloniaFact]
        public void Properties_ShouldBeInitialized()
        {

            var vm = new MessageDialogViewModel("My Title", "My Message", MessageDialogType.Warning);

            vm.Title.Should().Be("My Title");
            vm.Message.Should().Be("My Message");
            vm.DialogType.Should().Be(MessageDialogType.Warning);
        }
    }
}

