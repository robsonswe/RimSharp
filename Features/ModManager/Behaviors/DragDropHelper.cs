using System.Collections.Generic; // Required for List<>
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using RimSharp.Shared.Models;


namespace RimSharp.Features.ModManager.Behaviors
{
    /// <summary>
    /// NOTE: This helper seems designed for single-item drag initiation.
    /// The primary drag logic for the ListBox is now handled by ListBoxDragDropBehavior,
    /// which supports multi-item dragging. Consider if this helper is still necessary.
    /// </summary>
    public static class DragDropHelper
    {
        public static void StartDrag(ListBox listBox, ModItem draggedItem)
        {
            var dragData = new DataObject(typeof(ModItem), draggedItem); // DataObject might need update if drop target expects the list format

            // Create adorner
            var adornerLayer = AdornerLayer.GetAdornerLayer(listBox);
            if (adornerLayer == null) return; // Safety check

            // *** FIX: Wrap the single item in a List<> to match the constructor ***
            var adorner = new DragAdorner(listBox, new List<ModItem> { draggedItem }, listBox.ItemTemplate);
            // *** END FIX ***

            adornerLayer.Add(adorner);

            // Update adorner position during drag
            void UpdateAdornerPosition(object sender, MouseEventArgs e)
            {
                if (adorner != null) // Check if adorner exists
                {
                    var position = e.GetPosition(listBox);
                    adorner.SetPosition(position.X, position.Y);
                }
            }

            listBox.PreviewMouseMove += UpdateAdornerPosition;

            try
            {
                // Note: The DataObject here contains typeof(ModItem), but the ListBoxDragDropBehavior uses
                // a custom format ("RimSharpModItemList") and passes a List<ModItem>.
                // If the drop target relies on the behavior's format, this drag initiated
                // by the helper might not be recognized correctly on drop.
                DragDrop.DoDragDrop(listBox, dragData, DragDropEffects.Move);
            }
            finally // Ensure cleanup happens
            {
                // Cleanup
                listBox.PreviewMouseMove -= UpdateAdornerPosition;
                 try
                 {
                     if (adorner != null) // Check before removing
                     {
                         adornerLayer.Remove(adorner);
                     }
                 }
                 catch (System.Exception ex)
                 {
                      System.Diagnostics.Debug.WriteLine($"Error removing adorner in DragDropHelper: {ex.Message}");
                 }
            }
        }
    }
}
