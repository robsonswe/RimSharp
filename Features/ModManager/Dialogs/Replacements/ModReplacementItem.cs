using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace RimSharp.Features.ModManager.Dialogs.Replacements
{
    public class ModReplacementItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public ModItem OriginalMod { get; }
        public ModReplacementInfo ReplacementInfo { get; }
        public bool ReplacementAlreadyInstalled { get; set; }

        public ModReplacementItem(ModItem originalMod, ModReplacementInfo replacementInfo)
        {
            OriginalMod = originalMod ?? throw new ArgumentNullException(nameof(originalMod));
            ReplacementInfo = replacementInfo ?? throw new ArgumentNullException(nameof(replacementInfo));
        }
        public ModItem? InstalledReplacement { get; set; }
        public string? SteamTitle { get; set; }
        public string? SteamAuthor { get; set; }

        public string ReplacementDisplayName
        {
            get
            {
                // Mirror ModItem.DisplayName: Steam name first, local name fallback.
                if (!string.IsNullOrWhiteSpace(SteamTitle)) return SteamTitle;
                var installedName = InstalledReplacement?.DisplayName;
                if (!string.IsNullOrWhiteSpace(installedName)) return installedName;
                return ReplacementInfo?.ReplacementName ?? string.Empty;
            }
        }

        public string ReplacementAuthorDisplay
        {
            get
            {
                // Steam persona name (creator) first, then local fallbacks.
                if (!string.IsNullOrWhiteSpace(SteamAuthor)) return SteamAuthor;
                var installedAuthors = InstalledReplacement?.Authors;
                if (!string.IsNullOrWhiteSpace(installedAuthors)) return installedAuthors;
                return ReplacementInfo?.ReplacementAuthor ?? string.Empty;
            }
        }

        public long OriginalLastUpdate { get; set; }
        public long ReplacementLastUpdate { get; set; }
        public string FormattedOriginalLastUpdate => 
            OriginalLastUpdate > 0 
            ? DateTimeOffset.FromUnixTimeSeconds(OriginalLastUpdate).ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
            : "N/A";
            
        public string FormattedReplacementLastUpdate => 
            ReplacementLastUpdate > 0
            ? DateTimeOffset.FromUnixTimeSeconds(ReplacementLastUpdate).ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
            : "N/A";

        public List<VersionSupport> ReplacementSupportedVersions
        {
            get
            {
                if (ReplacementInfo == null || string.IsNullOrWhiteSpace(ReplacementInfo.ReplacementVersions))
                {
                    return new List<VersionSupport>();
                }
                var source = GetEquivalentVersionSource(ReplacementInfo.Source);
                var sourceName = GetExternalSourceName(ReplacementInfo.Source);
                return ReplacementInfo.ReplacementVersionList
                    .Select(v => new VersionSupport(v, source, unofficial: true, externalSourceName: sourceName))
                    .ToList();
            }
        }

        private VersionSource GetEquivalentVersionSource(ReplacementSource replacementSource)
        {
            // All replacement-version sources are community/external data.
            return VersionSource.External;
        }

        /// <summary>
        /// Display name of the external source behind a replacement's version
        /// data, carried on each <see cref="VersionSupport"/> entry so tooltips
        /// can say which source contributed it. Null when unknown (the converter
        /// falls back to generic wording).
        /// </summary>
        private static string? GetExternalSourceName(ReplacementSource replacementSource) => replacementSource switch
        {
            ReplacementSource.Database => "RimSharpDB",
            ReplacementSource.UseThisInstead => "the installed 'Use This Instead' mod",
            ReplacementSource.UseThisInsteadRepo => "'Use This Instead' (GitHub snapshot)",
            _ => null
        };

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public event EventHandler? SelectionChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

