#nullable enable
using ReactiveUI;
using RimSharp.AppDir.AppFiles;

namespace RimSharp.Features.Troubleshooting.ViewModels
{
    /// <summary>
    /// One problem card in the "Problems found" surface (State B).
    /// Ordered error → warning → info, most frequent first.
    /// </summary>
    public sealed class ProblemCardViewModel : ReactiveObject
    {
        /// <summary>Human-readable problem title.</summary>
        public string Title { get; }

        /// <summary>"Error", "Warning", or "Info".</summary>
        public string Severity { get; }

        /// <summary>Theme brush key for the 4px accent border and severity label.</summary>
        public string SeverityBrushKey { get; }

        /// <summary>Occurrence count; shown as ×N when &gt; 1.</summary>
        public int Count { get; }

        public string CountText => Count > 1 ? $"×{Count}" : string.Empty;

        /// <summary>"First seen at line N" when log-derived, null otherwise.</summary>
        public string? FirstLineReference { get; }

        /// <summary>"What it means" one-liner.</summary>
        public string WhatItMeans { get; }

        /// <summary>Guidance text from the log pattern (honesty-table items only).</summary>
        public string? Suggestion { get; }

        /// <summary>True when a catalog action exists to fix this problem.</summary>
        public bool HasFix { get; }

        /// <summary>Catalog action id this problem maps to, or null when no fix exists yet.</summary>
        public string? SuggestedActionId { get; }

        /// <summary>Display title of the suggested action.</summary>
        public string ActionTitle { get; }

        /// <summary>"Fix: &lt;action&gt;" when a fix exists, "No fix available yet" otherwise.</summary>
        public string PrimaryActionLabel =>
            HasFix ? $"Fix: {ActionTitle}" : "No fix available yet";

        /// <summary>True when the finding originated from a log signature scan.</summary>
        public bool IsLogDerived { get; }

        public ProblemCardViewModel(
            string title,
            string severity,
            string severityBrushKey,
            int count,
            string? firstLineReference,
            string whatItMeans,
            string? suggestion,
            bool hasFix,
            string? suggestedActionId,
            string actionTitle,
            bool isLogDerived)
        {
            Title = title;
            Severity = severity;
            SeverityBrushKey = severityBrushKey;
            Count = count;
            FirstLineReference = firstLineReference;
            WhatItMeans = whatItMeans;
            Suggestion = suggestion;
            HasFix = hasFix;
            SuggestedActionId = suggestedActionId;
            ActionTitle = actionTitle;
            IsLogDerived = isLogDerived;
        }
    }
}
