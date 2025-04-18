using RimSharp.Infrastructure.Dialog;
using System;
using System.Windows;
using System.Diagnostics; // For Debug.WriteLine

namespace RimSharp.AppDir.Dialogs
{
    public partial class MessageDialogView : BaseDialog
    {
        public MessageDialogView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        // Optional: Constructor accepting ViewModel
        public MessageDialogView(MessageDialogViewModel viewModel) : this()
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
            // Set DialogResult based on ViewModel before closing if needed
            if (DataContext is DialogViewModelBase<MessageDialogResult> vm)
            {
                // Standard WPF DialogResult mechanism
                this.DialogResult = vm.DialogResult switch
                {
                    MessageDialogResult.OK => true,
                    MessageDialogResult.Yes => true,
                    MessageDialogResult.Cancel => false,
                    MessageDialogResult.No => false,
                    _ => null
                };
                Debug.WriteLine($"[MessageDialogView] Setting DialogResult to {this.DialogResult} based on VM result {vm.DialogResult}");
            }

            Debug.WriteLine($"[MessageDialogView] Calling Close() for {this.Title}.");
            this.Close(); // This call will trigger BaseDialog.OnClosing
        }

        // ... OnClosed ...
        protected override void OnClosed(EventArgs e)
        {
            if (DataContext is DialogViewModelBase vm)
            {
                vm.RequestCloseDialog -= ViewModel_RequestCloseDialog;
            }
            DataContextChanged -= OnDataContextChanged; // Unsubscribe DataContext listener too
            base.OnClosed(e);
            Debug.WriteLine($"[MessageDialogView] Closed and cleaned up: {this.Title}");
        }

    }
}