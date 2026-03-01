using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using RimSharp.Features.ModManager.ViewModels;

namespace RimSharp.Features.ModManager.Components.ModDetails
{
    public partial class ModDetailsView : UserControl
    {
        public ModDetailsView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void Path_Tapped(object? sender, TappedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is Shared.Models.ModItem mod)
            {
                if (DataContext is ModDetailsViewModel vm)
                {
                    vm.OpenUrlCommand?.Execute(mod.Path);
                }
            }
        }
    }
}
