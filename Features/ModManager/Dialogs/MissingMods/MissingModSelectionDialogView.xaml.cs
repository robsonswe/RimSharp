#nullable enable
using RimSharp.AppDir.Dialogs; // For DialogViewModelBase<T>
using RimSharp.Infrastructure.Dialog; // For BaseDialog
using System;
using System.Collections.Generic; // For List<string>
using System.Diagnostics;
using System.Windows;

namespace RimSharp.Features.ModManager.Dialogs.MissingMods
{
    public partial class MissingModSelectionDialogView : BaseDialog
    {
        public MissingModSelectionDialogView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        // Constructor accepting ViewModel
        public MissingModSelectionDialogView(MissingModSelectionDialogViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is DialogViewModelBase oldVm)
            {
                oldVm.RequestCloseDialog -= ViewModel_RequestCloseDialog;
                // Consider disposing the old VM if this view manages its lifetime
                // (oldVm as IDisposable)?.Dispose();
            }
            if (e.NewValue is DialogViewModelBase newVm)
            {
                newVm.RequestCloseDialog += ViewModel_RequestCloseDialog;
            }
        }

        private void ViewModel_RequestCloseDialog(object? sender, EventArgs e)
        {
             // ViewModel now sets DialogResultForWindow internally using MapResultToWindowResult
             if (DataContext is DialogViewModelBase vm && vm.DialogResultForWindow.HasValue)
             {
                 try
                 {
                    // Standard WPF DialogResult mechanism
                    this.DialogResult = vm.DialogResultForWindow;
                    Debug.WriteLine($"[MissingModSelectionDialogView] Setting DialogResult to {this.DialogResult} based on VM request.");
                 }
                 catch (InvalidOperationException ex)
                 {
                     // This can happen if the window wasn't shown with ShowDialog()
                     Debug.WriteLine($"[MissingModSelectionDialogView] Failed to set DialogResult: {ex.Message}. Window might not have been shown modally.");
                 }
             }
             else
             {
                 Debug.WriteLine($"[MissingModSelectionDialogView] ViewModel requested close, but DialogResultForWindow was null. Closing without setting DialogResult.");
             }


            Debug.WriteLine($"[MissingModSelectionDialogView] Calling Close() for {this.Title}.");
            this.Close(); // This call will trigger BaseDialog.OnClosing
        }

        protected override void OnClosed(EventArgs e)
        {
            if (DataContext is DialogViewModelBase vm)
            {
                vm.RequestCloseDialog -= ViewModel_RequestCloseDialog;
                 // Dispose VM if appropriate
                (vm as IDisposable)?.Dispose();
            }
            DataContextChanged -= OnDataContextChanged; // Unsubscribe DataContext listener too
            base.OnClosed(e);
            Debug.WriteLine($"[MissingModSelectionDialogView] Closed and cleaned up: {this.Title}");
        }
    }
}
