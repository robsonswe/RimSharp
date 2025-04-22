#nullable enable
using RimSharp.AppDir.Dialogs;
using RimSharp.Infrastructure.Dialog; // For BaseDialog
using System.Diagnostics;
using System;

namespace RimSharp.Features.WorkshopDownloader.Dialogs.Collection
{
    public partial class CollectionDialogView : BaseDialog // Inherit from your BaseDialog
    {
        private readonly DialogViewModelBase _viewModel; // Keep reference

        public CollectionDialogView(CollectionDialogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            _viewModel = viewModel; // Store viewModel

            // Subscribe to the close request event
             viewModel.RequestCloseDialog += ViewModel_RequestCloseDialog;

             // Ensure owner is set if possible (handled by DialogService now mostly)
            // Owner = System.Windows.Application.Current?.MainWindow;
        }

        private void ViewModel_RequestCloseDialog(object? sender, EventArgs e)
        {
            // Set the Window's DialogResult based on the ViewModel's mapped property
             this.DialogResult = _viewModel.DialogResultForWindow;

            // No need to manually call Close() here, setting DialogResult usually handles it.
            // If it doesn't automatically close, uncomment the line below.
             // this.Close();
             Debug.WriteLine($"[CollectionDialogView] Close requested by ViewModel. DialogResult set to: {this.DialogResult}");
        }

         // Clean up subscription when the window is closed
         protected override void OnClosed(EventArgs e)
         {
             if (_viewModel != null)
             {
                 _viewModel.RequestCloseDialog -= ViewModel_RequestCloseDialog;
             }
             base.OnClosed(e);
              Debug.WriteLine("[CollectionDialogView] Closed and unsubscribed from ViewModel.");
         }
    }
}
