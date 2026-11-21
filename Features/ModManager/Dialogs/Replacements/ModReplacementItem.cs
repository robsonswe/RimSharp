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

        /// <summary>
        /// Package ID resolved at display time from a fallback source (installed mod
        /// by SteamId, then the ModInfo dictionary by SteamId) when the shared
        /// <see cref="ModReplacementInfo.ReplacementModId"/> is empty. Kept on the row
        /// rather than mutating the cached replacement info.
        /// </summary>
        public string? ResolvedReplacementPackageId { get; set; }

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

        /// <summary>
        /// Human-readable name of the source that supplied this replacement rule.
        /// Local-file sources use the registry record's display name carried on
        /// <see cref="ModReplacementInfo.SourceName"/>.
        /// </summary>
        public string SourceDisplayName => GetSourceDisplayName(ReplacementInfo) ?? string.Empty;

        /// <summary>Curated reason text, shown on a row only when present.</summary>
        public string? ReasonDisplay => ReplacementInfo?.Reason;

        /// <summary>Whether a non-empty reason is available for display.</summary>
        public bool HasReason => !string.IsNullOrWhiteSpace(ReplacementInfo?.Reason);

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
                var sourceName = GetSourceDisplayName(ReplacementInfo);
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
        /// Display name of the source behind a replacement rule (and its version
        /// data). Local-file sources use the registry record's display name;
        /// null when unknown so the version-tooltip converter falls back to
        /// generic wording.
        /// </summary>
        private static string? GetSourceDisplayName(ModReplacementInfo? info)
        {
            if (info == null) return null;
            if (info.Source == ReplacementSource.LocalFile || info.Source == ReplacementSource.IndexedRecords)
            {
                return string.IsNullOrWhiteSpace(info.SourceName)
                    ? (info.Source == ReplacementSource.LocalFile ? "local replacement file" : "external replacement index")
                    : info.SourceName;
            }
            return info.Source switch
            {
                ReplacementSource.Database => "RimSharpDB",
                ReplacementSource.UseThisInstead => "the installed 'Use This Instead' mod",
                ReplacementSource.UseThisInsteadRepo => "'Use This Instead' (GitHub snapshot)",
                _ => null
            };
        }

        // --- Read-only display helpers for the unified replacement card --------

        public IReadOnlyList<string> OriginalVersions =>
            OriginalMod.SupportedVersionStrings
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        public bool HasOriginalVersions => OriginalVersions.Count > 0;

        private IReadOnlyList<string>? _fetchedReplacementVersions;

        /// <summary>
        /// Versions fetched from Steam for the replacement (target) mod. Steam is
        /// the authoritative source for the replacement side only; the original
        /// (replaced) mod deliberately keeps its local versions in
        /// <see cref="OriginalVersions"/>. Normally set on the row in the view
        /// model's initializer (before binding), but raising change notifications
        /// keeps later updates safe too.
        /// </summary>
        public IReadOnlyList<string>? FetchedReplacementVersions
        {
            get => _fetchedReplacementVersions;
            set
            {
                if (!ReferenceEquals(_fetchedReplacementVersions, value))
                {
                    _fetchedReplacementVersions = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ReplacementVersions));
                    OnPropertyChanged(nameof(HasReplacementVersions));
                    OnPropertyChanged(nameof(HasFetchedReplacementVersions));
                }
            }
        }

        public bool HasFetchedReplacementVersions =>
            FetchedReplacementVersions != null && FetchedReplacementVersions.Count > 0;

        private IReadOnlyList<string>? _modInfoReplacementVersions;

        /// <summary>
        /// Versions for the replacement (target) mod resolved from the ModInfo
        /// database by its SteamId. Used only when Steam and the local installed
        /// mod cannot supply versions. Kept on the row rather than mutating the
        /// shared <see cref="ModReplacementInfo"/> cache.
        /// </summary>
        public IReadOnlyList<string>? ModInfoReplacementVersions
        {
            get => _modInfoReplacementVersions;
            set
            {
                if (!ReferenceEquals(_modInfoReplacementVersions, value))
                {
                    _modInfoReplacementVersions = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ReplacementVersions));
                    OnPropertyChanged(nameof(HasReplacementVersions));
                }
            }
        }

        private static IReadOnlyList<string> NormalizeVersions(IEnumerable<string>? versions) =>
            versions == null
                ? Array.Empty<string>()
                : versions.Where(v => !string.IsNullOrWhiteSpace(v))
                          .Distinct(StringComparer.OrdinalIgnoreCase)
                          .ToList();

        public IReadOnlyList<string> ReplacementVersions
        {
            get
            {
                // 1. Steam-fetched versions for the replacement (authoritative).
                var fetched = NormalizeVersions(FetchedReplacementVersions);
                if (fetched.Count > 0)
                    return fetched;

                // 2. Versions from the locally installed replacement mod.
                if (InstalledReplacement != null)
                {
                    var installed = NormalizeVersions(InstalledReplacement.SupportedVersionStrings);
                    if (installed.Count > 0)
                        return installed;
                }

                // 3. Versions from the ModInfo database.
                var modInfo = NormalizeVersions(ModInfoReplacementVersions);
                if (modInfo.Count > 0)
                    return modInfo;

                // 4. Curated rule list as the final fallback so no data is lost.
                return NormalizeVersions(ReplacementInfo?.ReplacementVersionList);
            }
        }

        public bool HasReplacementVersions => ReplacementVersions.Count > 0;

        public string OriginalPackageIdDisplay =>
            string.IsNullOrWhiteSpace(OriginalMod.PackageId) ? "(No Package ID)" : OriginalMod.PackageId;

        public string ReplacementPackageIdDisplay
        {
            get
            {
                var explicitId = ReplacementInfo?.ReplacementModId;
                if (!string.IsNullOrWhiteSpace(explicitId)) return explicitId!.Trim();
                if (!string.IsNullOrWhiteSpace(ResolvedReplacementPackageId)) return ResolvedReplacementPackageId!.Trim();
                return "(No Package ID)";
            }
        }

        public bool HasOriginalSteamId => !string.IsNullOrWhiteSpace(OriginalMod.SteamId);
        public bool HasReplacementSteamId => !string.IsNullOrWhiteSpace(ReplacementInfo?.ReplacementSteamId);

        public bool IsIndexedOrDbSource =>
            ReplacementInfo?.Source == ReplacementSource.IndexedRecords ||
            ReplacementInfo?.Source == ReplacementSource.Database;

        public string TargetBadgeText =>
            ReplacementAlreadyInstalled ? "Already Installed" : "Replacement";

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

