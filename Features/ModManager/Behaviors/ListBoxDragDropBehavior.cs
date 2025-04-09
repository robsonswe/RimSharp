using Microsoft.Xaml.Behaviors;
using RimSharp.Features.ModManager.Services.Commands;
using RimSharp.Shared.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace RimSharp.Features.ModManager.Behaviors
{
    public class ListBoxDragDropBehavior : Behavior<ListBox>
    {
        private Point _dragStartPoint;
        private bool _isDragging = false;
        private List<ModItem> _draggedItems;
        private List<ListBoxItem> _draggedItemContainers;

        private AdornerLayer _adornerLayer;
        private DragAdorner _dragAdorner;
        private InsertionAdorner _insertionAdorner;
        private static readonly Brush InsertionBrush = Brushes.Gray;
        private const double InsertionThickness = 2.0;

        private const string DraggedItemsFormat = "RimSharpModItemList";

        #region Dependency Properties

        public static readonly DependencyProperty DropCommandProperty =
            DependencyProperty.Register(nameof(DropCommand), typeof(ICommand), typeof(ListBoxDragDropBehavior), new PropertyMetadata(null));

        public ICommand DropCommand
        {
            get { return (ICommand)GetValue(DropCommandProperty); }
            set { SetValue(DropCommandProperty, value); }
        }

        public static readonly DependencyProperty DragItemTypeProperty =
            DependencyProperty.Register(nameof(DragItemType), typeof(Type), typeof(ListBoxDragDropBehavior), new PropertyMetadata(null));

        public Type DragItemType
        {
            get { return (Type)GetValue(DragItemTypeProperty); }
            set { SetValue(DragItemTypeProperty, value); }
        }

        public static readonly DependencyProperty ListGroupNameProperty =
            DependencyProperty.Register(nameof(ListGroupName), typeof(string), typeof(ListBoxDragDropBehavior), new PropertyMetadata("DefaultGroup"));

        public string ListGroupName
        {
            get { return (string)GetValue(ListGroupNameProperty); }
            set { SetValue(ListGroupNameProperty, value); }
        }

        #endregion

        protected override void OnAttached()
        {
            base.OnAttached();
            this.AssociatedObject.AllowDrop = true;
            this.AssociatedObject.PreviewMouseLeftButtonDown += AssociatedObject_PreviewMouseLeftButtonDown;
            this.AssociatedObject.PreviewMouseMove += AssociatedObject_PreviewMouseMove;
            this.AssociatedObject.PreviewMouseLeftButtonUp += AssociatedObject_PreviewMouseLeftButtonUp;
            this.AssociatedObject.PreviewDragEnter += AssociatedObject_PreviewDragEnter;
            this.AssociatedObject.PreviewDragOver += AssociatedObject_PreviewDragOver;
            this.AssociatedObject.PreviewDrop += AssociatedObject_PreviewDrop;
            this.AssociatedObject.PreviewDragLeave += AssociatedObject_PreviewDragLeave;
            this.AssociatedObject.QueryContinueDrag += AssociatedObject_QueryContinueDrag;
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (this.AssociatedObject != null)
            {
                this.AssociatedObject.PreviewMouseLeftButtonDown -= AssociatedObject_PreviewMouseLeftButtonDown;
                this.AssociatedObject.PreviewMouseMove -= AssociatedObject_PreviewMouseMove;
                this.AssociatedObject.PreviewMouseLeftButtonUp -= AssociatedObject_PreviewMouseLeftButtonUp;
                this.AssociatedObject.PreviewDragEnter -= AssociatedObject_PreviewDragEnter;
                this.AssociatedObject.PreviewDragOver -= AssociatedObject_PreviewDragOver;
                this.AssociatedObject.PreviewDrop -= AssociatedObject_PreviewDrop;
                this.AssociatedObject.PreviewDragLeave -= AssociatedObject_PreviewDragLeave;
                this.AssociatedObject.QueryContinueDrag -= AssociatedObject_QueryContinueDrag;
            }
            CleanupDragDrop();
        }

        private void AssociatedObject_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var listBox = AssociatedObject as ListBox;
            if (listBox == null) return;

            // Find the clicked ListBoxItem
            var clickedItemContainer = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);

            if (clickedItemContainer == null)
            {
                // Clicked on empty area, clear selection
                listBox.SelectedItems.Clear();
                return;
            }

            // Get the data item from the container
            var clickedItem = listBox.ItemContainerGenerator.ItemFromContainer(clickedItemContainer);
            if (clickedItem == null) return;

            // Check conditions: multiple items selected, clicked item is selected, no modifiers pressed
            if (listBox.SelectedItems.Count > 1 &&
                listBox.SelectedItems.Contains(clickedItem) &&
                Keyboard.Modifiers == ModifierKeys.None)
            {
                // Prevent deselection to allow dragging all selected items
                e.Handled = true;
            }
            // If Ctrl or Shift is pressed, or other cases, let the ListBox handle the selection
            _dragStartPoint = e.GetPosition(null);
        }

        private void AssociatedObject_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || DragItemType == null || _isDragging)
                return;

            Point currentPosition = e.GetPosition(null);
            Vector dragVector = currentPosition - _dragStartPoint;

            if (dragVector.Length > SystemParameters.MinimumHorizontalDragDistance ||
                dragVector.Length > SystemParameters.MinimumVerticalDragDistance)
            {
                ListBox listBox = AssociatedObject;
                var clickedItemContainer = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);

                if (clickedItemContainer == null) return;

                var clickedItemData = listBox.ItemContainerGenerator.ItemFromContainer(clickedItemContainer);

                if (clickedItemData != null && DragItemType.IsInstanceOfType(clickedItemData) && listBox.SelectedItems.Contains(clickedItemData))
                {
                    var currentSelectedMods = listBox.SelectedItems.OfType<ModItem>().ToList();
                    if (currentSelectedMods.Any())
                    {
                        _draggedItems = currentSelectedMods;
                        _isDragging = true;
                        Debug.WriteLine($"Starting drag for {_draggedItems.Count} item(s) from list: {ListGroupName}");

                        _draggedItemContainers = new List<ListBoxItem>();
                        foreach (var item in _draggedItems)
                        {
                            var container = listBox.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                            if (container != null)
                            {
                                _draggedItemContainers.Add(container);
                            }
                        }

                        UIElement elementForAdorner = Application.Current.MainWindow as Window ?? (UIElement)AssociatedObject;
                        _adornerLayer = AdornerLayer.GetAdornerLayer(elementForAdorner);

                        if (_adornerLayer != null)
                        {
                            _dragAdorner = new DragAdorner(elementForAdorner, _draggedItems, listBox.ItemTemplate);
                            _adornerLayer.Add(_dragAdorner);
                            UpdateDragAdornerPosition(e.GetPosition(elementForAdorner));
                        }

                        SetItemsDraggingVisualState(true);

                        var dragData = new DataObject(DraggedItemsFormat, _draggedItems);
                        try
                        {
                            DragDropEffects finalEffect = DragDrop.DoDragDrop(listBox, dragData, DragDropEffects.Move | DragDropEffects.Copy);
                            Debug.WriteLine($"DoDragDrop finished for {_draggedItems?.Count} items with effect {finalEffect}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error during DragDrop.DoDragDrop: {ex.Message}");
                            CleanupDragDrop();
                        }
                        finally
                        {
                            if (_isDragging)
                            {
                                Debug.WriteLine("DoDragDrop finished block calling Cleanup.");
                                CleanupDragDrop();
                            }
                        }
                    }
                }
            }
        }

        private void AssociatedObject_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ListBox listBox = AssociatedObject;
            var clickedElement = e.OriginalSource as DependencyObject;
            var clickedItemContainer = FindVisualParent<ListBoxItem>(clickedElement);

            if (!_isDragging && clickedItemContainer != null)
            {
                var clickedItem = listBox.ItemContainerGenerator.ItemFromContainer(clickedItemContainer);

                if (clickedItem != null &&
                    listBox.SelectedItems.Count > 1 &&
                    listBox.SelectedItems.Contains(clickedItem) &&
                    Keyboard.Modifiers == ModifierKeys.None)
                {
                    // Force WPF to update anchor for Shift+Click by resetting SelectedItem
                    listBox.SelectedItem = null;
                    listBox.SelectedItem = clickedItem;
                }
            }



            if (_isDragging)
            {
                Debug.WriteLine("MouseUp detected while IsDragging was true - cleaning up.");
                CleanupDragDrop();
            }
            _isDragging = false;
            _draggedItems = null;
            _draggedItemContainers = null;
        }

        private void AssociatedObject_PreviewDragEnter(object sender, DragEventArgs e)
        {
            if (_adornerLayer == null)
            {
                _adornerLayer = AdornerLayer.GetAdornerLayer(AssociatedObject);
            }

            if (!e.Data.GetDataPresent(DraggedItemsFormat))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void AssociatedObject_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (!e.Data.GetDataPresent(DraggedItemsFormat))
            {
                e.Effects = DragDropEffects.None;
                RemoveInsertionAdorner();
                return;
            }

            e.Effects = DragDropEffects.Move;

            ListBox listBox = AssociatedObject;
            Point currentPositionInListBox = e.GetPosition(listBox);

            if (_dragAdorner != null)
            {
                Point currentPositionInAdorned = e.GetPosition(_dragAdorner.AdornedElement);
                _dragAdorner.SetPosition(currentPositionInAdorned.X, currentPositionInAdorned.Y);
            }

            ListBoxItem targetItemContainer = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            int insertionIndex = -1;
            bool insertAbove = false;

            if (targetItemContainer != null)
            {
                Point itemRelativePos = e.GetPosition(targetItemContainer);
                insertAbove = itemRelativePos.Y < targetItemContainer.ActualHeight / 2;
                insertionIndex = listBox.ItemContainerGenerator.IndexFromContainer(targetItemContainer);
                if (!insertAbove) insertionIndex++;
                AddInsertionLine(listBox, targetItemContainer, insertAbove);
            }
            else
            {
                insertionIndex = CalculateIndexFromEmptySpace(listBox, currentPositionInListBox);
                if (insertionIndex == 0 && listBox.Items.Count > 0)
                {
                    var firstContainer = listBox.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
                    if (firstContainer != null) AddInsertionLine(listBox, firstContainer, true);
                    else AddInsertionLine(listBox, listBox, true);
                }
                else if (insertionIndex == listBox.Items.Count && listBox.Items.Count > 0)
                {
                    var lastContainer = listBox.ItemContainerGenerator.ContainerFromIndex(listBox.Items.Count - 1) as FrameworkElement;
                    if (lastContainer != null) AddInsertionLine(listBox, lastContainer, false);
                    else AddInsertionLine(listBox, listBox, false);
                }
                else
                {
                    AddInsertionLine(listBox, listBox, true);
                }
            }

            listBox.Tag = insertionIndex;
        }

        private void AssociatedObject_PreviewDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            RemoveInsertionAdorner();

            if (!e.Data.GetDataPresent(DraggedItemsFormat))
            {
                CleanupDragDrop();
                return;
            }

            var droppedData = e.Data.GetData(DraggedItemsFormat) as List<ModItem>;
            if (droppedData == null || !droppedData.Any())
            {
                CleanupDragDrop();
                return;
            }

            ListBox listBox = AssociatedObject;
            int dropIndex = listBox.Tag is int idx ? idx : -1;

            if (dropIndex == -1)
            {
                Point dropPosition = e.GetPosition(listBox);
                var targetContainer = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
                if (targetContainer != null)
                {
                    Point itemRelativePos = e.GetPosition(targetContainer);
                    bool insertAbove = itemRelativePos.Y < targetContainer.ActualHeight / 2;
                    dropIndex = listBox.ItemContainerGenerator.IndexFromContainer(targetContainer);
                    if (!insertAbove) dropIndex++;
                }
                else
                {
                    dropIndex = CalculateIndexFromEmptySpace(listBox, dropPosition);
                }
            }

            dropIndex = Math.Clamp(dropIndex, 0, listBox.Items.Count);

            var args = new DropModArgs
            {
                DroppedItems = droppedData,
                TargetListName = this.ListGroupName,
                DropIndex = dropIndex
            };

            if (DropCommand != null && DropCommand.CanExecute(args))
            {
                DropCommand.Execute(args);
            }

            CleanupDragDrop();
        }

        private void AssociatedObject_PreviewDragLeave(object sender, DragEventArgs e)
        {
            Point pos = e.GetPosition(AssociatedObject);
            Rect bounds = new Rect(0, 0, AssociatedObject.ActualWidth, AssociatedObject.ActualHeight);
            if (!bounds.Contains(pos))
            {
                RemoveInsertionAdorner();
                AssociatedObject.Tag = null;
            }
        }

        private void AssociatedObject_QueryContinueDrag(object sender, QueryContinueDragEventArgs e)
        {
            if (e.KeyStates.HasFlag(DragDropKeyStates.LeftMouseButton))
            {
                if (_dragAdorner != null)
                {
                    Point currentPos = GetMousePosition(_dragAdorner.AdornedElement);
                    _dragAdorner.SetPosition(currentPos.X, currentPos.Y);
                }
                e.Action = DragAction.Continue;
            }
            else
            {
                if (e.EscapePressed)
                {
                    e.Action = DragAction.Cancel;
                    CleanupDragDrop();
                    e.Handled = true;
                }
                else
                {
                    e.Action = DragAction.Continue;
                }
            }
        }

        private void AddInsertionLine(ListBox listBox, FrameworkElement relativeTo, bool above)
        {
            if (_adornerLayer == null) return;

            RemoveInsertionAdorner();

            _insertionAdorner = new InsertionAdorner(
                listBox,
                relativeTo,
                true,
                InsertionBrush,
                InsertionThickness,
                above);

            _adornerLayer.Add(_insertionAdorner);
        }

        private void RemoveInsertionAdorner()
        {
            if (_insertionAdorner != null && _adornerLayer != null)
            {
                try { _adornerLayer.Remove(_insertionAdorner); }
                catch (Exception ex) { Debug.WriteLine($"Error removing insertion adorner: {ex.Message}"); }
                finally { _insertionAdorner = null; }
            }
        }

        private void UpdateDragAdornerPosition(Point positionInAdornedElement)
        {
            _dragAdorner?.SetPosition(positionInAdornedElement.X, positionInAdornedElement.Y);
        }

        private void RemoveDragAdorner()
        {
            if (_dragAdorner != null && _adornerLayer != null)
            {
                try { _adornerLayer.Remove(_dragAdorner); }
                catch (Exception ex) { Debug.WriteLine($"Error removing drag adorner: {ex.Message}"); }
                finally { _dragAdorner = null; }
            }
        }

        private void SetItemsDraggingVisualState(bool isDragging)
        {
            if (_draggedItemContainers == null) return;

            foreach (var container in _draggedItemContainers)
            {
                if (container != null)
                {
                    container.Tag = isDragging ? "Dragging" : null;
                }
            }
        }

        private void CleanupDragDrop()
        {
            if (!_isDragging && _dragAdorner == null && _insertionAdorner == null)
            {
                return;
            }

            RemoveInsertionAdorner();
            RemoveDragAdorner();
            SetItemsDraggingVisualState(false);

            if (AssociatedObject != null) AssociatedObject.Tag = null;
            _isDragging = false;
            _draggedItems = null;
            _draggedItemContainers = null;
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            if (child == null) return null;
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            T parent = parentObject as T;
            if (parent != null)
                return parent;
            else
                return FindVisualParent<T>(parentObject);
        }

        private static Point GetMousePosition(Visual relativeTo)
        {
            Win32Point w32Mouse = new Win32Point();
            NativeMethods.GetCursorPos(ref w32Mouse);
            Point screenPoint = new Point(w32Mouse.X, w32Mouse.Y);
            PresentationSource source = PresentationSource.FromVisual(relativeTo);

            if (source?.RootVisual != null)
            {
                try
                {
                    return source.RootVisual.TransformToDescendant(relativeTo).Transform(screenPoint);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Warning: GetMousePosition error for {relativeTo.GetType().Name}: {ex.Message}");
                    return screenPoint;
                }
            }
            return screenPoint;
        }

        private struct Win32Point { public int X; public int Y; };
        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            internal static extern bool GetCursorPos(ref Win32Point pt);
        }

        private int CalculateIndexFromEmptySpace(ListBox listBox, Point positionInListBox)
        {
            if (listBox.Items.Count == 0) return 0;

            var firstContainer = listBox.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
            if (firstContainer != null)
            {
                Point firstItemPos = firstContainer.TranslatePoint(new Point(0, 0), listBox);
                if (positionInListBox.Y < firstItemPos.Y) return 0;
            }

            var lastContainer = listBox.ItemContainerGenerator.ContainerFromIndex(listBox.Items.Count - 1) as ListBoxItem;
            if (lastContainer != null)
            {
                Point lastItemPos = lastContainer.TranslatePoint(new Point(0, 0), listBox);
                if (positionInListBox.Y > lastItemPos.Y + lastContainer.ActualHeight) return listBox.Items.Count;
            }

            return listBox.Items.Count;
        }
    }
}