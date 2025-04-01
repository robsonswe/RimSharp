using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RimSharp.Models;

namespace RimSharp.Views.Modules.Mods
{
    public partial class ModsView : UserControl
    {
        private Point _dragStartPoint;
        private ModItem _draggedItem;
        private Rectangle _insertionLine;

        public ModsView()
        {
            InitializeComponent();
            _insertionLine = new Rectangle { Style = (Style)FindResource("DropInsertionLine") };

        }

        private void InactiveList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.Modules.Mods.ModsViewModel vm &&
                ((ListBox)sender).SelectedItem is Models.ModItem mod)
            {
                vm.AddModToActive(mod);
            }
        }

        private void ActiveList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.Modules.Mods.ModsViewModel vm &&
                ((ListBox)sender).SelectedItem is Models.ModItem mod)
            {
                vm.RemoveModFromActive(mod);
            }
        }

        private void ActiveList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedItem == null)
            {
                var listBox = (ListBox)sender;
                var point = e.GetPosition(null);

                if ((point - _dragStartPoint).Length > 10)
                {
                    _draggedItem = listBox.SelectedItem as ModItem;
                    if (_draggedItem != null)
                    {
                        DragDrop.DoDragDrop(listBox, _draggedItem, DragDropEffects.Move);
                        _draggedItem = null;
                    }
                }
            }
        }


               private void ListBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (_draggedItem == null) return;

            var listBox = (ListBox)sender;
            var point = e.GetPosition(listBox);
            var item = GetItemAtPoint(listBox, point);

            // Remove any existing insertion line
            RemoveInsertionLine(listBox);

            if (item != null)
            {
                var itemPos = listBox.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                var itemPoint = itemPos.TransformToAncestor(listBox).Transform(new Point(0, 0));

                // Determine if we're inserting above or below the item
                bool insertAbove = point.Y < itemPoint.Y + itemPos.ActualHeight / 2;
                int index = listBox.Items.IndexOf(item);

                if (insertAbove)
                {
                    // Insert above
                    AddInsertionLine(listBox, itemPos, true);
                }
                else
                {
                    // Insert below
                    AddInsertionLine(listBox, itemPos, false);
                    index++;
                }

                // Store the target index for the drop
                listBox.Tag = index;
            }
            else
            {
                // Insert at end
                listBox.Tag = listBox.Items.Count;
                if (listBox.Items.Count > 0)
                {
                    var lastItem = listBox.Items[listBox.Items.Count - 1];
                    var container = listBox.ItemContainerGenerator.ContainerFromItem(lastItem) as FrameworkElement;
                    AddInsertionLine(listBox, container, false);
                }
                else
                {
                    AddInsertionLine(listBox, listBox, true);
                }
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void AddInsertionLine(ListBox listBox, FrameworkElement relativeTo, bool above)
        {
            var panel = FindVisualChild<VirtualizingStackPanel>(listBox);
            if (panel != null)
            {
                var pos = relativeTo.TransformToAncestor(panel).Transform(new Point(0, 0));
                _insertionLine.Margin = new Thickness(0, above ? pos.Y : pos.Y + relativeTo.ActualHeight, 0, 0);
                panel.Children.Add(_insertionLine);
            }
        }

        private void RemoveInsertionLine(ListBox listBox)
        {
            var panel = FindVisualChild<VirtualizingStackPanel>(listBox);
            panel?.Children.Remove(_insertionLine);
        }

        private static T FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is T result)
                    return result;
                var childResult = FindVisualChild<T>(child);
                if (childResult != null)
                    return childResult;
            }
            return null;
        }


        private void ActiveList_Drop(object sender, DragEventArgs e)
        {
            var listBox = (ListBox)sender;
            RemoveInsertionLine(listBox);

            if (DataContext is ViewModels.Modules.Mods.ModsViewModel vm)
            {
                int dropIndex = listBox.Tag is int i ? i : vm.ActiveMods.Count;

                if (e.Data.GetData(typeof(ModItem)) is ModItem draggedMod)
                {
                    if (vm.ActiveMods.Contains(draggedMod))
                    {
                        // Reorder within active list
                        vm.ReorderActiveMod(draggedMod, dropIndex);
                    }
                    else
                    {
                        // Add from inactive to active
                        vm.AddModToActiveAtPosition(draggedMod, dropIndex);
                    }
                }
            }
        }

        private void InactiveList_Drop(object sender, DragEventArgs e)
        {
            RemoveInsertionLine((ListBox)sender);

            if (DataContext is ViewModels.Modules.Mods.ModsViewModel vm &&
                e.Data.GetData(typeof(ModItem)) is ModItem draggedMod &&
                vm.ActiveMods.Contains(draggedMod))
            {
                vm.RemoveModFromActive(draggedMod);
            }
        }

        private void ListBox_DragLeave(object sender, DragEventArgs e)
        {
            RemoveInsertionLine((ListBox)sender);
        }

        // Helper methods
        private ModItem GetItemAtPoint(ListBox listBox, Point point)
        {
            for (int i = 0; i < listBox.Items.Count; i++)
            {
                var item = listBox.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (item != null && item.TransformToVisual(listBox).Transform(new Point(0, 0)).Y <= point.Y &&
                    item.TransformToVisual(listBox).Transform(new Point(0, item.ActualHeight)).Y >= point.Y)
                {
                    return listBox.Items[i] as ModItem;
                }
            }
            return null;
        }


        private void ActiveList_PreviewDrop(object sender, DragEventArgs e)
        {
            if (DataContext is ViewModels.Modules.Mods.ModsViewModel vm &&
                e.Data.GetData(typeof(Models.ModItem)) is Models.ModItem draggedMod)
            {
                var listBox = (ListBox)sender;
                var dropTarget = listBox.SelectedItem as Models.ModItem;
                var dropIndex = dropTarget != null ?
                    vm.ActiveMods.IndexOf(dropTarget) :
                    vm.ActiveMods.Count;

                // If dragging within active list, reorder
                if (vm.ActiveMods.Contains(draggedMod))
                {
                    vm.ReorderActiveMod(draggedMod, dropIndex);
                }
                // If dragging from inactive to active, add at position
                else
                {
                    vm.AddModToActiveAtPosition(draggedMod, dropIndex);
                }
            }
        }

        private void InactiveList_PreviewDrop(object sender, DragEventArgs e)
        {
            if (DataContext is ViewModels.Modules.Mods.ModsViewModel vm &&
                e.Data.GetData(typeof(Models.ModItem)) is Models.ModItem draggedMod &&
                vm.ActiveMods.Contains(draggedMod))
            {
                vm.RemoveModFromActive(draggedMod);
            }
        }

        private void ListBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }
    }
}