using System.Windows;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Features.WorkshopDownloader.Dialogs.UpdateCheck;
using System.Threading;
using RimSharp.Features.ModManager.Dialogs.CustomizeMod;
using RimSharp.Features.ModManager.Dialogs.Filter;
using RimSharp.Features.ModManager.Dialogs.Replacements;
using RimSharp.Features.ModManager.Dialogs.Dependencies;
using RimSharp.Features.ModManager.Dialogs.MissingMods;
using RimSharp.Features.WorkshopDownloader.Dialogs.Collection;
using System.Collections.Generic;

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

        public ProgressDialogViewModel ShowProgressDialog(string title, string message, bool canCancel = false, bool isIndeterminate = true, CancellationTokenSource cts = null, bool closeable = true)
        {
            var viewModel = new ProgressDialogViewModel(title, message, canCancel, isIndeterminate, cts);
            var dialog = new ProgressDialogView(viewModel)
            {
                Owner = Application.Current?.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Closeable = closeable
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
        public ModReplacementDialogResult ShowModReplacementDialog(ModReplacementDialogViewModel viewModel)
        {
            var dialog = new ModReplacementDialogView(viewModel)
            {
                Owner = Application.Current?.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            dialog.ShowDialog();
            return viewModel.DialogResult;
        }
        public DependencyResolutionDialogResult ShowDependencyResolutionDialog(DependencyResolutionDialogViewModel viewModel)
        {
            DependencyResolutionDialogResult result = DependencyResolutionDialogResult.Cancel;
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new DependencyResolutionDialogView(viewModel)
                {
                    Owner = Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                dialog.ShowDialog(); // Blocks until closed
                result = viewModel.DialogResult;
            });
            return result;
        }

        public MissingModSelectionDialogOutput ShowMissingModSelectionDialog(MissingModSelectionDialogViewModel viewModel)
        {
            MissingModSelectionDialogOutput result = null; // Default to null initially
            Application.Current.Dispatcher.Invoke(() =>
            { // Ensure UI thread
                var dialog = new MissingModSelectionDialogView(viewModel)
                {
                    Owner = Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                // ShowDialog blocks until closed and DialogResult is set (or window is closed manually)
                dialog.ShowDialog();
                // Retrieve the strongly-typed result from the ViewModel AFTER the dialog is closed
                result = viewModel.DialogResult ?? new MissingModSelectionDialogOutput(); // Use default if VM result is null
            });
            return result ?? new MissingModSelectionDialogOutput(); // Return default if dispatcher invoke fails?
        }

        public List<string>? ShowCollectionDialog(CollectionDialogViewModel viewModel)
        {
            List<string>? result = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new CollectionDialogView(viewModel)
                {
                    Owner = Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                // ShowDialog blocks execution until the dialog is closed.
                // The DialogResult (bool?) on the window will be set by the View's code-behind
                // based on the ViewModel's RequestCloseDialog event.
                dialog.ShowDialog();

                // We retrieve the actual List<string> result directly from the ViewModel
                // after the dialog closes.
                result = viewModel.DialogResult;
            });
            // Return the list of IDs (which could be null if cancelled)
            return result;
        }

    }
}