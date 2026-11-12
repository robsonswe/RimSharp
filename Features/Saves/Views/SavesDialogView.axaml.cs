using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Markup.Xaml;
using RimSharp.Features.Saves.ViewModels;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.Saves.Views
{
    /// <summary>
    /// The read-only saves dialog: the save list on the left, the details of
    /// the selected save on the right.
    /// </summary>
    public partial class SavesDialogView : BaseDialog
    {
        public SavesDialogView()
        {
            InitializeComponent();
        }

        public SavesDialogView(SavesDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            Opened += OnOpened;
        }

        /// <summary>
        /// The list takes the focus when the dialog opens - the arrow keys
        /// navigate the saves right away.
        /// </summary>
        private void OnOpened(object? sender, EventArgs e)
        {
            this.GetVisualDescendants().OfType<ListBox>().FirstOrDefault()?.Focus();
        }
    }
}
