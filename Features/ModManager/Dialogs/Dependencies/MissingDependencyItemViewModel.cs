using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Models; 
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace RimSharp.Features.ModManager.Dialogs.Dependencies
{
    public class MissingDependencyItemViewModel : ViewModelBase
    {
        public string DisplayName { get; }
        public string PackageId { get; }
        public string? SteamId { get; }
        public string SteamWorkshopUrl { get; }
        public List<string> RequiredByDisplay { get; }

        public bool IsSelectable => !string.IsNullOrEmpty(SteamId);

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (IsSelectable && SetProperty(ref _isSelected, value))
                {
                }
            }
        }

        public MissingDependencyItemViewModel(
            string? displayName,
            string packageId,
            string? steamWorkshopUrl,
            List<string>? requiredBy)
        {
            DisplayName = displayName ?? packageId ?? "Unknown Dependency";
            PackageId = packageId ?? string.Empty;
            SteamWorkshopUrl = steamWorkshopUrl ?? string.Empty;
            RequiredByDisplay = requiredBy ?? new List<string>();

            SteamId = ExtractSteamIdFromUrl(SteamWorkshopUrl);

            _isSelected = IsSelectable;
        }

        private static readonly System.Text.RegularExpressions.Regex SteamIdRegex =
            new System.Text.RegularExpressions.Regex(@"id=(\d+)",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static string? ExtractSteamIdFromUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var match = SteamIdRegex.Match(url);
            return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value : null;
        }
    }
}
