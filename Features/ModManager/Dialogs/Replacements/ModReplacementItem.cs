using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization; // Add for CultureInfo
using System.Linq;
using System.Runtime.CompilerServices;

namespace RimSharp.Features.ModManager.Dialogs.Replacements
{
    public class ModReplacementItem : INotifyPropertyChanged
    {
        public ModItem OriginalMod { get; }
        public ModReplacementInfo ReplacementInfo { get; }
        public bool ReplacementAlreadyInstalled { get; set; }

        public ModReplacementItem(ModItem originalMod, ModReplacementInfo replacementInfo)
        {
            OriginalMod = originalMod ?? throw new ArgumentNullException(nameof(originalMod));
            ReplacementInfo = replacementInfo ?? throw new ArgumentNullException(nameof(replacementInfo));
        }

        // --- NEW PROPERTIES TO STORE UPDATE TIMESTAMPS ---
        public long OriginalLastUpdate { get; set; }
        public long ReplacementLastUpdate { get; set; }

        // --- NEW FORMATTED PROPERTIES FOR THE VIEW ---
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
                return ReplacementInfo.ReplacementVersionList
                    .Select(v => new VersionSupport(v, source, unofficial: true))
                    .ToList();
            }
        }

        private VersionSource GetEquivalentVersionSource(ReplacementSource replacementSource)
        {
            return replacementSource switch
            {
                ReplacementSource.Database => VersionSource.Database,
                ReplacementSource.UseThisInstead => VersionSource.Mlie,
                _ => VersionSource.Database
            };
        }

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

                public event EventHandler? SelectionChanged;
                public event PropertyChangedEventHandler? PropertyChanged;
        
                protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                }
            }
        }
        