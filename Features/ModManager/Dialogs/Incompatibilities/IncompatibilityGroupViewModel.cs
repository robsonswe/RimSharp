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

        // New property to determine the group type
        public bool ContainsHardIncompatibility { get; private set; }
        
        public IncompatibilityGroupViewModel(IncompatibilityGroup group)
        {
            Group = group;
            GroupName = $"IncompatibilityGroup_{group.GetHashCode()}"; // Create a unique name
            
            // Determine if the group has any hard incompatibilities
            ContainsHardIncompatibility = group.IncompatibilityRelations.Any(r => 
                r.Reason.Trim().StartsWith("[Hard]", StringComparison.OrdinalIgnoreCase));

            GenerateResolutionOptions();
        }
        
        private void GenerateResolutionOptions()
        {
            // If there are NO hard incompatibilities, add the "Keep All" option first.
            if (!ContainsHardIncompatibility)
            {
                var keepAllOption = new IncompatibilityResolutionOption
                {
                    ModToKeep = null, // Using null to signify "Keep All"
                    ParentGroup = this,
                    IsSelected = true // This is the new default for soft groups
                };
                ResolutionOptions.Add(keepAllOption);
            }

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
            
            // If it's a hard group (so we didn't add "Keep All"), select the first option as the default.
            if (ContainsHardIncompatibility && ResolutionOptions.Count > 0)
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