using System.Collections.Generic;
using RimSharp.Shared.Models;

namespace RimSharp.Features.ModManager.Services.Commands
{
    public class DropModArgs
    {
        // public ModItem DroppedItem { get; set; } // Replaced
        public List<ModItem> DroppedItems { get; set; } // Changed to List
        public string TargetListName { get; set; }
        public int DropIndex { get; set; }
    }
}
