using RimSharp.Infrastructure.Dialog;
using System;
using System.Windows;
using System.Diagnostics;

namespace RimSharp.AppDir.Dialogs
{
    public partial class MessageDialogView : BaseDialog
    {
        // No _viewModel field needed
        // No _isClosing field needed

        public MessageDialogView() // Keep default constructor if used by XAML previewer or DI without VM
        {
            InitializeComponent();
            // DataContext will be set later or by derived constructor/DI
            Debug.WriteLine($"[MessageDialogView] Default constructor finished.");
            // Optional: Subscribe/unsubscribe DataContextChanged here if needed for *other* reasons
            // DataContextChanged += OnDataContextChanged; // Only if needed
        }

        public MessageDialogView(MessageDialogViewModel viewModel) : this() // Chain default constructor
        {
            DataContext = viewModel; // BaseDialog handles subscription
            Debug.WriteLine($"[MessageDialogView] Constructor with VM finished for {viewModel?.Title}. DataContext set.");
        }

        // Optional: If you need OnDataContextChanged for reasons *other* than
        // subscribing/unsubscribing RequestCloseDialog, keep it. Otherwise, remove it.
        // BaseDialog now handles the necessary subscription.
        /*
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Keep this method ONLY if you need to react to DataContext changes
            // for reasons OTHER than subscribing/unsubscribing RequestCloseDialog.
            // Example: if view needs to do something specific when VM changes type.
            Debug.WriteLine($"[MessageDialogView] OnDataContextChanged. New VM: {e.NewValue?.GetType().Name}");
        }
        */

        // No ViewModel_RequestCloseDialog handler needed

        // No manual unsubscription needed in OnClosed
        protected override void OnClosed(EventArgs e)
        {
            Debug.WriteLine($"[MessageDialogView] OnClosed for {this.Title}.");
            // Optional: Unsubscribe DataContextChanged if you kept it
            // DataContextChanged -= OnDataContextChanged;
            base.OnClosed(e); // Call base for its cleanup
        }
    }
}
