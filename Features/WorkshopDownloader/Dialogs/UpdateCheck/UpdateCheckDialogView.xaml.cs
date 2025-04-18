using System.Windows;
using RimSharp.Infrastructure.Dialog;
using System;
using System.Diagnostics;

namespace RimSharp.Features.WorkshopDownloader.Dialogs.UpdateCheck
{
    // Inherit from BaseDialog instead of Window
    public partial class UpdateCheckDialogView : BaseDialog
    {
        // No _viewModel field needed
        // No _isClosing field needed

         public UpdateCheckDialogView() // Add default constructor if needed
        {
            InitializeComponent();
        }

        public UpdateCheckDialogView(UpdateCheckDialogViewModel viewModel) : this()
        {
            // The BaseDialog constructor handles DataContextChanged event subscription
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            Debug.WriteLine($"[UpdateCheckDialogView] Constructor with VM finished for {viewModel?.Title}. DataContext set.");
            // Title binding happens via Style in BaseDialog template
        }

        // No inline handler for RequestCloseDialog needed

         // BaseDialog handles cleanup in its OnClosed override
        protected override void OnClosed(EventArgs e)
        {
            Debug.WriteLine($"[UpdateCheckDialogView] OnClosed for {this.Title}.");
            // Optional: Add view-specific cleanup if needed
            // Example: ViewModel might need explicit cleanup if it holds resources
            // (DataContext as UpdateCheckDialogViewModel)?.Cleanup(); // If VM provides a Cleanup method
            base.OnClosed(e);
        }
    }
}
