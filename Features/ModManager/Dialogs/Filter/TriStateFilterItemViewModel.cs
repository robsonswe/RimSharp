using RimSharp.AppDir.AppFiles;

namespace RimSharp.Features.ModManager.Dialogs.Filter
{
    /// <summary>
    /// One filterable row (game version, tag, ...) carrying a tri-state
    /// selection (radio group): Any (no filtering), Include, Exclude.
    /// Used by both the Versions and Tags tabs of the filter dialog.
    /// </summary>
    public class TriStateFilterItemViewModel : ViewModelBase
    {
        public enum FilterMode
        {
            Any,
            Include,
            Exclude
        }

        public string Item { get; }
        public string DisplayName { get; }

        /// <summary>"Version" or "Tag" — composed into command parameters so the
        /// dialog VM can route the selection to the right collection.</summary>
        public string Kind { get; }

        private FilterMode _mode;

        public TriStateFilterItemViewModel(string item, FilterMode mode = FilterMode.Any, string kind = "Version")
        {
            Item = item;
            DisplayName = item;
            Kind = kind;
            _mode = mode;
        }

        public FilterMode Mode
        {
            get => _mode;
            set => SetProperty(ref _mode, value);
        }

        /// <summary>True when the row should be initially checked as Include.</summary>
        public bool IsInclude => _mode == FilterMode.Include;

        /// <summary>True when the row should be initially checked as Exclude.</summary>
        public bool IsExclude => _mode == FilterMode.Exclude;

        /// <summary>True when the row has no active selection (Any).</summary>
        public bool IsAny => _mode == FilterMode.Any;

        // Composed command parameters "<Kind>|<value>|<mode>" (Kind routes
        // the dialog VM to the Versions or Tags collection).
        public string ParamAny => $"{Kind}|{Item}|Any";
        public string ParamInclude => $"{Kind}|{Item}|Include";
        public string ParamExclude => $"{Kind}|{Item}|Exclude";
    }
}
