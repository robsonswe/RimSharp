using System;
using System.Collections.Generic;
using System.Linq;
using RimSharp.AppDir.AppFiles;
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Shared.Models;

namespace RimSharp.Features.ModManager.Dialogs.Incompatibilities
{
    public class IncompatibilityResolutionOption : ViewModelBase
    {
        public ModItem? ModToKeep { get; }
        public List<ModItem> ModsToRemove { get; } = new List<ModItem>();
        public IncompatibilityGroupViewModel ParentGroup { get; }

        public bool IsKeepAllOption => ModToKeep == null;
        public string PillLabel => ModToKeep == null ? "Keep All (Ignore)" : $"Keep: {ModToKeep.DisplayName}";

        public IncompatibilityResolutionOption(ModItem? modToKeep, IncompatibilityGroupViewModel parentGroup)
        {
            ModToKeep = modToKeep;
            ParentGroup = parentGroup ?? throw new ArgumentNullException(nameof(parentGroup));
        }

        public Dictionary<ModItem, List<ModIncompatibilityRelation>> RemovalReasons { get; } =
            new Dictionary<ModItem, List<ModIncompatibilityRelation>>();

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    ParentGroup.NotifySelectionChanged();
                }
            }
        }

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

            if (!RemovalReasons[modToRemove].Any(r => r.Reason == relation.Reason))
            {
                RemovalReasons[modToRemove].Add(relation);
            }
        }

        public string GetPrimaryRemovalReason(ModItem mod)
        {
            if (!RemovalReasons.ContainsKey(mod) || RemovalReasons[mod].Count == 0)
                return "Unknown incompatibility";

            var directIncompatibility = RemovalReasons[mod]
                .FirstOrDefault(r => r.Reason.StartsWith("Direct incompatibility"));

            return directIncompatibility != null
                ? directIncompatibility.Reason
                : RemovalReasons[mod][0].Reason;
        }
    }
}