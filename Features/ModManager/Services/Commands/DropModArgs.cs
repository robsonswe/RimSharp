using System.Collections.Generic;
using RimSharp.Shared.Models;

namespace RimSharp.Features.ModManager.Services.Commands
{
    public class DropModArgs
    {
        // public ModItem DroppedItem { get; set; } 
        public List<ModItem> DroppedItems { get; set; } = new();
        public string TargetListName { get; set; } = string.Empty;
        public int DropIndex { get; set; }
    }
}


