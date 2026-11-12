#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RimSharp.Features.Saves.ViewModels
{
    /// <summary>
    /// How one mod of the saves dialog relates to the mods that are active now.
    /// The words are the ones the diff already uses: added, removed, uninstalled,
    /// reordered.
    /// </summary>
    public enum SaveModState
    {
        /// <summary>The mod list is not known - nothing can be said about the mod.</summary>
        Unknown,

        /// <summary>In the save and active now.</summary>
        Matches,

        /// <summary>Active now, but not in the save.</summary>
        Added,

        /// <summary>In the save and installed, but not active now.</summary>
        Removed,

        /// <summary>In the save, but not installed any more.</summary>
        Uninstalled,

        /// <summary>In the save and active now, but in a different position of the load order.</summary>
        Reordered
    }

    /// <summary>
    /// One line of the mod list of a save: the name, the package id and how the
    /// mod relates to the active mods. The core game and the official DLCs are
    /// entries like any other - adding or removing a DLC changes what the game
    /// warns about just like a mod does.
    /// </summary>
    public sealed class SaveModItemViewModel
    {
        public SaveModItemViewModel(string name, string packageId, string? workshopId, SaveModState state,
            bool showsStatus = true)
        {
            PackageId = packageId ?? string.Empty;
            Name = string.IsNullOrWhiteSpace(name) ? PackageId : name.Trim();
            State = state;
            ShowsStatus = showsStatus;
            ToolTipText = BuildToolTip(workshopId);
        }

        /// <summary>The display name - the name the save recorded, or the package id when it recorded none.</summary>
        public string Name { get; }

        /// <summary>The package id, as the save records it (or as the diff reports it).</summary>
        public string PackageId { get; }

        public SaveModState State { get; }

        /// <summary>
        /// Whether the status tag belongs on this row. The save's own list shows
        /// its mods bare - what they differ from is only said once the viewer
        /// asks for the differences.
        /// </summary>
        public bool ShowsStatus { get; }

        /// <summary>True when the mod differs from the active mods. A matching mod carries no tag.</summary>
        public bool HasStatus => ShowsStatus
            && State is SaveModState.Added or SaveModState.Removed or SaveModState.Uninstalled
                or SaveModState.Reordered;

        /// <summary>Same as <see cref="HasStatus"/> - the rows the differences view marks.</summary>
        public bool IsDifference => HasStatus;

        public string StatusText => State switch
        {
            SaveModState.Added => "Added",
            SaveModState.Removed => "Removed",
            SaveModState.Uninstalled => "Uninstalled",
            SaveModState.Reordered => "Reordered",
            _ => string.Empty
        };

        /// <summary>The palette key of the status dot. Added is blue, the palette's only info hue.</summary>
        public string StatusBrushKey => State switch
        {
            SaveModState.Added => "BrushStateGit",
            SaveModState.Removed => "BrushStateWarning",
            SaveModState.Uninstalled => "BrushStateDanger",
            SaveModState.Reordered => "BrushStateHighlight",
            _ => "BrushStateNeutral"
        };

        /// <summary>The full name and id, what the status means, and the Workshop id the save recorded.</summary>
        public string ToolTipText { get; }

        private string BuildToolTip(string? workshopId)
        {
            var lines = new List<string>();

            if (!string.Equals(Name, PackageId, StringComparison.Ordinal))
            {
                lines.Add(Name);
            }

            lines.Add(PackageId);

            switch (State)
            {
                case SaveModState.Added:
                    lines.Add("Active now, but not in the save.");
                    break;
                case SaveModState.Removed:
                    lines.Add("In the save, but not active now.");
                    break;
                case SaveModState.Uninstalled:
                    lines.Add("In the save, but not installed.");
                    break;
                case SaveModState.Reordered:
                    lines.Add("In the save and active now, but the mods around it are not the same as in the save.");
                    break;
            }

            // "0" means the save knows no Workshop entry for the mod.
            if (!string.IsNullOrWhiteSpace(workshopId)
                && long.TryParse(workshopId, NumberStyles.None, CultureInfo.InvariantCulture, out long id)
                && id != 0)
            {
                lines.Add($"Workshop ID recorded in the save: {id.ToString(CultureInfo.InvariantCulture)}");
            }

            return string.Join("\n", lines);
        }
    }
}
