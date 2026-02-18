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
using System.ComponentModel;
using System;

namespace RimSharp.Infrastructure.Dialog
{
    public class DialogService : IDialogService, INotifyPropertyChanged
    {
        private int _openDialogCount;
        public bool IsAnyDialogOpen => _openDialogCount > 0;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void IncrementDialogCount()
        {
            _openDialogCount++;
            if (_openDialogCount == 1) OnPropertyChanged(nameof(IsAnyDialogOpen));
        }

        private void DecrementDialogCount()
        {
            _openDialogCount--;
            if (_openDialogCount == 0) OnPropertyChanged(nameof(IsAnyDialogOpen));
        }

        private MessageDialogResult ShowDialogInternal(MessageDialogViewModel viewModel)
        {
            IncrementDialogCount();
            try
            {
                var dialog = new MessageDialogView(viewModel)
                {
                    Owner = Application.Current.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                dialog.ShowDialog();
                return viewModel.DialogResult;
            }
            finally
            {
                DecrementDialogCount();
            }
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
            IncrementDialogCount();
            try
            {
                var dialog = new UpdateCheckDialogView(viewModel)
                {
                    Owner = Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                dialog.ShowDialog();
                return viewModel.DialogResult;
            }
            finally
            {
                DecrementDialogCount();
            }
        }

        public void ShowMessageWithCopy(string title, string message, MessageDialogType dialogType = MessageDialogType.Information)
        {
            var viewModel = new MessageDialogViewModel(title, message, dialogType)
            {
                ShowCopyButton = true
            };
            ShowDialogInternal(viewModel);
        }

        public ProgressDialogViewModel ShowProgressDialog(string title, string message, bool canCancel = false, bool isIndeterminate = true, CancellationTokenSource? cts = null, bool closeable = false, bool showInTaskbar = true)
        {
            var viewModel = new ProgressDialogViewModel(title, message, canCancel, isIndeterminate, cts);
            var dialog = new ProgressDialogView(viewModel)
            {
                Owner = Application.Current?.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Closeable = closeable,
                ShowInTaskbar = showInTaskbar
            };

            IncrementDialogCount();
            dialog.Closed += (s, e) => DecrementDialogCount();

            dialog.Show();
            return viewModel;
        }

        public (MessageDialogResult Result, string Input) ShowInputDialog(string title, string message, string defaultInput = "")
        {
            var viewModel = new InputDialogViewModel(title, message, defaultInput);
            IncrementDialogCount();
            try
            {
                var dialog = new InputDialogView(viewModel)
                {
                    Owner = Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                dialog.ShowDialog();
                return (viewModel.DialogResult, viewModel.Input);
            }
            finally
            {
                DecrementDialogCount();
            }
        }

        public (bool, IEnumerable<string>?) ShowStripModsDialog(StripDialogViewModel viewModel)
        {
            IncrementDialogCount();
            try
            {
                var dialog = new StripModsDialogView(viewModel)
                {
                    Owner = Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                dialog.ShowDialog();
                return viewModel.DialogResult;
            }
            finally
            {
                DecrementDialogCount();
            }
        }

        public ModCustomizationResult ShowCustomizeModDialog(CustomizeModDialogViewModel viewModel)
        {
            IncrementDialogCount();
            try
            {
                var dialog = new CustomizeModDialogView(viewModel)
                {
                    Owner = Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                dialog.ShowDialog();
                return viewModel.DialogResult;
            }
            finally
            {
                DecrementDialogCount();
            }
        }
        public ModFilterDialogResult ShowModFilterDialog(ModFilterDialogViewModel viewModel)
        {
            IncrementDialogCount();
            try
            {
                var dialog = new ModFilterDialogView(viewModel)
                {
                    Owner = Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                dialog.ShowDialog();
                return viewModel.DialogResult;
            }
            finally
            {
                DecrementDialogCount();
            }
        }
        public ModReplacementDialogResult ShowModReplacementDialog(ModReplacementDialogViewModel viewModel)
        {
            IncrementDialogCount();
            try
            {
                var dialog = new ModReplacementDialogView(viewModel)
                {
                    Owner = Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                dialog.ShowDialog();
                return viewModel.DialogResult;
            }
            finally
            {
                DecrementDialogCount();
            }
        }
        public DependencyResolutionDialogResult ShowDependencyResolutionDialog(DependencyResolutionDialogViewModel viewModel)
        {
            DependencyResolutionDialogResult result = DependencyResolutionDialogResult.Cancel;
            Application.Current.Dispatcher.Invoke(() =>
            {
                IncrementDialogCount();
                try
                {
                    var dialog = new DependencyResolutionDialogView(viewModel)
                    {
                        Owner = Application.Current?.MainWindow,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };
                    dialog.ShowDialog();
                    result = viewModel.DialogResult;
                }
                finally
                {
                    DecrementDialogCount();
                }
            });
            return result;
        }

        public MissingModSelectionDialogOutput ShowMissingModSelectionDialog(MissingModSelectionDialogViewModel viewModel)
        {
            MissingModSelectionDialogOutput? result = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                IncrementDialogCount();
                try
                {
                    var dialog = new MissingModSelectionDialogView(viewModel)
                    {
                        Owner = Application.Current?.MainWindow,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };
                    dialog.ShowDialog();
                    result = viewModel.DialogResult ?? new MissingModSelectionDialogOutput();
                }
                finally
                {
                    DecrementDialogCount();
                }
            });
            return result ?? new MissingModSelectionDialogOutput();
        }

        public List<string>? ShowCollectionDialog(CollectionDialogViewModel viewModel)
        {
            List<string>? result = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                IncrementDialogCount();
                try
                {
                    var dialog = new CollectionDialogView(viewModel)
                    {
                        Owner = Application.Current?.MainWindow,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };
                    dialog.ShowDialog();
                    result = viewModel.DialogResult;
                }
                finally
                {
                    DecrementDialogCount();
                }
            });
            return result;
        }
    }
}
