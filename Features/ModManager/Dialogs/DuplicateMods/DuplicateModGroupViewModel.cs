using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using RimSharp.AppDir.AppFiles;
using RimSharp.Infrastructure.Mods.Validation.Duplicates;
using RimSharp.Shared.Models;

namespace RimSharp.Features.ModManager.Dialogs.DuplicateMods
{
    public class DuplicateModGroupViewModel : ViewModelBase
    {
        public ObservableCollection<ModItemWrapper> Mods { get; } = new();
        public string PackageId { get; }
        public string GroupName { get; }
        public string CopiesBadgeText => $"{Mods.Count} copies";

        private bool _isIgnored;
        public bool IsIgnored
        {
            get => _isIgnored;
            set
            {
                if (SetProperty(ref _isIgnored, value))
                {
                    if (value)
                    {
                        foreach (var mod in Mods)
                            mod.IsActive = false;
                    }
                    else if (Mods.All(m => !m.IsActive))
                    {
                        var fallback = Mods.FirstOrDefault(m => m.IsOriginal) ?? Mods.FirstOrDefault();
                        if (fallback != null) fallback.IsActive = true;
                    }

                    OnPropertyChanged(nameof(IgnoreButtonText));
                    NotifyOutcomesChanged();
                }
            }
        }

        public string IgnoreButtonText => IsIgnored ? "✓ Ignoring (Keep all)" : "Ignore · Keep all";

        public int DeletionsCount =>
            (!IsIgnored && Mods.Any(m => m.IsActive))
                ? Mods.Count(m => !m.IsActive)
                : 0;

        public ICommand ToggleIgnoreCommand { get; }

        public event EventHandler? OutcomesChanged;

        public DuplicateModGroupViewModel(IGrouping<string, ModItem> group)
        {
            PackageId = group.Key;
            GroupName = $"DuplicateGroup_{group.GetHashCode()}";
            _isIgnored = false;
            ToggleIgnoreCommand = CreateCommand(() => IsIgnored = !IsIgnored);

            foreach (var mod in group)
            {
                if (mod != null)
                    Mods.Add(new ModItemWrapper(mod, this));
            }

            bool allHaveSteamId = Mods.All(w => !string.IsNullOrEmpty(w.SteamId) && long.TryParse(w.SteamId, out _));
            if (allHaveSteamId)
            {
                var originalModWrapper = Mods.OrderBy(w => long.Parse(w.SteamId)).FirstOrDefault();
                if (originalModWrapper != null)
                    originalModWrapper.IsOriginal = true;
            }

            var defaultMod = group.OrderByDescending(m => m?.IsActive ?? false)
                  .ThenByDescending(m => m?.SupportedVersions?.FirstOrDefault()?.Version)
                  .FirstOrDefault();

            if (defaultMod != null)
            {
                var wrapper = Mods.FirstOrDefault(w => w?.Original == defaultMod);
                if (wrapper != null)
                    wrapper.IsActive = true;
            }
        }

        internal void NotifyOutcomesChanged()
        {
            foreach (var m in Mods)
                m.NotifyOutcomeChanged();

            OnPropertyChanged(nameof(DeletionsCount));
            OutcomesChanged?.Invoke(this, EventArgs.Empty);
        }

        public ModItem? GetSelectedModToKeep() => Mods.FirstOrDefault(m => m?.IsActive == true)?.Original;

        public void UpdateSelection(ModItemWrapper selected)
        {
            if (_isIgnored)
            {
                _isIgnored = false;
                OnPropertyChanged(nameof(IsIgnored));
                OnPropertyChanged(nameof(IgnoreButtonText));
            }

            foreach (var mod in Mods)
            {
                if (mod != null && mod != selected)
                    mod.IsActive = false;
            }
        }
    }
}