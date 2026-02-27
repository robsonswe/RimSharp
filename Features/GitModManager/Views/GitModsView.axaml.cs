using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using RimSharp.Features.GitModManager.ViewModels;
using System.Diagnostics;

namespace RimSharp.Features.GitModManager.Views
{
    public partial class GitModsView : UserControl
    {
        private object? _lastClickedItem;

        public GitModsView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            var dataGrid = this.FindControl<DataGrid>("GitModsDataGrid");
            if (dataGrid != null)
            {
                dataGrid.AddHandler(InputElement.PointerReleasedEvent, OnDataGridPointerReleased, RoutingStrategies.Tunnel);
            }
        }

        private void OnDataGridPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            var dataGrid = sender as DataGrid;
            if (dataGrid?.SelectedItem != null)
            {
                if (dataGrid.SelectedItem == _lastClickedItem)
                {
                    // Toggle: deselect if clicking same item
                    dataGrid.SelectedItem = null;
                    _lastClickedItem = null;
                }
                else
                {
                    _lastClickedItem = dataGrid.SelectedItem;
                }
            }
            else
            {
                _lastClickedItem = null;
            }
        }
    }
}
