using RimSharp.MyApp.AppFiles; // For ViewModelBase or similar base for INPC
using RimSharp.Shared.Models;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RimSharp.Features.ModManager.Dialogs.Replacements
{
    // Inherit from a base class that implements INotifyPropertyChanged if you have one,
    // otherwise implement it directly.
    public class ModReplacementItem : INotifyPropertyChanged
    {
        public ModItem OriginalMod { get; set; } // Assuming ModItem doesn't change
        public ModReplacementInfo ReplacementInfo { get; set; } // Assuming this doesn't change
        
        // New property to track if replacement is already installed
        public bool ReplacementAlreadyInstalled { get; set; }

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
                    // Optional: Notify the parent VM to update the count
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        // Event to notify parent VM (optional, alternative to recalculating)
        public event EventHandler SelectionChanged;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}