using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using Avalonia.Media;
using RimSharp.Features.ModManager.Services.Commands;
using RimSharp.Features.ModManager.Components.ModList.DragDrop;
using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls.Primitives;

namespace RimSharp.Features.ModManager.Behaviors
{
    public class ListBoxDragDropBehavior : Behavior<ListBox>
    {
        private Point _dragStartPoint;
        private bool _isDragging = false;
        private const string DraggedItemsFormat = "RimSharpModItemList";
        private InsertionAdorner? _insertionAdorner;
        private IBrush? _insertionBrush;

        public static readonly StyledProperty<ICommand?> DropCommandProperty =
            AvaloniaProperty.Register<ListBoxDragDropBehavior, ICommand?>(nameof(DropCommand));

        public ICommand? DropCommand
        {
            get => GetValue(DropCommandProperty);
            set => SetValue(DropCommandProperty, value);
        }

        public static readonly StyledProperty<Type?> DragItemTypeProperty =
            AvaloniaProperty.Register<ListBoxDragDropBehavior, Type?>(nameof(DragItemType));

        public Type? DragItemType
        {
            get => GetValue(DragItemTypeProperty);
            set => SetValue(DragItemTypeProperty, value);
        }

        public static readonly StyledProperty<string> ListGroupNameProperty =
            AvaloniaProperty.Register<ListBoxDragDropBehavior, string>(nameof(ListGroupName), "DefaultGroup");

        public string ListGroupName
        {
            get => GetValue(ListGroupNameProperty);
            set => SetValue(ListGroupNameProperty, value);
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject != null)
            {
                DragDrop.SetAllowDrop(AssociatedObject, true);
                AssociatedObject.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
                AssociatedObject.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
                AssociatedObject.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);

                AssociatedObject.AddHandler(DragDrop.DragOverEvent, OnDragOver);
                AssociatedObject.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
                AssociatedObject.AddHandler(DragDrop.DropEvent, OnDrop);
                if (Application.Current?.Resources.TryGetResource("RimworldHighlightBrush", null, out var res) == true)
                {
                    _insertionBrush = res as IBrush;
                }
                _insertionBrush ??= Brushes.Gray;
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject != null)
            {
                AssociatedObject.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
                AssociatedObject.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
                AssociatedObject.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);

                AssociatedObject.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
                AssociatedObject.RemoveHandler(DragDrop.DragLeaveEvent, OnDragLeave);
                AssociatedObject.RemoveHandler(DragDrop.DropEvent, OnDrop);
            }
            RemoveInsertionAdorner();
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (AssociatedObject == null || _isDragging) return;

            var properties = e.GetCurrentPoint(AssociatedObject).Properties;
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                var pos = e.GetPosition(AssociatedObject);
                var visual = AssociatedObject.InputHitTest(pos) as Visual;
                var listBoxItem = GetParentListBoxItem(visual);

                if (listBoxItem != null)
                {
                    if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && 
                        !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        AssociatedObject.SelectedItem = listBoxItem.DataContext;
                    }
                }
            }
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject == null) return;

            var properties = e.GetCurrentPoint(AssociatedObject).Properties;
            if (properties.IsLeftButtonPressed)
            {
                _dragStartPoint = e.GetPosition(AssociatedObject);

                // IDENTIFY the clicked item
                var visual = AssociatedObject.InputHitTest(_dragStartPoint) as Visual;
                var listBoxItem = GetParentListBoxItem(visual);

                if (listBoxItem != null)
                {
                    var clickedItem = listBoxItem.DataContext;

if (AssociatedObject.SelectedItems != null && 
                        AssociatedObject.SelectedItems.Count > 1 && 
                        AssociatedObject.SelectedItems.Contains(clickedItem) &&
                        e.KeyModifiers == KeyModifiers.None)
                    {
                        e.Handled = true;
                    }
                }
            }
        }

        private DragAdorner? _dragAdorner;

        private async void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_isDragging || AssociatedObject == null || DragItemType == null) return;

            if (e.GetCurrentPoint(AssociatedObject).Properties.IsLeftButtonPressed)
            {
                var currentPos = e.GetPosition(AssociatedObject);
                var delta = currentPos - _dragStartPoint;

                if (Math.Abs(delta.X) > 3 || Math.Abs(delta.Y) > 3)
                {
                    var visual = AssociatedObject.InputHitTest(_dragStartPoint) as Visual;
                    var listBoxItem = GetParentListBoxItem(visual);

                    if (listBoxItem != null && AssociatedObject.SelectedItems != null && 
                        AssociatedObject.SelectedItems.Contains(listBoxItem.DataContext))
                    {

                        var selectedItems = AssociatedObject.SelectedItems.OfType<ModItem>().ToList();

                        System.Diagnostics.Debug.WriteLine($"[DragDrop] Selection contains {AssociatedObject.SelectedItems.Count} items. Filtered to {selectedItems.Count} ModItems.");

                        if (selectedItems.Count > 0)
                        {
                            _isDragging = true;
                            var data = new DataObject();
                            data.Set(DraggedItemsFormat, selectedItems);

                            // Setup Drag Adorner
                            var layer = AdornerLayer.GetAdornerLayer(AssociatedObject);
                            if (layer != null)
                            {
                                _dragAdorner = new DragAdorner(selectedItems, AssociatedObject.ItemTemplate);
                                AdornerLayer.SetAdorner(AssociatedObject, _dragAdorner);
                                _dragAdorner.SetPosition(e.GetPosition(AssociatedObject));
                            }

                            System.Diagnostics.Debug.WriteLine($"[DragDrop] Starting drag for {selectedItems.Count} items.");

                            try
                            {
                                var result = await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
                                System.Diagnostics.Debug.WriteLine($"[DragDrop] Completed with result: {result}");
                            }
                            finally
                            {
                                _isDragging = false;
                                RemoveDragAdorner();
                                RemoveInsertionAdorner();
                            }
                        }
                    }
                }
            }
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            if (AssociatedObject == null) return;

            if (e.Data.Contains(DraggedItemsFormat))
            {
                e.DragEffects = DragDropEffects.Move;

                var dropPos = e.GetPosition(AssociatedObject);
                UpdateInsertionAdorner(dropPos);

                if (_dragAdorner != null)
                {
                    _dragAdorner.SetPosition(dropPos);
                }
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
                RemoveInsertionAdorner();
            }
        }

        private void RemoveDragAdorner()
        {
            if (AssociatedObject != null)
            {
                AdornerLayer.SetAdorner(AssociatedObject, null);
            }
            _dragAdorner = null;
        }

        private void OnDragLeave(object? sender, DragEventArgs e)
        {
            RemoveInsertionAdorner();
        }

        private void OnDrop(object? sender, DragEventArgs e)
        {
            RemoveInsertionAdorner();

            if (AssociatedObject == null || !e.Data.Contains(DraggedItemsFormat)) return;

            var rawData = e.Data.Get(DraggedItemsFormat);
            var droppedData = rawData as List<ModItem>;

            if (droppedData == null && rawData is IEnumerable<ModItem> enumData)
            {
                droppedData = enumData.ToList();
            }

            if (droppedData == null || !droppedData.Any())
            {
                System.Diagnostics.Debug.WriteLine($"[DragDrop] Drop failed: Data is null or empty. Type: {rawData?.GetType().Name}");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[DragDrop] Dropped {droppedData.Count} items on {this.ListGroupName}.");

            var dropPos = e.GetPosition(AssociatedObject);
            int dropIndex = CalculateDropIndex(dropPos);

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

            e.Handled = true;
        }

        private void UpdateInsertionAdorner(Point point)
        {
            if (AssociatedObject == null) return;

            // Find the item under the point
            var items = AssociatedObject.ItemsSource ?? AssociatedObject.Items;
            if (items == null) return;

            Control? targetContainer = null;
            bool isAbove = false;

            foreach (var item in items)
            {
                var container = AssociatedObject.ContainerFromItem(item) as Control;
                if (container != null)
                {
                    var bounds = container.Bounds;
                    if (point.Y < bounds.Bottom)
                    {
                        targetContainer = container;
                        isAbove = point.Y < bounds.Center.Y;
                        break;
                    }
                }
            }

            if (targetContainer == null && AssociatedObject.ItemCount > 0)
            {
                targetContainer = AssociatedObject.ContainerFromIndex(AssociatedObject.ItemCount - 1) as Control;
                isAbove = false;
            }

            if (targetContainer != null)
            {
                var layer = AdornerLayer.GetAdornerLayer(AssociatedObject);
                if (layer != null)
                {
                    if (_insertionAdorner == null || !ReferenceEquals(layer, _insertionAdorner.Parent))
                    {
                        RemoveInsertionAdorner();
                        _insertionAdorner = new InsertionAdorner(targetContainer, isAbove, _insertionBrush!, 2.0);
                        AdornerLayer.SetAdorner(AssociatedObject, _insertionAdorner);
                    }
                    else
                    {

                        RemoveInsertionAdorner();
                        _insertionAdorner = new InsertionAdorner(targetContainer, isAbove, _insertionBrush!, 2.0);
                        AdornerLayer.SetAdorner(AssociatedObject, _insertionAdorner);
                    }
                }
            }
            else
            {
                RemoveInsertionAdorner();
            }
        }

        private void RemoveInsertionAdorner()
        {
            if (AssociatedObject != null)
            {
                AdornerLayer.SetAdorner(AssociatedObject, null);
            }
            _insertionAdorner = null;
        }

        private int CalculateDropIndex(Point point)
        {
            if (AssociatedObject == null) return 0;

            int index = 0;

            var items = AssociatedObject.ItemsSource ?? AssociatedObject.Items;
            if (items == null) return 0;

            foreach (var item in items)
            {
                var container = AssociatedObject.ContainerFromItem(item);
                if (container != null)
                {

                    var bounds = container.Bounds;
                    double midPoint = bounds.Y + (bounds.Height / 2);

                    if (point.Y > midPoint)
                    {
                        index++;
                    }
                    else
                    {
                        // Found the spot!
                        return index;
                    }
                }
            }
            return index; // Append to end if not found spot
        }

        private ListBoxItem? GetParentListBoxItem(Visual? visual)
        {
            while (visual != null)
            {
                if (visual is ListBoxItem item) return item;
                visual = visual.GetVisualParent();
            }
            return null;
        }
    }
}


