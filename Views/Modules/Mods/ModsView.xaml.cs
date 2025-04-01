using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RimSharp.Models;
using RimSharp.Views.Modules.Mods.DragAdorner;
using System.Windows.Documents;

namespace RimSharp.Views.Modules.Mods
{
    public partial class ModsView : UserControl
    {
        private Point _dragStartPoint;
        private ModItem _draggedItem;
        private Rectangle _insertionLine;

        private AdornerLayer _insertionAdornerLayer;
        private InsertionAdorner _insertionAdorner;


        public ModsView()
        {
            InitializeComponent();
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
                        var container = listBox.ItemContainerGenerator.ContainerFromItem(_draggedItem) as ListBoxItem;
                        if (container != null)
                        {
                            container.Opacity = 0.5;
                        }

                        try
                        {
                            DragDropHelper.StartDrag(listBox, _draggedItem);
                        }
                        finally
                        {
                            if (container != null)
                            {
                                container.Opacity = 1.0;
                            }
                            _draggedItem = null;
                            RemoveInsertionLine();
                        }
                    }
                }
            }
        }
        private void ListBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(ModItem)) is not ModItem) return;

            var listBox = (ListBox)sender;
            var point = e.GetPosition(listBox);
            var item = GetItemAtPoint(listBox, point);

            RemoveInsertionLine();

            if (item != null)
            {
                var itemPos = listBox.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                var itemPoint = itemPos.TransformToAncestor(listBox).Transform(new Point(0, 0));

                bool insertAbove = point.Y < itemPoint.Y + itemPos.ActualHeight / 2;
                int index = listBox.Items.IndexOf(item);

                if (insertAbove)
                {
                    AddInsertionLine(listBox, itemPos, true);
                }
                else
                {
                    AddInsertionLine(listBox, itemPos, false);
                    index++;
                }

                listBox.Tag = index;
            }
            else
            {
                listBox.Tag = listBox.Items.Count;
                if (listBox.Items.Count > 0)
                {
                    var lastItem = listBox.Items[listBox.Items.Count - 1];
                    var container = listBox.ItemContainerGenerator.ContainerFromItem(lastItem) as FrameworkElement;
                    AddInsertionLine(listBox, container, false);
                }
                else
                {
                    // For empty list, just show at top
                    AddInsertionLine(listBox, listBox, true);
                }
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }


        private void AddInsertionLine(ListBox listBox, FrameworkElement relativeTo, bool above)
        {
            RemoveInsertionLine();

            _insertionAdornerLayer = AdornerLayer.GetAdornerLayer(listBox);
            if (_insertionAdornerLayer != null)
            {
                _insertionAdorner = new InsertionAdorner(
                    listBox,
                    relativeTo,
                    above,
                    Brushes.Gray, // Use your RimworldHighlightBrush here
                    2.0);

                _insertionAdornerLayer.Add(_insertionAdorner);
            }
        }


        private void RemoveInsertionLine()
        {
            if (_insertionAdornerLayer != null && _insertionAdorner != null)
            {
                _insertionAdornerLayer.Remove(_insertionAdorner);
                _insertionAdorner = null;
            }
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
            RemoveInsertionLine();

            if (DataContext is ViewModels.Modules.Mods.ModsViewModel vm &&
                e.Data.GetData(typeof(ModItem)) is ModItem draggedMod)
            {
                var listBox = (ListBox)sender;
                int dropIndex = listBox.Tag is int i ? i : vm.ActiveMods.Count;

                if (vm.ActiveMods.Contains(draggedMod))
                {
                    vm.ReorderActiveMod(draggedMod, dropIndex);
                }
                else
                {
                    vm.AddModToActiveAtPosition(draggedMod, dropIndex);
                }

                listBox.SelectedItem = draggedMod;
                listBox.ScrollIntoView(draggedMod);
            }
        }

        private void InactiveList_Drop(object sender, DragEventArgs e)
        {
            RemoveInsertionLine();

            if (DataContext is ViewModels.Modules.Mods.ModsViewModel vm &&
                e.Data.GetData(typeof(ModItem)) is ModItem draggedMod &&
                vm.ActiveMods.Contains(draggedMod))
            {
                vm.RemoveModFromActive(draggedMod);
            }
        }

        private void ListBox_DragLeave(object sender, DragEventArgs e)
        {
            RemoveInsertionLine();
        }

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
private void InactiveList_PreviewMouseMove(object sender, MouseEventArgs e)
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
                var container = listBox.ItemContainerGenerator.ContainerFromItem(_draggedItem) as ListBoxItem;
                if (container != null)
                {
                    container.Opacity = 0.5;
                }

                try
                {
                    DragDropHelper.StartDrag(listBox, _draggedItem);
                }
                finally
                {
                    if (container != null)
                    {
                        container.Opacity = 1.0;
                    }
                    _draggedItem = null;
                    RemoveInsertionLine();
                }
            }
        }
    }
}
        private void ListBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }
    }
}