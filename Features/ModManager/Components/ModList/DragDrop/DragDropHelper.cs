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
        // Avalonia 12: DataObject/string formats replaced by DataFormat + DataTransfer (PR #20521).
        private static readonly DataFormat<List<ModItem>> DraggedItemsDataFormat =
            DataFormat.CreateInProcessFormat<List<ModItem>>("rimsharp-mod-item-list");

        public static async Task StartDragAsync(PointerPressedEventArgs e, IEnumerable<ModItem> draggedItems)
        {
            var itemsList = draggedItems.ToList();
            if (!itemsList.Any()) return;

            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(DraggedItemsDataFormat, itemsList));

            await Avalonia.Input.DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
    }
}
