using System.Windows; // Added
using System.Windows.Controls;

namespace RimSharp.Features.WorkshopDownloader.Components.DownloadQueue
{
    public partial class DownloadQueueView : UserControl
    {
        public DownloadQueueView()
        {
            InitializeComponent();
        }

        // *** ADDED: Event Handler ***
        private void DownloadQueueListBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var listBox = sender as ListBox;
            if (listBox == null) return;

            // Find the ContextMenu resources
            var singleItemMenu = FindResource("ItemContextMenu") as ContextMenu;
            var multiItemMenu = FindResource("MultiItemContextMenu") as ContextMenu;

            if (singleItemMenu == null || multiItemMenu == null)
            {
                e.Handled = true; // Prevent default context menu if resources are missing
                return;
            }

            // Decide which menu to show based on selection count
            if (listBox.SelectedItems.Count > 1)
            {
                // More than one item selected, show multi-item menu
                listBox.ContextMenu = multiItemMenu;
            }
            else if (listBox.SelectedItems.Count == 1)
            {
                // Exactly one item selected, show single-item menu
                // Ensure the context menu is associated with the ListBox itself,
                // not the ListBoxItem, so RelativeSource binding works.
                listBox.ContextMenu = singleItemMenu;
            }
            else
            {
                // No items selected, prevent any context menu from showing
                e.Handled = true;
                listBox.ContextMenu = null; // Clear any previously set menu
            }
        }
    }
}