using RimSharp.Infrastructure.Dialog;
using System;
using System.Windows;
using System.Diagnostics; // Keep for Debug messages

namespace RimSharp.AppDir.Dialogs
{
    public partial class ProgressDialogView : BaseDialog
    {
        public ProgressDialogView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        public ProgressDialogView(ProgressDialogViewModel viewModel) : this()
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
             // Progress dialog might not set DialogResult in the same way,
             // but owner activation is handled by BaseDialog.OnClosing anyway.

            Debug.WriteLine($"[ProgressDialogView] Calling Close() for {this.Title}.");
            this.Close(); // This will trigger BaseDialog.OnClosing
        }

        protected override void OnClosed(EventArgs e)
        {
            if (DataContext is DialogViewModelBase vm)
            {
                vm.RequestCloseDialog -= ViewModel_RequestCloseDialog;
            }
            DataContextChanged -= OnDataContextChanged;
            // Reminder: ViewModel disposal should be managed by its creator (likely DialogService or caller)
            // or via DI container scope management. Avoid disposing VM here unless the View truly owns it.
            base.OnClosed(e);
            Debug.WriteLine($"[ProgressDialogView] Closed and cleaned up: {this.Title}");
        }
    }
}
