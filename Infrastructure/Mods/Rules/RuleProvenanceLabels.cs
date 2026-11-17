#nullable enable
using RimSharp.Shared.Models;

namespace RimSharp.Infrastructure.Mods.Rules
{
    /// <summary>
    /// Resolves recorded rule-provenance source Ids (see
    /// <see cref="RuleProvenance"/> and <see cref="ModItem.DatabaseLoadBefore"/>)
    /// into the display labels the UI shows as a rule row's origin. The
    /// designer lane labels each row as "About.xml", "Custom" or the source's
    /// display name; this helper covers the last case with safe fallbacks so
    /// an id no longer present in the registry never produces an empty label.
    /// </summary>
    public static class RuleProvenanceLabels
    {
        /// <summary>Fallback label when no source id was recorded at all.</summary>
        public const string UnknownLabel = "Unknown";

        /// <summary>
        /// Display name of a rule source for a recorded source Id.
        /// <list type="bullet">
        /// <item>Known id (case-insensitive) → the registry record's
        /// <see cref="ModRuleSourceRecord.Name"/>, falling back to its Id when
        /// the name is blank.</item>
        /// <item>Unknown id (removed source, legacy bare repository, ...)
        /// → the id itself, so the label is always present.</item>
        /// <item>Null/blank id or null registry → <see cref="UnknownLabel"/>.</item>
        /// </list>
        /// </summary>
        public static string GetSourceDisplayName(string? sourceId, ModRuleSourceRegistry? registry)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                return UnknownLabel;
            }

            var record = registry?.Find(sourceId);
            if (record == null)
            {
                return sourceId; // unknown id → the id itself
            }

            return string.IsNullOrWhiteSpace(record.Name) ? record.Id : record.Name;
        }
    }
}
