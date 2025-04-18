using RimSharp.Infrastructure.Dialog;
using System;
using System.Diagnostics;
using System.Windows;

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{

    public partial class DependencyRuleEditorDialogView : BaseDialog
    {

        public DependencyRuleEditorDialogView(DependencyRuleEditorDialogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel; // BaseDialog will handle subscription via DataContextChanged
            Debug.WriteLine($"[DependencyRuleEditorDialogView] Constructor finished for {viewModel?.Title}. DataContext set.");
        }

        // NO ViewModel_RequestCloseDialog handler needed

        // NO manual unsubscription needed in OnClosed (BaseDialog handles it)
        protected override void OnClosed(EventArgs e)
        {
            // Optional: Add specific cleanup for THIS view if needed
            Debug.WriteLine($"[DependencyRuleEditorDialogView] OnClosed for {this.Title}.");
            base.OnClosed(e); // Call base for its cleanup
        }
    }
}
