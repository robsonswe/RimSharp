using RimSharp.Infrastructure.Dialog;
using System;
using System.Diagnostics;
using System.Windows;

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public partial class IncompatibilityRuleEditorDialogView : BaseDialog
    {

        public IncompatibilityRuleEditorDialogView(IncompatibilityRuleEditorDialogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel; // BaseDialog handles subscription
            Debug.WriteLine($"[IncompatibilityRuleEditorDialogView] Constructor finished for {viewModel?.Title}. DataContext set.");
        }

        protected override void OnClosed(EventArgs e)
        {
            Debug.WriteLine($"[IncompatibilityRuleEditorDialogView] OnClosed for {this.Title}.");
            base.OnClosed(e); // Call base for its cleanup
        }
    }
}
