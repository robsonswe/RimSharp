using RimSharp.Infrastructure.Dialog;
using System;
using System.Windows;

namespace RimSharp.MyApp.Dialogs
{
    public partial class InputDialogView : BaseDialog
    {
        public InputDialogView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        public InputDialogView(InputDialogViewModel viewModel) : this()
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
            if (DataContext is DialogViewModelBase<MessageDialogResult> vm)
            {
                this.DialogResult = vm.DialogResult switch
                {
                    MessageDialogResult.OK => true,
                    MessageDialogResult.Cancel => false,
                    _ => null
                };
            }
            this.Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (DataContext is DialogViewModelBase vm)
            {
                vm.RequestCloseDialog -= ViewModel_RequestCloseDialog;
            }
            DataContextChanged -= OnDataContextChanged;
            base.OnClosed(e);
        }
    }
}