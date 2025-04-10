using RimSharp.MyApp.AppFiles;
using RimSharp.Shared.Models;
using System.Collections.Generic; // Required for EqualityComparer used in base SetProperty

namespace RimSharp.Features.WorkshopDownloader.Dialogs.UpdateCheck
{
    // ViewModel for each item in the Update Check Dialog ListBox
    public class UpdateCheckItemViewModel : ViewModelBase
    {
        // Constants for property names (good practice for binding/sorting parameters)
        public const string NamePropertyName = nameof(Name);
        public const string PackageIdPropertyName = nameof(PackageId);
        public const string SteamIdPropertyName = nameof(SteamId);
        public const string LocalUpdateDatePropertyName = nameof(LocalUpdateDate);
        public const string IsSelectedPropertyName = nameof(IsSelected); // Corrected name

        private bool _isSelected;
        public ModItem Mod { get; } // Keep the original ModItem for reference

        public string Name => Mod?.Name ?? "Unknown Mod";
        public string PackageId => Mod?.PackageId ?? "N/A";
        public string SteamId => Mod?.SteamId ?? "N/A";
        public string LocalUpdateDate => Mod?.UpdateDate ?? "N/A"; // Display the raw date string

        public bool IsSelected
        {
            get => _isSelected;
            // Call the BASE SetProperty, explicitly providing the property name constant
            set => SetProperty(ref _isSelected, value, IsSelectedPropertyName);
        }

        public UpdateCheckItemViewModel(ModItem mod)
        {
            Mod = mod;
            IsSelected = false; // Default to not selected
        }
    }
}