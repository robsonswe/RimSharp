using RimSharp.Models;

namespace RimSharp.ViewModels.Modules.Mods.Commands
{
    public class DropModArgs
    {
        public ModItem DroppedItem { get; set; }
        public string TargetListName { get; set; }
        public int DropIndex { get; set; }
    }
}
