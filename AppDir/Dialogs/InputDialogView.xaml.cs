using RimSharp.Infrastructure.Dialog;
using System;
using System.Windows;
using System.Diagnostics;

namespace RimSharp.AppDir.Dialogs
{
    public partial class InputDialogView : BaseDialog
    {
        // No _viewModel field needed
        // No _isClosing field needed

        public InputDialogView() // Keep default constructor
        {
            InitializeComponent();
            Debug.WriteLine($"[InputDialogView] Default constructor finished.");
            // Optional: Subscribe/unsubscribe DataContextChanged here if needed for *other* reasons
        }

        public InputDialogView(InputDialogViewModel viewModel) : this() // Chain default constructor
        {
            DataContext = viewModel; // BaseDialog handles subscription
            Debug.WriteLine($"[InputDialogView] Constructor with VM finished for {viewModel?.Title}. DataContext set.");
        }

        // Optional: Keep OnDataContextChanged ONLY if needed for other reasons
        /*
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Debug.WriteLine($"[InputDialogView] OnDataContextChanged. New VM: {e.NewValue?.GetType().Name}");
        }
        */

        // No ViewModel_RequestCloseDialog handler needed

        // No manual unsubscription needed in OnClosed
        protected override void OnClosed(EventArgs e)
        {
            Debug.WriteLine($"[InputDialogView] OnClosed for {this.Title}.");
            // Optional: Unsubscribe DataContextChanged if you kept it
            base.OnClosed(e); // Call base for its cleanup
        }
    }
}
