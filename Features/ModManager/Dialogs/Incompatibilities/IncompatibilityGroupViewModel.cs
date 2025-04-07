using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Shared.Models;

namespace RimSharp.Features.ModManager.Dialogs.Incompatibilities
{
    public class IncompatibilityGroupViewModel
    {
        public ObservableCollection<IncompatibilityResolutionOption> ResolutionOptions { get; } 
            = new ObservableCollection<IncompatibilityResolutionOption>();
            
        public IncompatibilityGroup Group { get; }
            public string GroupName { get; } = Guid.NewGuid().ToString();

        
        public IncompatibilityGroupViewModel(IncompatibilityGroup group)
        {
            Group = group;
            GroupName = $"IncompatibilityGroup_{group.GetHashCode()}"; // Create a unique name
            GenerateResolutionOptions();
        }
        
        private void GenerateResolutionOptions()
        {
            var allInvolvedMods = Group.InvolvedMods.ToList();
            
            // Create resolution options for each mod
            foreach (var mod in allInvolvedMods)
            {
                // Skip if mod is not involved in any relations as target
                if (!Group.IncompatibilityRelations.Any(r => r.TargetMod == mod || r.SourceMod == mod))
                    continue;
                    
                var option = new IncompatibilityResolutionOption
                {
                    ModToKeep = mod,
                    ParentGroup = this
                };
                
                // Find all relations where this mod is the target
                // These indicate mods that are incompatible with this one
                foreach (var relation in Group.IncompatibilityRelations.Where(r => r.TargetMod == mod))
                {
                    option.AddRelation(relation);
                }
                
                if (option.ModsToRemove.Count > 0)
                {
                    ResolutionOptions.Add(option);
                }
            }
            
            if (ResolutionOptions.Count > 0)
            {
                ResolutionOptions[0].IsSelected = true;
            }
        }
        
        public ModItem GetSelectedModToKeep()
        {
            var selectedOption = ResolutionOptions.FirstOrDefault(o => o.IsSelected);
            return selectedOption?.ModToKeep;
        }
        
        public List<ModItem> GetSelectedModsToRemove()
        {
            var selectedOption = ResolutionOptions.FirstOrDefault(o => o.IsSelected);
            return selectedOption?.ModsToRemove ?? new List<ModItem>();
        }
    }
}