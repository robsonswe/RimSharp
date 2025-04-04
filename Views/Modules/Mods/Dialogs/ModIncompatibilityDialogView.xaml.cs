using RimSharp.ViewModels.Modules.Mods.Management;
using System;
using System.Windows;

namespace RimSharp.Views.Modules.Mods.Dialogs
{
    public partial class ModIncompatibilityDialogView : Window
    {
        public ModIncompatibilityDialogView()
        {
            InitializeComponent();
        }

        public ModIncompatibilityDialogView(ModIncompatibilityDialogViewModel viewModel) : this()
        {
            DataContext = viewModel;
            viewModel.RequestClose += (result) => 
            {
                DialogResult = result;
                Close();
            };
        }
    }
}