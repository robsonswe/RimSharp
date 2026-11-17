#nullable enable
using System;
using System.Collections.Generic;

namespace RimSharp.Shared.Models
{
    /// <summary>
    /// Which rule source claimed each entry of one packageId's merged
    /// <see cref="ModRule"/>. Built during the same priority-ordered merge
    /// pass that produces the effective rule set (first source carrying an
    /// entry claims it), so every presented rule row can be labelled with its
    /// origin: the source's display name for database-layer contributions,
    /// "About.xml" for mod-declared ones (<see cref="ModItem.AboutLoadBefore"/>
    /// etc.) and "Custom" for user edits.
    /// Provenance is tracked per TARGET (not per packageId) for the list and
    /// map fields, mirroring the per-target merge policies.
    /// </summary>
    public class RuleProvenance
    {
        /// <summary>
        /// target packageId → source Id that claimed the <c>loadBefore</c>
        /// entry (first source carrying the target wins).
        /// </summary>
        public Dictionary<string, string> LoadBeforeSource { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// target packageId → source Id that claimed the <c>loadAfter</c>
        /// entry (first source carrying the target wins).
        /// </summary>
        public Dictionary<string, string> LoadAfterSource { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// target packageId → source Id that claimed the
        /// <c>incompatibleWith</c> entry (first-source-wins per target).
        /// </summary>
        public Dictionary<string, string> IncompatibleSource { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Source Id that first provided the <c>loadBottom</c> flag
        /// (null when no source provides it; the flag itself merges by OR).
        /// </summary>
        public string? LoadBottomSource { get; set; }

        /// <summary>
        /// Source Id that first provided the <c>loadTop</c> flag
        /// (null when no source provides it; the flag itself merges by OR).
        /// </summary>
        public string? LoadTopSource { get; set; }
    }
}
