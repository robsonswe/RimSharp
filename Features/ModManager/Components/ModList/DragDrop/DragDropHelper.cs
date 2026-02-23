using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using RimSharp.Shared.Models;

namespace RimSharp.Features.ModManager.Components.ModList.DragDrop
{
    public static class DragDropHelper
    {
        private const string DraggedItemsFormat = "RimSharpModItemList";

        public static async Task StartDragAsync(PointerEventArgs e, IEnumerable<ModItem> draggedItems)
        {
            var itemsList = draggedItems.ToList();
            if (!itemsList.Any()) return;

            var data = new DataObject();
            data.Set(DraggedItemsFormat, itemsList);

            await Avalonia.Input.DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        }
    }
}
