using System;
using System.Collections.Generic;
using System.Linq;
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Shared.Models;

namespace RimSharp.Features.ModManager.Dialogs.Incompatibilities
{
    public class IncompatibilityResolutionOption
    {
        public ModItem? ModToKeep { get; }
        public List<ModItem> ModsToRemove { get; } = new List<ModItem>();
        public IncompatibilityGroupViewModel ParentGroup { get; }

        public IncompatibilityResolutionOption(ModItem? modToKeep, IncompatibilityGroupViewModel parentGroup)
        {
            ModToKeep = modToKeep;
            ParentGroup = parentGroup ?? throw new ArgumentNullException(nameof(parentGroup));
        }

// Relations grouped by the mod to remove

        public Dictionary<ModItem, List<ModIncompatibilityRelation>> RemovalReasons { get; } = 
            new Dictionary<ModItem, List<ModIncompatibilityRelation>>();

        public bool IsSelected { get; set; }

        public void AddRelation(ModIncompatibilityRelation relation)
        {

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


