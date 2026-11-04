using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using RimSharp.Features.ModManager.ViewModels;

namespace RimSharp.Features.ModManager.Components.ModDetails
{
    public partial class ModDetailsView : UserControl
    {
        private ModDetailsViewModel? _observedVm;

        public ModDetailsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_observedVm != null)
            {
                _observedVm.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _observedVm = DataContext as ModDetailsViewModel;

            if (_observedVm != null)
            {
                _observedVm.PropertyChanged += OnViewModelPropertyChanged;
            }
        }

        // The description ScrollViewers live inside a ContentControl DataTemplate,
        // so Avalonia reuses the same visual tree when the selected mod changes and
        // the previous scroll position is kept. Reset them to the top here.
        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ModDetailsViewModel.CurrentMod))
            {
                ResetDescriptionScroll();
            }
        }

        private void ResetDescriptionScroll()
        {
            foreach (var sv in this.GetVisualDescendants().OfType<ScrollViewer>())
            {
                sv.Offset = new Vector(sv.Offset.X, 0);
            }
        }

        private void Path_Tapped(object? sender, TappedEventArgs e)
        {
            if (sender is TextBlock { DataContext: Shared.Models.ModItem mod })
            {
                if (DataContext is ModDetailsViewModel vm)
                {
                    vm.OpenUrlCommand?.Execute(mod.Path);
                }
            }
        }
    }
}