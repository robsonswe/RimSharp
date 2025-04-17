using RimSharp.Infrastructure.Dialog;
using System;
using System.Windows;

namespace RimSharp.AppDir.Dialogs
{
    public partial class MessageDialogView : BaseDialog 
    {
        public MessageDialogView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        // Optional: Constructor accepting ViewModel
        public MessageDialogView(MessageDialogViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is DialogViewModelBase oldVm)
            {
                oldVm.RequestCloseDialog -= ViewModel_RequestCloseDialog;
            }
            if (e.NewValue is DialogViewModelBase newVm)
            {
                newVm.RequestCloseDialog += ViewModel_RequestCloseDialog;
            }
        }

        private void ViewModel_RequestCloseDialog(object sender, EventArgs e)
        {
            // Set DialogResult based on ViewModel before closing if needed for complex scenarios
             if (DataContext is DialogViewModelBase<MessageDialogResult> vm)
             {
                 // Standard WPF DialogResult mechanism (optional but good practice)
                 this.DialogResult = vm.DialogResult switch
                 {
                     MessageDialogResult.OK => true, // Or map appropriately
                     MessageDialogResult.Yes => true,
                     MessageDialogResult.Cancel => false,
                     MessageDialogResult.No => false,
                     _ => null // Undefined/closed via 'X'
                 };
             }

            this.Close();
        }

        // Ensure cleanup on close
        protected override void OnClosed(EventArgs e)
        {
            if (DataContext is DialogViewModelBase vm)
            {
                vm.RequestCloseDialog -= ViewModel_RequestCloseDialog;
            }
            DataContextChanged -= OnDataContextChanged; // Unsubscribe DataContext listener too
            base.OnClosed(e);
        }
    }
}
