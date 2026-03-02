using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.ViewModels;

namespace RimSharp.Features.WorkshopDownloader.Components.DownloadQueue
{
    public partial class DownloadQueueView : UserControl
    {
        private ListBox? _listBox;

        public DownloadQueueView()
        {
            InitializeComponent();
            _listBox = this.FindControl<ListBox>("DownloadQueueListBox");
            if (_listBox != null)
            {
                _listBox.AddHandler(PointerReleasedEvent, OnListBoxPointerReleased, RoutingStrategies.Tunnel);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnListBoxPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Right)
                return;

            var listBox = sender as ListBox ?? _listBox;
            if (listBox == null) return;

            var viewModel = DataContext as DownloadQueueViewModel;
            if (viewModel == null) return;

            ContextMenu? contextMenu;

            if (listBox.SelectedItems?.Count > 1)
            {

                contextMenu = new ContextMenu();
                var removeSelectedMenuItem = new MenuItem
                {
                    Header = "Remove Selected",
                    Command = viewModel.RemoveItemsCommand,
                    CommandParameter = listBox.SelectedItems
                };
                contextMenu.Items.Add(removeSelectedMenuItem);
            }
            else if (listBox.SelectedItems?.Count == 1)
            {

                var selectedItem = listBox.SelectedItem as DownloadItem;
                if (selectedItem == null) return;

                contextMenu = new ContextMenu();
                var openInBrowserMenuItem = new MenuItem
                {
                    Header = "Open in Browser",
                    Command = viewModel.NavigateToUrlCommand,
                    CommandParameter = selectedItem.Url
                };
                var removeMenuItem = new MenuItem
                {
                    Header = "Remove from Queue",
                    Command = viewModel.RemoveItemCommand,
                    CommandParameter = selectedItem
                };
                contextMenu.Items.Add(openInBrowserMenuItem);
                contextMenu.Items.Add(removeMenuItem);
            }
            else
            {
                // No selection - no context menu
                return;
            }

            listBox.ContextMenu = contextMenu;
            contextMenu.Open(listBox);
            e.Handled = true;
        }
    }
}

