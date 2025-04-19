using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Models;
using System; // Required for DateTime
using System.Globalization; // Required for CultureInfo, DateTimeStyles
using System.Collections.Generic; // Required for EqualityComparer used in base SetProperty
using System.Diagnostics; // For Debug.WriteLine

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
        // <<< NEW PROPERTY NAME CONSTANT >>>
        public const string LocalUpdateDateTimePropertyName = nameof(LocalUpdateDateTime);
        public const string IsSelectedPropertyName = nameof(IsSelected);

        private bool _isSelected;
        public ModItem Mod { get; } // Keep the original ModItem for reference

        public string Name => Mod?.Name ?? "Unknown Mod";
        public string PackageId => Mod?.PackageId ?? "N/A";
        public string SteamId => Mod?.SteamId ?? "N/A";
        public string LocalUpdateDate => Mod?.UpdateDate ?? "N/A"; // Display the raw date string

        // <<< NEW PROPERTY FOR SORTING >>>
        /// <summary>
        /// Gets the parsed DateTime representation of the LocalUpdateDate.
        /// Used for sorting. Returns DateTime.MinValue if parsing fails or date is null/empty.
        /// </summary>
        public DateTime LocalUpdateDateTime { get; }

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

            // Parse the date string for sorting
            if (!string.IsNullOrWhiteSpace(mod?.UpdateDate) &&
                DateTime.TryParseExact(mod.UpdateDate, "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
            {
                LocalUpdateDateTime = parsedDate;
            }
            else
            {
                // Assign a default value if parsing fails or date is missing,
                // ensuring items without a valid date sort consistently (e.g., at the beginning/end).
                LocalUpdateDateTime = DateTime.MinValue;
                 if (!string.IsNullOrWhiteSpace(mod?.UpdateDate)) {
                     // Log parsing failure only if there was a date string to parse
                     Debug.WriteLine($"[UpdateCheckItemVM] Failed to parse date '{mod.UpdateDate}' for mod '{mod.PackageId ?? "N/A"}'. Using MinValue for sorting.");
                 }
            }
        }
    }
}
