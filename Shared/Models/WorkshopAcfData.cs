using System.Collections.Generic;

namespace RimSharp.Shared.Models
{
    /// <summary>
    /// One workshop item extracted from an appworkshop_294100.acf file
    /// (AppWorkshop\WorkshopItemDetails entry), keyed by publishedfileid.
    /// </summary>
    public sealed class WorkshopAcfItem
    {
        /// <summary>timeupdated from WorkshopItemDetails — the Steam client's last content update (unix seconds).</summary>
        public long TimeUpdated { get; init; }

        /// <summary>timetouched from WorkshopItemDetails (unix seconds).</summary>
        public long TimeTouched { get; init; }

        /// <summary>sourceid from WorkshopItemDetails (the revision source stamp; mirrors the installed timestamp).</summary>
        public long SourceId { get; init; }

        /// <summary>
        /// Best-known local content timestamp for this item (unix seconds): TimeUpdated,
        /// falling back to the WorkshopItemsInstalled record when details are absent or
        /// zero. This is what Phase 1.2 stores on ModItem.AcfTimeUpdated.
        /// </summary>
        public long ResolvedTimeUpdated { get; init; }

        /// <summary>
        /// Raw string values of the WorkshopItemDetails entry (unescaped), keyed by field
        /// name — kept for diagnostics and future fields the parser does not surface yet.
        /// Null for items that only have a WorkshopItemsInstalled record.
        /// </summary>
        public IReadOnlyDictionary<string, string>? RawValues { get; init; }
    }

    /// <summary>
    /// Parsed contents of an appworkshop_294100.acf file. One parser serves both mod
    /// kinds — the Steam client layout (&lt;steamapps&gt;\workshop\...) and the SteamCMD
    /// layout (&lt;steamcmd-prefix&gt;\steam\steamapps\workshop\...).
    /// </summary>
    public sealed class WorkshopAcfData
    {
        /// <summary>Workshop items keyed by publishedfileid (as a string, per the ACF object key).</summary>
        public IReadOnlyDictionary<string, WorkshopAcfItem> Items { get; init; } =
            new Dictionary<string, WorkshopAcfItem>();

        public static WorkshopAcfData Empty { get; } = new();
    }
}
