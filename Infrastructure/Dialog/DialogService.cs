#nullable enable
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
using RimSharp.Features.ModManager.Dialogs.Strip;

namespace RimSharp.Infrastructure.Dialog
{
    public class DialogService : IDialogService
    {
        /// <summary>
        /// Manually centers a window on the application's main window.
        /// If no valid main window is found, it relies on the window's default startup location.
        /// </summary>
        private void CenterWindowOnMainWindow(Window window)
        {
            var mainWindow = Application.Current.MainWindow;
            // Proceed only if there is a main window that is visible and not minimized.
            if (mainWindow != null && mainWindow.IsVisible && mainWindow.WindowState == WindowState.Normal)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;

                // We must use the 'Loaded' event to ensure ActualWidth and ActualHeight have been calculated.
                RoutedEventHandler loadedHandler = null;
                loadedHandler = (s, e) =>
                {
                    // Unsubscribe immediately to prevent this from running again.
                    window.Loaded -= loadedHandler;

                    // Calculate the center position.
                    window.Left = mainWindow.Left + (mainWindow.ActualWidth - window.ActualWidth) / 2;
                    window.Top = mainWindow.Top + (mainWindow.ActualHeight - window.ActualHeight) / 2;
                };

                window.Loaded += loadedHandler;
            }
            // If there's no valid main window, the window will use the default location
            // set in its constructor (which we previously set to CenterScreen).
        }

        private MessageDialogResult ShowDialogInternal(MessageDialogViewModel viewModel)
        {
            var dialog = new MessageDialogView(viewModel);
            CenterWindowOnMainWindow(dialog); // Center the dialog
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
            var dialog = new UpdateCheckDialogView(viewModel);
            CenterWindowOnMainWindow(dialog); // Center the dialog
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

        public ProgressDialogViewModel ShowProgressDialog(string title, string message, bool canCancel = false, bool isIndeterminate = true, CancellationTokenSource? cts = null, bool closeable = true)
        {
            var viewModel = new ProgressDialogViewModel(title, message, canCancel, isIndeterminate, cts);
            var dialog = new ProgressDialogView(viewModel)
            {
                Closeable = closeable
            };
            CenterWindowOnMainWindow(dialog); // Center the dialog
            dialog.Show();
            return viewModel;
        }

        public (MessageDialogResult Result, string Input) ShowInputDialog(string title, string message, string defaultInput = "")
        {
            var viewModel = new InputDialogViewModel(title, message, defaultInput);
            var dialog = new InputDialogView(viewModel);
            CenterWindowOnMainWindow(dialog); // Center the dialog
            dialog.ShowDialog();
            return (viewModel.DialogResult, viewModel.Input);
        }

        public (bool, IEnumerable<string>?) ShowStripModsDialog(StripDialogViewModel viewModel)
        {
            var dialog = new StripModsDialogView(viewModel);
            CenterWindowOnMainWindow(dialog); // Center the dialog
            dialog.ShowDialog();
            return viewModel.DialogResult;
        }

        public ModCustomizationResult ShowCustomizeModDialog(CustomizeModDialogViewModel viewModel)
        {
            var dialog = new CustomizeModDialogView(viewModel);
            CenterWindowOnMainWindow(dialog); // Center the dialog
            dialog.ShowDialog();
            return viewModel.DialogResult;
        }
        public ModFilterDialogResult ShowModFilterDialog(ModFilterDialogViewModel viewModel)
        {
            var dialog = new ModFilterDialogView(viewModel);
            CenterWindowOnMainWindow(dialog); // Center the dialog
            dialog.ShowDialog();
            return viewModel.DialogResult;
        }
        public ModReplacementDialogResult ShowModReplacementDialog(ModReplacementDialogViewModel viewModel)
        {
            var dialog = new ModReplacementDialogView(viewModel);
            CenterWindowOnMainWindow(dialog); // Center the dialog
            dialog.ShowDialog();
            return viewModel.DialogResult;
        }
        public DependencyResolutionDialogResult ShowDependencyResolutionDialog(DependencyResolutionDialogViewModel viewModel)
        {
            DependencyResolutionDialogResult result = DependencyResolutionDialogResult.Cancel;
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new DependencyResolutionDialogView(viewModel);
                CenterWindowOnMainWindow(dialog); // Center the dialog
                dialog.ShowDialog();
                result = viewModel.DialogResult;
            });
            return result;
        }

        public MissingModSelectionDialogOutput ShowMissingModSelectionDialog(MissingModSelectionDialogViewModel viewModel)
        {
            MissingModSelectionDialogOutput? result = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new MissingModSelectionDialogView(viewModel);
                CenterWindowOnMainWindow(dialog); // Center the dialog
                dialog.ShowDialog();
                result = viewModel.DialogResult ?? new MissingModSelectionDialogOutput();
            });
            return result ?? new MissingModSelectionDialogOutput();
        }

        public List<string>? ShowCollectionDialog(CollectionDialogViewModel viewModel)
        {
            List<string>? result = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new CollectionDialogView(viewModel);
                CenterWindowOnMainWindow(dialog); // Center the dialog
                dialog.ShowDialog();
                result = viewModel.DialogResult;
            });
            return result;
        }
    }
}