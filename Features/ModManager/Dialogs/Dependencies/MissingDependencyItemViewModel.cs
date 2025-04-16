using RimSharp.MyApp.AppFiles;
using RimSharp.Shared.Models; // Assuming ModDependency is here
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace RimSharp.Features.ModManager.Dialogs.Dependencies
{
    public class MissingDependencyItemViewModel : ViewModelBase // Inherit from ViewModelBase for INPC
    {
        public string DisplayName { get; }
        public string PackageId { get; }
        public string SteamId { get; } // Store the extracted ID
        public string SteamWorkshopUrl { get; }
        public List<string> RequiredByDisplay { get; } // Simple list for display

        public bool IsSelectable => !string.IsNullOrEmpty(SteamId); // Can only select if it has a Steam ID

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                // Only allow setting if it's selectable
                if (IsSelectable && SetProperty(ref _isSelected, value))
                {
                    // Optionally raise an event if the parent VM needs immediate notification
                    // SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        // Optional event
        // public event EventHandler SelectionChanged;

        // Constructor takes the raw data from ModListManager
        public MissingDependencyItemViewModel(
            string displayName,
            string packageId,
            string steamWorkshopUrl,
            List<string> requiredBy) // Pass simple list of names
        {
            DisplayName = displayName ?? packageId ?? "Unknown Dependency";
            PackageId = packageId;
            SteamWorkshopUrl = steamWorkshopUrl;
            RequiredByDisplay = requiredBy ?? new List<string>();

            // Extract Steam ID using the logic from ModDependency (or re-implement safely)
            SteamId = ExtractSteamIdFromUrl(steamWorkshopUrl);

            // Default selection state (only if selectable)
            _isSelected = IsSelectable;
        }

        // Helper to extract Steam ID (same as in ModDependency)
        private static readonly System.Text.RegularExpressions.Regex SteamIdRegex =
            new System.Text.RegularExpressions.Regex(@"id=(\d+)",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static string ExtractSteamIdFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var match = SteamIdRegex.Match(url);
            return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value : null;
        }
    }
}
