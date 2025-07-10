using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace RimSharp.Features.ModManager.Dialogs.Replacements
{
    public class ModReplacementItem : INotifyPropertyChanged
    {
        public ModItem OriginalMod { get; set; }
        public ModReplacementInfo ReplacementInfo { get; set; }
        public bool ReplacementAlreadyInstalled { get; set; }

        // --- NEW CALCULATED PROPERTY FOR THE VIEW ---
        /// <summary>
        /// Converts the replacement's simple version string into a List of VersionSupport
        /// objects that can be used by the UI's VersionSupportConverter.
        /// </summary>
        public List<VersionSupport> ReplacementSupportedVersions
        {
            get
            {
                if (ReplacementInfo == null || string.IsNullOrWhiteSpace(ReplacementInfo.ReplacementVersions))
                {
                    return new List<VersionSupport>();
                }

                // Determine the source for these versions based on where the rule came from.
                var source = GetEquivalentVersionSource(ReplacementInfo.Source);

                // Since these versions are from a replacement rule and not the mod's own About.xml,
                // they are inherently "unofficial" overrides.
                return ReplacementInfo.ReplacementVersionList
                    .Select(v => new VersionSupport(v, source, unofficial: true))
                    .ToList();
            }
        }

        /// <summary>
        /// Maps the ReplacementSource enum to the corresponding VersionSource enum.
        /// </summary>
        private VersionSource GetEquivalentVersionSource(ReplacementSource replacementSource)
        {
            return replacementSource switch
            {
                ReplacementSource.Database => VersionSource.Database,
                // 'Use This Instead' provides community data via local XML, analogous to the 'Mlie' source.
                ReplacementSource.UseThisInstead => VersionSource.Mlie,
                _ => VersionSource.Database // Default fallback
            };
        }
        // --- END OF NEW PROPERTY AND HELPER ---

        private bool _isSelected = true;
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

        public event EventHandler SelectionChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}