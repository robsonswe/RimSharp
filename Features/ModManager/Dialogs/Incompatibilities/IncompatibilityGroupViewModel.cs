using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RimSharp.AppDir.AppFiles;
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Shared.Models;

namespace RimSharp.Features.ModManager.Dialogs.Incompatibilities
{
    public sealed class IncompatibilityRemovalSummary
    {
        public string ModName { get; }
        public string ReasonText { get; }

        public IncompatibilityRemovalSummary(string modName, string reasonText)
        {
            ModName = modName;
            ReasonText = reasonText;
        }
    }

    public class IncompatibilityGroupViewModel : ViewModelBase
    {
        public ObservableCollection<IncompatibilityResolutionOption> ResolutionOptions { get; } = new();

        public IncompatibilityGroup Group { get; }
        public string GroupName { get; }
        public bool ContainsHardIncompatibility { get; private set; }
        public string GroupTitle { get; }
        public string SeverityBadgeText => ContainsHardIncompatibility ? "Hard Conflict" : "Soft Conflict";

        public IncompatibilityResolutionOption? SelectedOption => ResolutionOptions.FirstOrDefault(o => o.IsSelected);
        public bool SelectedIsKeepAll => SelectedOption?.IsKeepAllOption ?? false;
        public bool SelectedHasRemovals => SelectedOption != null && !SelectedOption.IsKeepAllOption && SelectedOption.ModsToRemove.Count > 0;

        public IReadOnlyList<IncompatibilityRemovalSummary> SelectedRemovalSummaries
        {
            get
            {
                var opt = SelectedOption;
                if (opt == null || opt.IsKeepAllOption)
                    return Array.Empty<IncompatibilityRemovalSummary>();

                return opt.RemovalReasons
                    .Select(kvp => new IncompatibilityRemovalSummary(
                        kvp.Key.DisplayName,
                        string.Join(" · ", kvp.Value.Select(r => r.Reason).Distinct())))
                    .ToList();
            }
        }

        public event EventHandler? SelectionChanged;

        public IncompatibilityGroupViewModel(IncompatibilityGroup group)
        {
            Group = group;
            GroupName = $"IncompatibilityGroup_{group.GetHashCode()}";

            var names = group.InvolvedMods.Select(m => m.DisplayName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            GroupTitle = names.Count == 2
                ? $"{names[0]} ↔ {names[1]}"
                : names.Count > 0 ? string.Join(" · ", names) : "Incompatibility Group";

            ContainsHardIncompatibility = group.IncompatibilityRelations.Any(r =>
                r.Reason.Trim().StartsWith("[Hard]", StringComparison.OrdinalIgnoreCase));

            GenerateResolutionOptions();
        }

        internal void NotifySelectionChanged()
        {
            OnPropertyChanged(nameof(SelectedOption));
            OnPropertyChanged(nameof(SelectedIsKeepAll));
            OnPropertyChanged(nameof(SelectedHasRemovals));
            OnPropertyChanged(nameof(SelectedRemovalSummaries));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void GenerateResolutionOptions()
        {
            if (!ContainsHardIncompatibility)
            {
                var keepAllOption = new IncompatibilityResolutionOption(null, this) { IsSelected = true };
                ResolutionOptions.Add(keepAllOption);
            }

            foreach (var mod in Group.InvolvedMods.ToList())
            {
                if (!Group.IncompatibilityRelations.Any(r => r.TargetMod == mod || r.SourceMod == mod))
                    continue;

                var option = new IncompatibilityResolutionOption(mod, this);
                foreach (var relation in Group.IncompatibilityRelations.Where(r => r.TargetMod == mod))
                    option.AddRelation(relation);

                if (option.ModsToRemove.Count > 0)
                    ResolutionOptions.Add(option);
            }

            if (ContainsHardIncompatibility && ResolutionOptions.Count > 0)
            {
                ResolutionOptions[0].IsSelected = true;
            }
        }

        public ModItem? GetSelectedModToKeep() => SelectedOption?.ModToKeep;
        public List<ModItem> GetSelectedModsToRemove() => SelectedOption?.ModsToRemove ?? new List<ModItem>();
    }
}