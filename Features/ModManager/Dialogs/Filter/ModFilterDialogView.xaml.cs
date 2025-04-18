using RimSharp.Infrastructure.Dialog; // For BaseDialog
using System;
using System.Diagnostics;
using System.Windows;

namespace RimSharp.Features.ModManager.Dialogs.Filter
{
    public partial class ModFilterDialogView : BaseDialog
    {
        // No _viewModel field needed
        // No _isClosing field needed

        public ModFilterDialogView() // Add default constructor if needed
        {
            InitializeComponent();
        }

        public ModFilterDialogView(ModFilterDialogViewModel viewModel) : this()
        {
            // The BaseDialog constructor handles DataContextChanged event subscription
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
             Debug.WriteLine($"[ModFilterDialogView] Constructor with VM finished for {viewModel?.Title}. DataContext set.");
        }

        // No inline handler for RequestCloseDialog needed

        // BaseDialog handles cleanup in its OnClosed override
        protected override void OnClosed(EventArgs e)
        {
            Debug.WriteLine($"[ModFilterDialogView] OnClosed for {this.Title}.");
            base.OnClosed(e);
        }
    }
}
