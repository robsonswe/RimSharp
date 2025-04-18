using RimSharp.Infrastructure.Dialog;
using System;
using System.Windows;
using System.Diagnostics; // Keep for Debug messages

namespace RimSharp.AppDir.Dialogs
{
    public partial class InputDialogView : BaseDialog
    {
        public InputDialogView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        public InputDialogView(InputDialogViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is DialogViewModelBase oldVm)
            {
                oldVm.RequestCloseDialog -= ViewModel_RequestCloseDialog;
            }
            if (e.NewValue is DialogViewModelBase newVm)
            {
                newVm.RequestCloseDialog += ViewModel_RequestCloseDialog;
            }
        }

        private void ViewModel_RequestCloseDialog(object sender, EventArgs e)
        {
            // Set DialogResult based on ViewModel before closing
            if (DataContext is DialogViewModelBase<MessageDialogResult> vm)
            {
                this.DialogResult = vm.DialogResult switch
                {
                    MessageDialogResult.OK => true,
                    MessageDialogResult.Cancel => false,
                    _ => null
                };
                Debug.WriteLine($"[InputDialogView] Setting DialogResult to {this.DialogResult} based on VM result {vm.DialogResult}");
            }

            // Owner activation is now handled by BaseDialog.OnClosing

            Debug.WriteLine($"[InputDialogView] Calling Close() for {this.Title}.");
            this.Close(); // This will trigger BaseDialog.OnClosing
        }

        protected override void OnClosed(EventArgs e)
        {
            if (DataContext is DialogViewModelBase vm)
            {
                vm.RequestCloseDialog -= ViewModel_RequestCloseDialog;
            }
            DataContextChanged -= OnDataContextChanged;
            base.OnClosed(e);
            Debug.WriteLine($"[InputDialogView] Closed and cleaned up: {this.Title}");
        }
    }
}
