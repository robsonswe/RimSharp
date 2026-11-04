using RimSharp.AppDir.AppFiles;

namespace RimSharp.Features.ModManager.Dialogs.Filter
{
    /// <summary>
    /// One game-version row in the filter dialog. Each version carries a
    /// tri-state selection (radio group): Any (no filtering), Include
    /// (mod must support it), Exclude (mod must not support it).
    /// </summary>
    public class VersionFilterItemViewModel : ViewModelBase
    {
        public enum VersionMode
        {
            Any,
            Include,
            Exclude
        }

        public string Version => Item;
        public string Item { get; }
        public string DisplayName { get; }

        private VersionMode _mode;

        /// <summary>
        /// Command parameter fragment for this row ("&lt;version&gt;").
        /// Exposed because GroupName on the radio grid needs the version.
        /// </summary>
        public VersionFilterItemViewModel(string version, VersionMode mode = VersionMode.Any)
        {
            Item = version;
            DisplayName = version;
            _mode = mode;
        }

        public VersionMode Mode
        {
            get => _mode;
            set => SetProperty(ref _mode, value);
        }

        /// <summary>True when the row should be initially checked as Include.</summary>
        public bool IsInclude => _mode == VersionMode.Include;

        /// <summary>True when the row should be initially checked as Exclude.</summary>
        public bool IsExclude => _mode == VersionMode.Exclude;

        /// <summary>True when the row has no active selection (Any).</summary>
        public bool IsAny => _mode == VersionMode.Any;

        // Composed command parameters so each radio carries "<version>|<mode>".
        public string ParamAny => $"{Item}|Any";
        public string ParamInclude => $"{Item}|Include";
        public string ParamExclude => $"{Item}|Exclude";
    }
}
