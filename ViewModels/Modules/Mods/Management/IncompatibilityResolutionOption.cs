using RimSharp.Models;
using System.Collections.Generic;
using System.Linq;

namespace RimSharp.ViewModels.Modules.Mods.Management
{
    public class IncompatibilityResolutionOption
    {
        public ModItem ModToKeep { get; set; }
        public List<ModItem> ModsToRemove { get; set; } = new List<ModItem>();
        public IncompatibilityGroupViewModel ParentGroup { get; set; }

        
        // Relations grouped by the mod to remove
        // Key: Mod to remove, Value: List of relations explaining why it needs to be removed
        public Dictionary<ModItem, List<ModIncompatibilityRelation>> RemovalReasons { get; } = 
            new Dictionary<ModItem, List<ModIncompatibilityRelation>>();
        
        public bool IsSelected { get; set; }
        
        public void AddRelation(ModIncompatibilityRelation relation)
        {
            // If relation targets our mod to keep, we need to remove the source
            if (relation.TargetMod == ModToKeep)
            {
                AddModToRemove(relation.SourceMod, relation);
            }
        }
        
        private void AddModToRemove(ModItem modToRemove, ModIncompatibilityRelation relation)
        {
            if (!ModsToRemove.Contains(modToRemove))
            {
                ModsToRemove.Add(modToRemove);
            }
            
            if (!RemovalReasons.ContainsKey(modToRemove))
            {
                RemovalReasons[modToRemove] = new List<ModIncompatibilityRelation>();
            }
            
            // Don't add duplicate relations with the same reason
            if (!RemovalReasons[modToRemove].Any(r => r.Reason == relation.Reason))
            {
                RemovalReasons[modToRemove].Add(relation);
            }
        }
        
        // Get the primary reason why a mod needs to be removed
        public string GetPrimaryRemovalReason(ModItem mod)
        {
            if (!RemovalReasons.ContainsKey(mod) || RemovalReasons[mod].Count == 0)
                return "Unknown incompatibility";
                
            // Prioritize direct incompatibilities
            var directIncompatibility = RemovalReasons[mod]
                .FirstOrDefault(r => r.Reason.StartsWith("Direct incompatibility"));
                
            if (directIncompatibility != null)
                return directIncompatibility.Reason;
                
            // Otherwise return the first reason
            return RemovalReasons[mod][0].Reason;
        }
    }
}