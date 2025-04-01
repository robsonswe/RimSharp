using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using RimSharp.Models;

namespace RimSharp.Views.Modules.Mods.DragAdorner
{
    public static class DragDropHelper
    {
        public static void StartDrag(ListBox listBox, ModItem draggedItem)
        {
            var dragData = new DataObject(typeof(ModItem), draggedItem);
            
            // Create adorner
            var adornerLayer = AdornerLayer.GetAdornerLayer(listBox);
            var adorner = new DragAdorner(listBox, draggedItem, listBox.ItemTemplate);
            adornerLayer.Add(adorner);

            // Update adorner position during drag
            void UpdateAdornerPosition(object sender, MouseEventArgs e)
            {
                var position = e.GetPosition(listBox);
                adorner.SetPosition(position.X, position.Y);
            }

            listBox.PreviewMouseMove += UpdateAdornerPosition;

            DragDrop.DoDragDrop(listBox, dragData, DragDropEffects.Move);

            // Cleanup
            listBox.PreviewMouseMove -= UpdateAdornerPosition;
            adornerLayer.Remove(adorner);
        }
    }
}