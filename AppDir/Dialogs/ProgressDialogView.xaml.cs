using RimSharp.Infrastructure.Dialog;
using System;
using System.Windows;
using System.Diagnostics;

namespace RimSharp.AppDir.Dialogs
{
    public partial class ProgressDialogView : BaseDialog
    {
        // No _viewModel field needed
        // No _isClosing field needed

        public ProgressDialogView() // Keep default constructor
        {
            InitializeComponent();
            Debug.WriteLine($"[ProgressDialogView] Default constructor finished.");
            // Optional: Subscribe/unsubscribe DataContextChanged here if needed for *other* reasons
        }

        public ProgressDialogView(ProgressDialogViewModel viewModel) : this() // Chain default constructor
        {
            DataContext = viewModel; // BaseDialog handles subscription
            Debug.WriteLine($"[ProgressDialogView] Constructor with VM finished for {viewModel?.Title}. DataContext set.");
        }

        // Optional: Keep OnDataContextChanged ONLY if needed for other reasons
        /*
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Debug.WriteLine($"[ProgressDialogView] OnDataContextChanged. New VM: {e.NewValue?.GetType().Name}");
        }
        */

        // No ViewModel_RequestCloseDialog handler needed

        // No manual unsubscription needed in OnClosed
        protected override void OnClosed(EventArgs e)
        {
            Debug.WriteLine($"[ProgressDialogView] OnClosed for {this.Title}.");
            // Optional: Unsubscribe DataContextChanged if you kept it
            // Optional: Consider if VM disposal needs explicit handling here or rely on creator/DI scope.
            // (DataContext as IDisposable)?.Dispose(); // Generally avoid unless View exclusively owns VM
            base.OnClosed(e); // Call base for its cleanup
        }
    }
}
