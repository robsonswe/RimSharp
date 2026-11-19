#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using RimSharp.AppDir.AppFiles;

namespace RimSharp.Features.ModManager.Dialogs.MissingMods
{
    public class MissingModGroupViewModel : ViewModelBase
    {
        private string _packageId = string.Empty;
        private ObservableCollection<MissingModVariantViewModel> _variants = new();
        private MissingModVariantViewModel? _selectedVariant;
        private bool _isSyncingSelection;

        public string PackageId
        {
            get => _packageId;
            set
            {
                if (SetProperty(ref _packageId, value))
                {
                    OnPropertyChanged(nameof(GroupName));
                    UpdateAllVariantGroupNames();
                }
            }
        }

        public ObservableCollection<MissingModVariantViewModel> Variants
        {
            get => _variants;
            set
            {
                var old = _variants;
                if (SetProperty(ref _variants, value))
                {
                    UnhookVariantsCollection(old);
                    HookVariantsCollection(_variants);
                }
            }
        }

        public MissingModVariantViewModel? SelectedVariant
        {
            get => _selectedVariant;
            set
            {
                if (SetProperty(ref _selectedVariant, value))
                {
                    if (!_isSyncingSelection)
                    {
                        _isSyncingSelection = true;
                        foreach (var v in Variants)
                        {
                            v.IsSelected = (v == value);
                        }
                        _isSyncingSelection = false;
                    }
                    OnPropertyChanged(nameof(IsSelectionMade));
                }
            }
        }

        public bool IsSelectionMade => SelectedVariant != null;

        /// <summary>
        /// Whether this group has at least one published (selectable) variant.
        /// </summary>
        public bool HasSelectableVariants => Variants.Any(v => v.IsPublished);

        /// <summary>
        /// Display label for variant count, e.g. "2 variants" or "1 variant".
        /// </summary>
        public string VariantCountLabel
        {
            get
            {
                var count = Variants.Count;
                return count == 1 ? "1 variant" : $"{count} variants";
            }
        }

        /// <summary>
        /// Optional list of mod names that require this missing mod. Populated
        /// by the dependency-resolution flow; empty for the import flow.
        /// </summary>
        public IReadOnlyList<string> RequiredBy { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Whether <see cref="RequiredBy"/> has any entries.
        /// </summary>
        public bool HasRequiredBy => RequiredBy.Count > 0;

        /// <summary>
        /// Comma-joined display of <see cref="RequiredBy"/>, e.g. "Mod A, Mod B".
        /// Empty when <see cref="HasRequiredBy"/> is false.
        /// </summary>
        public string RequiredByDisplay => string.Join(", ", RequiredBy);

        /// <summary>
        /// Unique group name for RadioButtons in this group.
        /// </summary>
        public string GroupName => $"MissingModGroup_{PackageId}";

        public MissingModGroupViewModel(string packageId)
        {
            PackageId = packageId;
            HookVariantsCollection(_variants);
        }

        private void HookVariantsCollection(ObservableCollection<MissingModVariantViewModel>? collection)
        {
            if (collection == null) return;

            collection.CollectionChanged += OnVariantsCollectionChanged;
            foreach (var variant in collection)
            {
                RegisterVariant(variant);
            }
        }

        private void UnhookVariantsCollection(ObservableCollection<MissingModVariantViewModel>? collection)
        {
            if (collection == null) return;

            collection.CollectionChanged -= OnVariantsCollectionChanged;
            foreach (var variant in collection)
            {
                UnregisterVariant(variant);
            }
        }

        private void OnVariantsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (MissingModVariantViewModel variant in e.NewItems)
                {
                    RegisterVariant(variant);
                }
            }
            if (e.OldItems != null)
            {
                foreach (MissingModVariantViewModel variant in e.OldItems)
                {
                    UnregisterVariant(variant);
                }
            }

            OnPropertyChanged(nameof(VariantCountLabel));
            OnPropertyChanged(nameof(HasSelectableVariants));
        }

        private void RegisterVariant(MissingModVariantViewModel variant)
        {
            variant.GroupName = GroupName;
            variant.PropertyChanged += Variant_PropertyChanged;
        }

        private void UnregisterVariant(MissingModVariantViewModel variant)
        {
            variant.PropertyChanged -= Variant_PropertyChanged;
        }

        private void UpdateAllVariantGroupNames()
        {
            foreach (var variant in Variants)
            {
                variant.GroupName = GroupName;
            }
        }

        private void Variant_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_isSyncingSelection) return;

            if (e.PropertyName == nameof(MissingModVariantViewModel.IsSelected) && sender is MissingModVariantViewModel v)
            {
                if (v.IsSelected)
                {
                    _isSyncingSelection = true;
                    // Deselect every other variant in this group to enforce single selection
                    foreach (var variant in Variants)
                    {
                        if (!ReferenceEquals(variant, v) && variant.IsSelected)
                        {
                            variant.IsSelected = false;
                        }
                    }
                    _isSyncingSelection = false;

                    SelectedVariant = v;
                }
                else if (SelectedVariant == v)
                {
                    SelectedVariant = null;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                UnhookVariantsCollection(_variants);
            }
            base.Dispose(disposing);
        }
    }
}
