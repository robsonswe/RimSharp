using System.Windows;
using System.Windows.Controls;
using RimSharp.Core.Helpers;
using RimSharp.Features.WorkshopDownloader.Models;

namespace RimSharp.Features.WorkshopDownloader.Components.DownloadQueue
{
    public partial class DownloadQueueView : UserControl
{
    public DownloadQueueView()
    {
        InitializeComponent();
    }

    private void DownloadQueueListBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var listBox = sender as ListBox;
        if (listBox == null) return;

        var viewModel = DataContext as DownloadQueueViewModel;
        if (viewModel == null)
        {
            e.Handled = true;
            return;
        }

        ContextMenu contextMenu;

        if (listBox.SelectedItems.Count > 1)
        {
            // Multi-item menu
            contextMenu = new ContextMenu();
            var removeSelectedItem = new MenuItem
            {
                Header = "Remove Selected",
                Command = viewModel.RemoveItemsCommand,
                CommandParameter = listBox.SelectedItems
            };
            contextMenu.Items.Add(removeSelectedItem);
        }
        else if (listBox.SelectedItems.Count == 1)
        {
            // Single-item menu
            var selectedItem = listBox.SelectedItem as DownloadItem;
            if (selectedItem == null)
            {
                e.Handled = true;
                return;
            }

            contextMenu = new ContextMenu();
            var goToModPageItem = new MenuItem
            {
                Header = "Go to Mod Page",
                Command = viewModel.NavigateToUrlCommand,
                CommandParameter = selectedItem.Url
            };
            var removeItem = new MenuItem
            {
                Header = "Remove from Queue",
                Command = viewModel.RemoveItemCommand,
                CommandParameter = selectedItem
            };
            contextMenu.Items.Add(goToModPageItem);
            contextMenu.Items.Add(removeItem);
        }
        else
        {
            e.Handled = true;
            return;
        }

        listBox.ContextMenu = contextMenu;
    }
}
}