using RimSharp.Infrastructure.Dialog;
using System;
using System.Diagnostics;
using System.Windows;

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public partial class CustomizeModDialogView : BaseDialog
    {

        public CustomizeModDialogView(CustomizeModDialogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel; // BaseDialog handles subscription
            Debug.WriteLine($"[CustomizeModDialogView] Constructor finished for {viewModel?.Title}. DataContext set.");
        }

        protected override void OnClosed(EventArgs e)
        {
            Debug.WriteLine($"[CustomizeModDialogView] OnClosed for {this.Title}.");
            base.OnClosed(e); // Call base for its cleanup
        }
    }
}
