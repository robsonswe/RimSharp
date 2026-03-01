using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using RimSharp.Features.VramAnalysis.ViewModels;
using System.Diagnostics;

namespace RimSharp.Features.VramAnalysis.Views
{
    public partial class VramAnalysisView : UserControl
    {
        private object? _lastClickedItem;

        public VramAnalysisView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            var dataGrid = this.FindControl<DataGrid>("VramModsDataGrid");
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
