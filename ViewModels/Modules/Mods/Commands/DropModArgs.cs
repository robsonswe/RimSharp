using RimSharp.Models;
using System.Collections.Generic; // Required for List

namespace RimSharp.ViewModels.Modules.Mods.Commands
{
    public class DropModArgs
    {
        // public ModItem DroppedItem { get; set; } // Replaced
        public List<ModItem> DroppedItems { get; set; } // Changed to List
        public string TargetListName { get; set; }
        public int DropIndex { get; set; }
    }
}
