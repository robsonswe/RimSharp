using RimSharp.Models;

namespace RimSharp.ViewModels.Modules.Mods.Management
{
    /// <summary>
    /// Represents a relation between two mods that are incompatible,
    /// with a reason for the incompatibility
    /// </summary>
    public class ModIncompatibilityRelation
    {
        public ModItem SourceMod { get; set; }
        public ModItem TargetMod { get; set; }
        public string Reason { get; set; }

        public ModIncompatibilityRelation(ModItem sourceMod, ModItem targetMod, string reason)
        {
            SourceMod = sourceMod;
            TargetMod = targetMod;
            Reason = reason;
        }
    }
    public class ModIncompatibility
    {
        public ModItem SourceMod { get; set; }
        public ModItem TargetMod { get; set; }
        public string Reason { get; set; }

        public ModIncompatibility(ModItem sourceMod, ModItem targetMod, string reason)
        {
            SourceMod = sourceMod;
            TargetMod = targetMod;
            Reason = reason;
        }
    }

}