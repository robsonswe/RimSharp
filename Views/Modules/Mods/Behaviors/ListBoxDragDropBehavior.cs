using Microsoft.Xaml.Behaviors;
using RimSharp.Models; // For ModItem
using RimSharp.Views.Modules.Mods.DragAdorner; // For Adorners
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using RimSharp.ViewModels.Modules.Mods.Commands;

namespace RimSharp.Views.Modules.Mods.Behaviors
{
    public class ListBoxDragDropBehavior : Behavior<ListBox>
    {
        private Point _dragStartPoint;
        private bool _isDragging = false;
        private ModItem _draggedData; // Store the actual ModItem being dragged

        // Adorner fields
        private AdornerLayer _adornerLayer;
        private DragAdorner.DragAdorner _dragAdorner;
        private InsertionAdorner _insertionAdorner;
        private static readonly Brush InsertionBrush = Brushes.Gray; // Or use your RimworldHighlightBrush
        private const double InsertionThickness = 2.0;

        #region Dependency Properties

        // Command to execute on successful drop
        public static readonly DependencyProperty DropCommandProperty =
            DependencyProperty.Register(nameof(DropCommand), typeof(ICommand), typeof(ListBoxDragDropBehavior), new PropertyMetadata(null));

        public ICommand DropCommand
        {
            get { return (ICommand)GetValue(DropCommandProperty); }
            set { SetValue(DropCommandProperty, value); }
        }

        // The Type of item being dragged (e.g., typeof(ModItem))
        public static readonly DependencyProperty DragItemTypeProperty =
            DependencyProperty.Register(nameof(DragItemType), typeof(Type), typeof(ListBoxDragDropBehavior), new PropertyMetadata(null));

        public Type DragItemType
        {
            get { return (Type)GetValue(DragItemTypeProperty); }
            set { SetValue(DragItemTypeProperty, value); }
        }

        // Identifier for the list (e.g., "Active", "Inactive")
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
            this.AssociatedObject.AllowDrop = true; // Ensure AllowDrop is set
            this.AssociatedObject.PreviewMouseLeftButtonDown += AssociatedObject_PreviewMouseLeftButtonDown;
            this.AssociatedObject.PreviewMouseMove += AssociatedObject_PreviewMouseMove;
            this.AssociatedObject.PreviewMouseLeftButtonUp += AssociatedObject_PreviewMouseLeftButtonUp; // Handle drag end without drop
            this.AssociatedObject.PreviewDragOver += AssociatedObject_PreviewDragOver;
            this.AssociatedObject.PreviewDrop += AssociatedObject_PreviewDrop;
            this.AssociatedObject.PreviewDragLeave += AssociatedObject_PreviewDragLeave;
            this.AssociatedObject.QueryContinueDrag += AssociatedObject_QueryContinueDrag; // Optional: Handle Esc key
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (this.AssociatedObject != null)
            {
                this.AssociatedObject.PreviewMouseLeftButtonDown -= AssociatedObject_PreviewMouseLeftButtonDown;
                this.AssociatedObject.PreviewMouseMove -= AssociatedObject_PreviewMouseMove;
                this.AssociatedObject.PreviewMouseLeftButtonUp -= AssociatedObject_PreviewMouseLeftButtonUp;
                this.AssociatedObject.PreviewDragOver -= AssociatedObject_PreviewDragOver;
                this.AssociatedObject.PreviewDrop -= AssociatedObject_PreviewDrop;
                this.AssociatedObject.PreviewDragLeave -= AssociatedObject_PreviewDragLeave;
                this.AssociatedObject.QueryContinueDrag -= AssociatedObject_QueryContinueDrag;
            }
            // Ensure cleanup on detach
            CleanupDragDrop();
        }

        private void AssociatedObject_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Only process if the item type is set
            if (DragItemType == null) return;

            _dragStartPoint = e.GetPosition(null); // Position relative to screen/window
                                                   // Reset drag state in case a previous drag was aborted
            _isDragging = false;
            _draggedData = null;
        }

        private void AssociatedObject_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // Only process if the item type is set and mouse button is pressed
            if (e.LeftButton != MouseButtonState.Pressed || DragItemType == null || _isDragging)
                return;

            Point currentPosition = e.GetPosition(null);
            Vector dragVector = currentPosition - _dragStartPoint;

            // Start dragging only if the mouse has moved beyond a threshold
            // Use SystemParameters for sensitivity if desired
            if (dragVector.Length > SystemParameters.MinimumHorizontalDragDistance ||
                dragVector.Length > SystemParameters.MinimumVerticalDragDistance)
            {
                ListBox listBox = AssociatedObject;
                var draggedItem = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);

                if (draggedItem != null)
                {
                    // Ensure the item being dragged is actually selected or directly clicked
                    if (listBox.SelectedItem == null || listBox.ItemContainerGenerator.ItemFromContainer(draggedItem) != listBox.SelectedItem)
                    {
                        // If the clicked item wasn't selected, select it now before dragging
                        var itemData = listBox.ItemContainerGenerator.ItemFromContainer(draggedItem);
                        if (itemData != null && itemData.GetType() == DragItemType)
                        {
                            listBox.SelectedItem = itemData;
                        }
                        else
                        {
                            return; // Clicked on something else (scrollbar?) or wrong item type
                        }
                    }


                    _draggedData = listBox.SelectedItem as ModItem; // Assuming ModItem for now

                    if (_draggedData != null && _draggedData.GetType() == DragItemType)
                    {
                        _isDragging = true;
                        Debug.WriteLine($"Starting drag for item: {_draggedData.Name} from list: {ListGroupName}");

                        // --- Adorner Setup ---
                        _adornerLayer = AdornerLayer.GetAdornerLayer(listBox);
                        if (_adornerLayer != null)
                        {
                            // Use ListBox ItemTemplate for the drag adorner
                            _dragAdorner = new DragAdorner.DragAdorner(listBox, _draggedData, listBox.ItemTemplate);
                            _adornerLayer.Add(_dragAdorner);
                            UpdateDragAdornerPosition(e.GetPosition(listBox)); // Initial position
                        }
                        else
                        {
                            Debug.WriteLine("Could not find AdornerLayer for drag adorner.");
                        }

                        // Apply visual feedback to original item (optional)
                        draggedItem.Opacity = 0.5;

                        // --- Start Drag Operation ---
                        var dragData = new DataObject(DragItemType, _draggedData);
                        try
                        {
                            DragDropEffects finalEffect = DragDrop.DoDragDrop(listBox, dragData, DragDropEffects.Move | DragDropEffects.Copy); // Allow Move
                                                                                                                                               // Cleanup happens after DoDragDrop returns (in Up, Leave, Drop, or QueryContinueDrag)
                            Debug.WriteLine($"DoDragDrop finished for {_draggedData?.Name} with effect {finalEffect}");

                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error during DragDrop.DoDragDrop: {ex.Message}");
                            // Ensure cleanup even if DoDragDrop throws
                            CleanupDragDrop(draggedItem);
                        }
                        finally
                        {
                            // Final cleanup just in case other handlers didn't catch it
                            CleanupDragDrop(draggedItem);
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"Drag start failed: SelectedItem is null, not a ModItem, or doesn't match DragItemType ({DragItemType?.Name}).");
                        _isDragging = false; // Reset flag
                    }
                }
            }
        }

        private void AssociatedObject_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // If the mouse is released *before* a drag operation started, clean up potential partial state
            // This also handles the case where DoDragDrop finishes without a drop (e.g., outside window)
            if (_isDragging)
            {
                Debug.WriteLine("MouseUp detected during drag - cleaning up.");
                // Find the original item container if possible to reset opacity
                ListBoxItem itemContainer = null;
                if (AssociatedObject != null && _draggedData != null)
                {
                    itemContainer = AssociatedObject.ItemContainerGenerator.ContainerFromItem(_draggedData) as ListBoxItem;
                }
                CleanupDragDrop(itemContainer);
            }
            _isDragging = false;
            _draggedData = null;
        }


        private void AssociatedObject_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;

            // Check if the data being dragged is of the expected type
            if (!e.Data.GetDataPresent(DragItemType))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                RemoveInsertionAdorner(); // Remove line if dragging wrong type over
                return;
            }

            // Check if source and target allow the move (e.g., prevent dropping Core onto Inactive)
            // Basic check: Allow Move by default
            e.Effects = DragDropEffects.Move;
            e.Handled = true;


            ListBox listBox = AssociatedObject;
            Point currentPosition = e.GetPosition(listBox);

            // Update drag adorner position
            UpdateDragAdornerPosition(currentPosition);

            // --- Insertion Adorner Logic ---
            ListBoxItem targetItemContainer = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            int insertionIndex = -1;
            bool insertAbove = false;

            if (targetItemContainer != null)
            {
                // Get the data item associated with the container under the mouse
                var itemUnderMouse = listBox.ItemContainerGenerator.ItemFromContainer(targetItemContainer);
                if (itemUnderMouse != null)
                {
                    Point itemRelativePos = e.GetPosition(targetItemContainer);
                    insertAbove = itemRelativePos.Y < targetItemContainer.ActualHeight / 2;
                    insertionIndex = listBox.ItemContainerGenerator.IndexFromContainer(targetItemContainer);

                    if (!insertAbove)
                    {
                        insertionIndex++;
                    }
                    AddInsertionLine(listBox, targetItemContainer, insertAbove);
                }
                else
                {
                    // Dragging over empty space below last item?
                    insertionIndex = listBox.Items.Count;
                    AddInsertionLine(listBox, listBox, false); // Show at bottom
                }
            }
            else
            {
                // Dragging over empty space (or header/footer?)
                // If list has items, insert at the end. If empty, insert at the top (index 0).
                insertionIndex = listBox.Items.Count;
                if (listBox.Items.Count > 0)
                {
                    var lastContainer = listBox.ItemContainerGenerator.ContainerFromIndex(listBox.Items.Count - 1) as FrameworkElement;
                    if (lastContainer != null) AddInsertionLine(listBox, lastContainer, false); // Below last item
                    else AddInsertionLine(listBox, listBox, true); // Fallback: Top of listbox
                }
                else
                {
                    AddInsertionLine(listBox, listBox, true); // Top of empty listbox
                }
            }

            // Store the calculated index (e.g., in Tag) for the Drop handler
            listBox.Tag = insertionIndex; // Use Tag to pass index to Drop event
        }

        private void AssociatedObject_PreviewDrop(object sender, DragEventArgs e)
        {
            Debug.WriteLine($"=== DROP START ===");
            Debug.WriteLine($"Drop detected on list: {ListGroupName}");
            Debug.WriteLine($"Data present: {e.Data.GetDataPresent(DragItemType)}");

            if (e.Data.GetData(DragItemType) is ModItem item)
            {
                Debug.WriteLine($"Dropped item: {item.Name}");
            }

            Debug.WriteLine($"Drop detected on list: {ListGroupName}");
            e.Handled = true;
            RemoveInsertionAdorner(); // Clean up insertion line first

            if (!e.Data.GetDataPresent(DragItemType))
            {
                Debug.WriteLine("Drop failed: Data type mismatch.");
                CleanupDragDrop(); // Clean up drag adorner too
                return;
            }

            var droppedData = e.Data.GetData(DragItemType) as ModItem;
            if (droppedData == null)
            {
                Debug.WriteLine("Drop failed: Could not cast data to ModItem.");
                CleanupDragDrop();
                return;
            }

            ListBox listBox = AssociatedObject;
            // Retrieve the index calculated during DragOver
            int dropIndex = listBox.Tag is int idx ? idx : -1;
            if (dropIndex == -1) // Should ideally always be set by DragOver
            {
                Debug.WriteLine("Warning: Drop index not found in ListBox.Tag. Defaulting to end of list.");
                dropIndex = listBox.Items.Count; // Fallback
            }
            // Clamp index just in case
            dropIndex = Math.Clamp(dropIndex, 0, listBox.Items.Count);


            // Create arguments for the command
            var args = new DropModArgs
            {
                DroppedItem = droppedData,
                TargetListName = this.ListGroupName,
                DropIndex = dropIndex
            };

            // Execute the command
            if (DropCommand != null && DropCommand.CanExecute(args))
            {
                Debug.WriteLine($"Executing DropCommand: Item='{args.DroppedItem.Name}', Target='{args.TargetListName}', Index={args.DropIndex}");
                DropCommand.Execute(args);
                e.Handled = true;
            }
            else
            {
                Debug.WriteLine("DropCommand is null or cannot execute.");
            }

            // Find the original item container if possible to reset opacity
            ListBoxItem originalItemContainer = null;
            // Note: Finding the original container might be tricky if the drag originated
            // from a *different* ListBox. We only reliably know the container if the
            // source and target are the same AssociatedObject.
            // For now, we'll attempt cleanup assuming the source might be this list.
            // The _draggedData field holds the item regardless of source list.
            if (AssociatedObject != null && _draggedData != null)
            {
                originalItemContainer = AssociatedObject.ItemContainerGenerator.ContainerFromItem(_draggedData) as ListBoxItem;
            }

            CleanupDragDrop(originalItemContainer); // General cleanup after drop
        }

        private void AssociatedObject_PreviewDragLeave(object sender, DragEventArgs e)
        {
            // Remove insertion adorner if the mouse leaves the ListBox bounds
            // Check if the mouse position is truly outside the ListBox
            Point pos = e.GetPosition(AssociatedObject);
            Rect bounds = new Rect(0, 0, AssociatedObject.ActualWidth, AssociatedObject.ActualHeight);
            if (!bounds.Contains(pos))
            {
                RemoveInsertionAdorner();
                // Do NOT clean up the drag adorner here, it should follow the mouse
                AssociatedObject.Tag = null; // Clear stored index
                Debug.WriteLine($"DragLeave detected from list: {ListGroupName}");
            }
        }

        private void AssociatedObject_QueryContinueDrag(object sender, QueryContinueDragEventArgs e)
{
    // Optional: Keep updating drag adorner position continuously if button is pressed
    if (e.KeyStates.HasFlag(DragDropKeyStates.LeftMouseButton))
    {
        if (_dragAdorner != null)
        {
            Point currentPos = GetMousePosition(AssociatedObject); // Helper needed
            UpdateDragAdornerPosition(currentPos);
        }
        // Keep default action (Continue) if button is pressed
        e.Action = DragAction.Continue;
        // Don't handle here, let system continue
        // e.Handled = true; // REMOVE THIS from general continuation
    }
    else // Mouse button is NOT pressed OR Escape is pressed
    {
        if (e.EscapePressed)
        {
            Debug.WriteLine("Drag cancelled by Escape key.");
            e.Action = DragAction.Cancel;
            // Find the original item container if possible
            ListBoxItem itemContainer = null;
            if (AssociatedObject != null && _draggedData != null)
            {
                itemContainer = AssociatedObject.ItemContainerGenerator.ContainerFromItem(_draggedData) as ListBoxItem;
            }
            CleanupDragDrop(itemContainer); // Clean up everything on cancel
            e.Handled = true; // IMPORTANT: Set handled ONLY for explicit cancel
        }
        else // Button released (not Escape) - Let the framework decide Drop or Cancel
        {
             Debug.WriteLine("Drag ended (mouse button released). Action will be determined by Drop/Leave handlers.");
             // Do NOT set e.Action here.
             // Do NOT set e.Handled = true here. The framework needs to proceed
             // to check for a valid drop target and raise PreviewDrop.
             // The cleanup will happen in PreviewDrop or PreviewMouseLeftButtonUp.
        }
    }

    // Only set it inside the Escape block.
}


        // --- Adorner Helper Methods ---

        private void AddInsertionLine(ListBox listBox, FrameworkElement relativeTo, bool above)
        {
            RemoveInsertionAdorner(); // Remove previous one first

            if (_adornerLayer == null)
            {
                _adornerLayer = AdornerLayer.GetAdornerLayer(listBox);
                if (_adornerLayer == null)
                {
                    Debug.WriteLine("Cannot add insertion line: AdornerLayer not found.");
                    return;
                }
            }


            _insertionAdorner = new InsertionAdorner(
                listBox,        // Adorned Element (the ListBox itself)
                relativeTo,     // Element to position relative to (ListBoxItem or ListBox)
                true,           // Always horizontal for ListBox items
                InsertionBrush,
                InsertionThickness,
                above);         // Pass the 'above' parameter

            _adornerLayer.Add(_insertionAdorner);
        }

        private void RemoveInsertionAdorner()
        {
            if (_insertionAdorner != null && _adornerLayer != null)
            {
                try
                {
                    _adornerLayer.Remove(_insertionAdorner);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error removing insertion adorner: {ex.Message}");
                    // Attempt to clear references anyway
                }
                finally
                {
                    _insertionAdorner = null;
                }
            }
            // Don't clear _adornerLayer here, might be needed by drag adorner
        }


        private void UpdateDragAdornerPosition(Point currentPosition)
        {
            if (_dragAdorner != null)
            {
                // Position adorner relative to the ListBox top-left
                _dragAdorner.SetPosition(currentPosition.X, currentPosition.Y);
            }
        }

        private void RemoveDragAdorner()
        {
            if (_dragAdorner != null && _adornerLayer != null)
            {
                try
                {
                    _adornerLayer.Remove(_dragAdorner);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error removing drag adorner: {ex.Message}");
                }
                finally
                {
                    _dragAdorner = null;
                }
            }
            // Don't clear _adornerLayer here, might be needed by insertion adorner
        }

        private void CleanupDragDrop(ListBoxItem originalItemContainer = null)
        {
            Debug.WriteLine($"CleanupDragDrop called. Dragging: {_isDragging}");
            RemoveInsertionAdorner();
            RemoveDragAdorner();


            // Reset opacity of the original item if provided
            if (originalItemContainer != null)
            {
                originalItemContainer.Opacity = 1.0;
            }
            else if (AssociatedObject != null && _draggedData != null && _isDragging) // Try finding it again if needed
            {
                var container = AssociatedObject.ItemContainerGenerator.ContainerFromItem(_draggedData) as ListBoxItem;
                if (container != null) container.Opacity = 1.0;
            }


            // Clear state variables
            if (AssociatedObject != null) AssociatedObject.Tag = null; // Clear stored index
            _isDragging = false;
            _draggedData = null; // Clear the reference to the dragged data
            // _adornerLayer = null; // Avoid clearing this unless detaching behavior
        }


        // --- Visual Tree Helpers ---

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            T parent = parentObject as T;
            if (parent != null)
                return parent;
            else
                return FindVisualParent<T>(parentObject);
        }

        // Helper to get mouse position relative to an element
        // Required because DragEventArgs.GetPosition gives position relative to the event source,
        // which might not be the element we want (e.g., if dragging over textblock inside listboxitem)
        private static Point GetMousePosition(Visual relativeTo)
        {
            Win32Point w32Mouse = new Win32Point();
            NativeMethods.GetCursorPos(ref w32Mouse);
            return relativeTo.PointFromScreen(new Point(w32Mouse.X, w32Mouse.Y));
        }

        // P/Invoke helpers for GetCursorPos
        private struct Win32Point { public int X; public int Y; };
        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            internal static extern bool GetCursorPos(ref Win32Point pt);
        }
    }
}
