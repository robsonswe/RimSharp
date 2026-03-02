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
        public bool ContainsHardIncompatibility { get; private set; }

        public IncompatibilityGroupViewModel(IncompatibilityGroup group)
        {
            Group = group;
            GroupName = $"IncompatibilityGroup_{group.GetHashCode()}"; // Create a unique name

            ContainsHardIncompatibility = group.IncompatibilityRelations.Any(r => 
                r.Reason.Trim().StartsWith("[Hard]", StringComparison.OrdinalIgnoreCase));

            GenerateResolutionOptions();
        }

        private void GenerateResolutionOptions()
        {

            if (!ContainsHardIncompatibility)
            {
                var keepAllOption = new IncompatibilityResolutionOption(null, this)
                {
                    IsSelected = true 
                };
                ResolutionOptions.Add(keepAllOption);
            }

            var allInvolvedMods = Group.InvolvedMods.ToList();

            // Create resolution options for each mod
            foreach (var mod in allInvolvedMods)
            {

                if (!Group.IncompatibilityRelations.Any(r => r.TargetMod == mod || r.SourceMod == mod))
                    continue;

                var option = new IncompatibilityResolutionOption(mod, this);

foreach (var relation in Group.IncompatibilityRelations.Where(r => r.TargetMod == mod))
                {
                    option.AddRelation(relation);
                }

                if (option.ModsToRemove.Count > 0)
                {
                    ResolutionOptions.Add(option);
                }
            }

            if (ContainsHardIncompatibility && ResolutionOptions.Count > 0)
            {
                ResolutionOptions[0].IsSelected = true;
            }
        }

        public ModItem? GetSelectedModToKeep()
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


