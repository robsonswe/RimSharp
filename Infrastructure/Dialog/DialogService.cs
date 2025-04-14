using System.Windows;
using RimSharp.MyApp.Dialogs;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Features.WorkshopDownloader.Dialogs.UpdateCheck;
using System.Threading;
using RimSharp.Features.ModManager.Dialogs.CustomizeMod;
using RimSharp.Features.ModManager.Dialogs.Filter;

namespace RimSharp.Infrastructure.Dialog
{
    public class DialogService : IDialogService
    {
        private MessageDialogResult ShowDialogInternal(MessageDialogViewModel viewModel)
        {
            var dialog = new MessageDialogView(viewModel)
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            dialog.ShowDialog();
            return viewModel.DialogResult;
        }

        public void ShowInformation(string title, string message)
        {
            var viewModel = new MessageDialogViewModel(title, message, MessageDialogType.Information);
            ShowDialogInternal(viewModel);
        }

        public void ShowWarning(string title, string message)
        {
            var viewModel = new MessageDialogViewModel(title, message, MessageDialogType.Warning);
            ShowDialogInternal(viewModel);
        }

        public void ShowError(string title, string message)
        {
            var viewModel = new MessageDialogViewModel(title, message, MessageDialogType.Error);
            ShowDialogInternal(viewModel);
        }

        public MessageDialogResult ShowConfirmation(string title, string message, bool showCancel = false)
        {
            var viewModel = new MessageDialogViewModel(title, message, MessageDialogType.Question)
            {
                ShowOkButton = true,
                ShowCancelButton = showCancel
            };
            return ShowDialogInternal(viewModel);
        }

        public UpdateCheckDialogResult ShowUpdateCheckDialog(UpdateCheckDialogViewModel viewModel)
        {
            var dialog = new UpdateCheckDialogView(viewModel)
            {
                Owner = Application.Current?.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            dialog.ShowDialog();
            return viewModel.DialogResult;
        }

        public void ShowMessageWithCopy(string title, string message, MessageDialogType dialogType = MessageDialogType.Information)
        {
            var viewModel = new MessageDialogViewModel(title, message, dialogType)
            {
                ShowCopyButton = true
            };
            ShowDialogInternal(viewModel);
        }

        public ProgressDialogViewModel ShowProgressDialog(string title, string message, bool canCancel = false, bool isIndeterminate = true, CancellationTokenSource cts = null)
        {
            var viewModel = new ProgressDialogViewModel(title, message, canCancel, isIndeterminate, cts);
            var dialog = new ProgressDialogView(viewModel)
            {
                Owner = Application.Current?.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            dialog.Show();
            return viewModel;
        }

        public (MessageDialogResult Result, string Input) ShowInputDialog(string title, string message, string defaultInput = "")
        {
            var viewModel = new InputDialogViewModel(title, message, defaultInput);
            var dialog = new InputDialogView(viewModel)
            {
                Owner = Application.Current?.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            dialog.ShowDialog();
            return (viewModel.DialogResult, viewModel.Input);
        }

        public ModCustomizationResult ShowCustomizeModDialog(CustomizeModDialogViewModel viewModel)
        {
            var dialog = new CustomizeModDialogView(viewModel)
            {
                Owner = Application.Current?.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            dialog.ShowDialog();
            return viewModel.DialogResult;
        }
        public ModFilterDialogResult ShowModFilterDialog(ModFilterDialogViewModel viewModel)
        {
            var dialog = new ModFilterDialogView(viewModel)
            {
                Owner = Application.Current?.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            dialog.ShowDialog();
            return viewModel.DialogResult; // ViewModel handles setting this before closing
        }

    }
}