#nullable enable
using System.Collections.ObjectModel;
using RimSharp.AppDir.AppFiles;

namespace RimSharp.Features.ModManager.Dialogs.MissingMods
{
    public class MissingModGroupViewModel : ViewModelBase
    {
        private string _packageId = string.Empty;
        private ObservableCollection<MissingModVariantViewModel> _variants = new();
        private MissingModVariantViewModel? _selectedVariant; // Nullable

        public string PackageId
        {
            get => _packageId;
            set => SetProperty(ref _packageId, value);
        }

        public ObservableCollection<MissingModVariantViewModel> Variants
        {
            get => _variants;
            set => SetProperty(ref _variants, value);
        }

        // This property will be bound to the RadioButton GroupName and allows tracking the selection
        public MissingModVariantViewModel? SelectedVariant
        {
            get => _selectedVariant;
            set => SetProperty(ref _selectedVariant, value);
        }

        // Helper to know if a selection has been made for this group
        public bool IsSelectionMade => SelectedVariant != null;

        public MissingModGroupViewModel(string packageId)
        {
            PackageId = packageId;
        }
    }
}
