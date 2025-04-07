using Microsoft.Xaml.Behaviors;
using RimSharp.Features.ModManager.Services.Commands;
using RimSharp.Shared.Models;
using System;
using System.Collections; // For IEnumerable
using System.Collections.Generic; // For List<>
using System.Diagnostics;
using System.Linq; // For Linq extensions like OfType, ToList
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
        // private ModItem _draggedData; // Replaced
        private List<ModItem> _draggedItems; // Store the actual ModItems being dragged
        private List<ListBoxItem> _draggedItemContainers; // Store original containers for visual feedback

        // Adorner fields
        private AdornerLayer _adornerLayer;
        private DragAdorner _dragAdorner;
        private InsertionAdorner _insertionAdorner;
        private static readonly Brush InsertionBrush = Brushes.Gray; // Or use your RimworldHighlightBrush
        private const double InsertionThickness = 2.0;

        // Define a custom data format string
        private const string DraggedItemsFormat = "RimSharpModItemList";

        #region Dependency Properties

        // Command to execute on successful drop
        public static readonly DependencyProperty DropCommandProperty =
            DependencyProperty.Register(nameof(DropCommand), typeof(ICommand), typeof(ListBoxDragDropBehavior), new PropertyMetadata(null));

        public ICommand DropCommand
        {
            get { return (ICommand)GetValue(DropCommandProperty); }
            set { SetValue(DropCommandProperty, value); }
        }

        // The Type of *individual* item allowed (e.g., typeof(ModItem))
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
            this.AssociatedObject.PreviewDragEnter += AssociatedObject_PreviewDragEnter; // Use Enter instead of Over for initial check/adorner layer
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
                this.AssociatedObject.PreviewDragEnter -= AssociatedObject_PreviewDragEnter;
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

            ListBox listBox = AssociatedObject;
            var clickedItemContainer = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);

            // Check if clicking on a selected item in a multi-selection
            if (clickedItemContainer != null)
            {
                var clickedItem = listBox.ItemContainerGenerator.ItemFromContainer(clickedItemContainer);
                if (clickedItem != null && listBox.SelectedItems.Count > 1 && listBox.SelectedItems.Contains(clickedItem))
                {
                    // Prevent the ListBox from changing selection on click
                    e.Handled = true;
                }
            }

            _dragStartPoint = e.GetPosition(null); // Position relative to screen/window
                                                   // Reset drag state in case a previous drag was aborted
            _isDragging = false;
            _draggedItems = null;
            _draggedItemContainers = null;
        }


        private void AssociatedObject_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // Only process if the item type is set and mouse button is pressed
            if (e.LeftButton != MouseButtonState.Pressed || DragItemType == null || _isDragging)
                return;

            Point currentPosition = e.GetPosition(null);
            Vector dragVector = currentPosition - _dragStartPoint;

            // Start dragging only if the mouse has moved beyond a threshold
            if (dragVector.Length > SystemParameters.MinimumHorizontalDragDistance ||
                dragVector.Length > SystemParameters.MinimumVerticalDragDistance)
            {
                ListBox listBox = AssociatedObject;
                var clickedItemContainer = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);

                // Ensure the click was on a ListBoxItem
                if (clickedItemContainer == null) return;

                // Get the data item associated with the clicked container
                var clickedItemData = listBox.ItemContainerGenerator.ItemFromContainer(clickedItemContainer);

                // --- REVISED CHECK ---
                // 1. Check if the item clicked is valid (not null, correct type).
                // 2. Check if this clicked item is *part of the current selection*.
                if (clickedItemData != null && DragItemType.IsInstanceOfType(clickedItemData) && listBox.SelectedItems.Contains(clickedItemData))
                {
                    // The key fix: make sure we properly collect ALL selected items
                    var currentSelectedMods = new List<ModItem>();
                    foreach (var item in listBox.SelectedItems)
                    {
                        if (item is ModItem modItem)
                        {
                            currentSelectedMods.Add(modItem);
                        }
                    }


                    if (currentSelectedMods.Any())
                    {
                        _draggedItems = currentSelectedMods;
                        _isDragging = true;
                        Debug.WriteLine($"Starting drag for {_draggedItems.Count} item(s) from list: {ListGroupName}");

                        // Store original containers for visual feedback
                        _draggedItemContainers = new List<ListBoxItem>();
                        foreach (var item in _draggedItems) // Use the behavior's field
                        {
                            var container = listBox.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                            if (container != null)
                            {
                                _draggedItemContainers.Add(container);
                            }
                        }

                        // --- Adorner Setup ---
                        UIElement elementForAdorner = Application.Current.MainWindow as Window ?? (UIElement)AssociatedObject;

                        _adornerLayer = AdornerLayer.GetAdornerLayer(elementForAdorner);

                        if (_adornerLayer != null)
                        {
                            // Pass the behavior's field _draggedItems
                            _dragAdorner = new DragAdorner(elementForAdorner, _draggedItems, listBox.ItemTemplate);
                            _adornerLayer.Add(_dragAdorner);
                            UpdateDragAdornerPosition(e.GetPosition(elementForAdorner));
                        }
                        else
                        {
                            // *** LOG FIX *** Log the element type we tried
                            Debug.WriteLine($"Could not find AdornerLayer for element '{elementForAdorner?.GetType().Name}'. Drag adorner not shown.");
                        }

                        // Apply visual feedback to original items
                        SetItemsDraggingVisualState(true);

                        // --- Start Drag Operation ---
                        var dragData = new DataObject(DraggedItemsFormat, _draggedItems); // Pass the behavior's field

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
                    else
                    {
                        // This case means the clicked item WAS selected, but OfType<ModItem> returned nothing
                        // which implies SelectedItems contains non-ModItem objects, or is empty unexpectedly.
                        Debug.WriteLine($"Drag start check failed: Clicked item was selected, but no ModItems found in SelectedItems.");
                        _isDragging = false; // Ensure flag is reset
                        _draggedItems = null;
                        _draggedItemContainers = null;
                    }
                }
                else
                {
                    // Drag didn't start because the clicked item wasn't selected or wasn't valid.
                    // This is normal if clicking on empty space or an unselected item without modifiers.
                    // Debug.WriteLine($"Drag condition not met: ClickedItemData is null, wrong type, or not selected.");
                }
            }
        }



        private void AssociatedObject_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // If the mouse is released *before* a drag operation started (no movement threshold met),
            // or if DoDragDrop returns because the button was released outside a valid target.
            if (_isDragging)
            {
                // This case is less common now, as DoDragDrop's finally block or PreviewDrop handles most cleanup.
                // However, keep it as a safety net.
                Debug.WriteLine("MouseUp detected while IsDragging was true - cleaning up (Safety Net).");
                CleanupDragDrop();
            }
            // Reset flags even if drag didn't start
            _isDragging = false;
            _draggedItems = null;
            _draggedItemContainers = null;
        }

        private void AssociatedObject_PreviewDragEnter(object sender, DragEventArgs e)
        {
            // Get the adorner layer early if possible
            if (_adornerLayer == null)
            {
                // Don't use MainWindow - use the AssociatedObject (the ListBox) directly
                _adornerLayer = AdornerLayer.GetAdornerLayer(AssociatedObject);
                if (_adornerLayer == null)
                {
                    Debug.WriteLine($"PreviewDragEnter: Could not find AdornerLayer for ListBox '{ListGroupName}'.");
                }
            }

            // Check data format immediately
            if (!e.Data.GetDataPresent(DraggedItemsFormat))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                Debug.WriteLine($"DragEnter on {ListGroupName}: Invalid data format.");
                return;
            }
            Debug.WriteLine($"DragEnter on {ListGroupName}: Valid data format detected.");
            e.Effects = DragDropEffects.Move; // Tentatively allow move
            e.Handled = true;
        }




        private void AssociatedObject_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true; // Handle the event

            // Double-check data format (might be redundant if Enter handled it, but safe)
            if (!e.Data.GetDataPresent(DraggedItemsFormat))
            {
                e.Effects = DragDropEffects.None;
                RemoveInsertionAdorner();
                Debug.WriteLine($"DragOver on {ListGroupName}: Invalid data format.");
                return;
            }

            // Allow Move effect
            e.Effects = DragDropEffects.Move;

            ListBox listBox = AssociatedObject;
            Point currentPositionInListBox = e.GetPosition(listBox); // Position relative to the ListBox

            // Update drag adorner position (relative to its adorned element)
            if (_dragAdorner != null)
            {
                Point currentPositionInAdorned = e.GetPosition(_dragAdorner.AdornedElement);
                _dragAdorner.SetPosition(currentPositionInAdorned.X, currentPositionInAdorned.Y);
            }


            // --- Insertion Adorner Logic ---
            ListBoxItem targetItemContainer = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            int insertionIndex = -1;
            bool insertAbove = false;

            if (targetItemContainer != null)
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
                // Dragging over empty space or header/footer
                insertionIndex = CalculateIndexFromEmptySpace(listBox, currentPositionInListBox);
                if (insertionIndex == 0 && listBox.Items.Count > 0)
                {
                    // Near top but not over an item, insert above first item
                    var firstContainer = listBox.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
                    if (firstContainer != null) AddInsertionLine(listBox, firstContainer, true);
                    else AddInsertionLine(listBox, listBox, true); // Fallback
                }
                else if (insertionIndex == listBox.Items.Count && listBox.Items.Count > 0)
                {
                    // Near bottom but not over an item, insert below last item
                    var lastContainer = listBox.ItemContainerGenerator.ContainerFromIndex(listBox.Items.Count - 1) as FrameworkElement;
                    if (lastContainer != null) AddInsertionLine(listBox, lastContainer, false);
                    else AddInsertionLine(listBox, listBox, false); // Fallback
                }
                else // Empty list or truly empty space
                {
                    AddInsertionLine(listBox, listBox, true); // Default to top for empty list
                }
            }

            // Store the calculated index
            listBox.Tag = insertionIndex;
        }

        private int CalculateIndexFromEmptySpace(ListBox listBox, Point positionInListBox)
        {
            if (listBox.Items.Count == 0) return 0;

            // Check if position is above the first item
            var firstContainer = listBox.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
            if (firstContainer != null)
            {
                Point firstItemPos = firstContainer.TranslatePoint(new Point(0, 0), listBox);
                if (positionInListBox.Y < firstItemPos.Y) return 0;
            }

            // Check if position is below the last item
            var lastContainer = listBox.ItemContainerGenerator.ContainerFromIndex(listBox.Items.Count - 1) as ListBoxItem;
            if (lastContainer != null)
            {
                Point lastItemPos = lastContainer.TranslatePoint(new Point(0, 0), listBox);
                if (positionInListBox.Y > lastItemPos.Y + lastContainer.ActualHeight) return listBox.Items.Count;
            }

            // If between items (but not directly over one), find the closest insertion point
            // This is more complex, often defaulting to the end works ok here if not over an item.
            // For simplicity, if not above first or below last, assume end.
            return listBox.Items.Count;
        }

        private void AssociatedObject_PreviewDrop(object sender, DragEventArgs e)
        {
            Debug.WriteLine($"=== DROP START on {ListGroupName} ===");
            e.Handled = true; // Handle the drop event
            RemoveInsertionAdorner(); // Clean up insertion line

            // Verify data format
            if (!e.Data.GetDataPresent(DraggedItemsFormat))
            {
                Debug.WriteLine("Drop failed: Data format mismatch.");
                CleanupDragDrop();
                return;
            }

            // Extract the data
            var droppedData = e.Data.GetData(DraggedItemsFormat) as List<ModItem>;
            if (droppedData == null || !droppedData.Any())
            {
                Debug.WriteLine("Drop failed: Could not cast data to List<ModItem> or list is empty.");
                CleanupDragDrop();
                return;
            }
            Debug.WriteLine($"Dropped {droppedData.Count} items.");


            ListBox listBox = AssociatedObject;
            // Retrieve the index calculated during DragOver
            int dropIndex = listBox.Tag is int idx ? idx : -1;

            // If dropIndex is -1 (wasn't over listbox area that calculated index),
            // determine index based on drop position relative to items.
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
                    // Dropped on empty space, calculate index again
                    dropIndex = CalculateIndexFromEmptySpace(listBox, dropPosition);
                    Debug.WriteLine($"Drop index calculated from empty space: {dropIndex}");
                }
            }

            // Clamp index just in case
            dropIndex = Math.Clamp(dropIndex, 0, listBox.Items.Count);
            Debug.WriteLine($"Final Drop Index: {dropIndex}");


            // Create arguments for the command
            var args = new DropModArgs
            {
                DroppedItems = droppedData, // Use the list
                TargetListName = this.ListGroupName,
                DropIndex = dropIndex
            };

            // Execute the command
            if (DropCommand != null && DropCommand.CanExecute(args))
            {
                Debug.WriteLine($"Executing DropCommand: {args.DroppedItems.Count} Items, Target='{args.TargetListName}', Index={args.DropIndex}");
                DropCommand.Execute(args);
            }
            else
            {
                Debug.WriteLine("DropCommand is null or cannot execute.");
            }

            // Cleanup occurs AFTER command execution
            Debug.WriteLine("Drop finished, calling CleanupDragDrop.");
            CleanupDragDrop(); // General cleanup after drop
        }

        private void AssociatedObject_PreviewDragLeave(object sender, DragEventArgs e)
        {
            // Remove insertion adorner if the mouse leaves the ListBox bounds
            Point pos = e.GetPosition(AssociatedObject);
            Rect bounds = new Rect(0, 0, AssociatedObject.ActualWidth, AssociatedObject.ActualHeight);
            if (!bounds.Contains(pos))
            {
                RemoveInsertionAdorner();
                AssociatedObject.Tag = null; // Clear stored index
                Debug.WriteLine($"DragLeave detected from list: {ListGroupName}");
            }
            // Don't cleanup drag adorner here, it follows the mouse
        }

        private void AssociatedObject_QueryContinueDrag(object sender, QueryContinueDragEventArgs e)
        {
            // Update drag adorner continuously if button is pressed
            if (e.KeyStates.HasFlag(DragDropKeyStates.LeftMouseButton))
            {
                if (_dragAdorner != null)
                {
                    // Need position relative to the element the adorner is attached to
                    Point currentPos = GetMousePosition(_dragAdorner.AdornedElement);
                    _dragAdorner.SetPosition(currentPos.X, currentPos.Y);
                }
                e.Action = DragAction.Continue;
            }
            else // Mouse button is NOT pressed OR Escape is pressed
            {
                if (e.EscapePressed)
                {
                    Debug.WriteLine("Drag cancelled by Escape key.");
                    e.Action = DragAction.Cancel;
                    CleanupDragDrop(); // Clean up everything on cancel
                    e.Handled = true; // Prevent further processing for cancel
                }
                else // Button released (not Escape) - let PreviewDrop or MouseUp handle it
                {
                    Debug.WriteLine("Drag ended (mouse button released or drop occurred). Action determined by Drop/Leave/Up handlers.");
                    e.Action = DragAction.Continue; // Let framework determine Drop or Cancel based on target
                                                    // Cleanup will happen in PreviewDrop or PreviewMouseLeftButtonUp/DoDragDrop finally block
                }
            }
        }


        // --- Adorner and Visual State Helper Methods ---

        private void AddInsertionLine(ListBox listBox, FrameworkElement relativeTo, bool above)
        {
            if (_adornerLayer == null) { /* Already logged in DragEnter */ return; }

            RemoveInsertionAdorner(); // Remove previous one first

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

        // Sets/Resets visual state (e.g., opacity) for dragged items
        private void SetItemsDraggingVisualState(bool isDragging)
        {
            if (_draggedItemContainers == null) return;

            foreach (var container in _draggedItemContainers)
            {
                if (container != null)
                {
                    // Option 1: Direct Opacity
                    // container.Opacity = isDragging ? 0.5 : 1.0;

                    // Option 2: Using Tag and Style Trigger (as defined in XAML)
                    container.Tag = isDragging ? "Dragging" : null;
                }
            }
        }

        private void CleanupDragDrop()
        {
            if (!_isDragging && _dragAdorner == null && _insertionAdorner == null)
            {
                // Avoid redundant cleanup if already cleaned or drag never started fully
                // Debug.WriteLine("CleanupDragDrop skipped (already clean or drag didn't fully start).");
                return;
            }

            Debug.WriteLine($"CleanupDragDrop called. Current IsDragging: {_isDragging}");
            RemoveInsertionAdorner();
            RemoveDragAdorner();

            // Reset visual state of original items
            SetItemsDraggingVisualState(false);

            // Clear state variables
            if (AssociatedObject != null) AssociatedObject.Tag = null; // Clear stored index
            _isDragging = false;
            _draggedItems = null; // Clear the reference to the dragged data
            _draggedItemContainers = null; // Clear container references
                                           // _adornerLayer reference can persist, it's often shared/reused.
            Debug.WriteLine($"CleanupDragDrop finished.");
        }


        // --- Visual Tree Helpers ---

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

        // Helper to get mouse position relative to an element
        private static Point GetMousePosition(Visual relativeTo) // Keep Visual here is okay
        {
            Win32Point w32Mouse = new Win32Point();
            NativeMethods.GetCursorPos(ref w32Mouse);
            Point screenPoint = new Point(w32Mouse.X, w32Mouse.Y);

            // Check if relativeTo is connected to presentation source (visual tree)
            PresentationSource source = PresentationSource.FromVisual(relativeTo);

            if (source?.RootVisual != null)
            {
                try
                {
                    // Transform screen coords to coords relative to the visual element
                    return source.RootVisual.TransformToDescendant(relativeTo).Transform(screenPoint);
                    // Alternative using PointFromScreen if the visual is a UIElement directly connected
                    // return relativeTo.PointFromScreen(screenPoint);
                }
                catch (InvalidOperationException ex) // May happen if transforms are not possible
                {
                    Debug.WriteLine($"Warning: GetMousePosition transform failed for {relativeTo.GetType().Name}: {ex.Message}. Returning screen coordinates relative to RootVisual or Screen.");
                    try
                    {
                        // Fallback: Position relative to the root visual of the source
                        return source.RootVisual.PointFromScreen(screenPoint);
                    }
                    catch
                    {
                        return screenPoint; // Absolute fallback: Screen coordinates
                    }
                }
                catch (Exception ex) // Catch other potential exceptions
                {
                    Debug.WriteLine($"Warning: GetMousePosition general error for {relativeTo.GetType().Name}: {ex.Message}. Returning screen coordinates.");
                    return screenPoint; // Absolute fallback: Screen coordinates
                }
            }
            else
            {
                Debug.WriteLine($"Warning: GetMousePosition could not find PresentationSource for {relativeTo.GetType().Name}. Returning screen coordinates.");
                // If not connected, PointFromScreen will fail. Return raw screen coords.
                return screenPoint;
            }
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
