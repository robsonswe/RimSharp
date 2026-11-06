namespace RimSharp.Shared.Models
{
    /// <summary>
    /// Result of structurally validating a user's "I am on Steam" claim for a given
    /// game directory (handoff §2.3.1 trust ladder, rung 2). Validation never proves
    /// provenance 100% — it verifies that the claimed folder is consistent with a
    /// real Steam-managed RimWorld installation (layout + Steam bookkeeping files).
    /// </summary>
    public sealed class SteamClaimValidationResult
    {
        /// <summary>True when the claimed directory passed every structural check.</summary>
        public bool IsValid { get; private init; }

        /// <summary>
        /// Human-readable explanation of the first failed check; empty when valid.
        /// Consumed by the settings dialog to explain a refused Steam confirmation.
        /// </summary>
        public string FailureReason { get; private init; } = string.Empty;

        /// <summary>
        /// Resolved workshop folder (&lt;steamapps&gt;\workshop\content\294100) when that
        /// directory exists on disk; null otherwise. Null on a valid claim simply means
        /// "no workshop content downloaded yet" — the caller persists auto-detect
        /// semantics (empty steam_workshop_folder) in that case.
        /// </summary>
        public string? WorkshopFolder { get; private init; }

        public static SteamClaimValidationResult Success(string? workshopFolder) => new()
        {
            IsValid = true,
            WorkshopFolder = workshopFolder
        };

        public static SteamClaimValidationResult Fail(string reason) => new()
        {
            IsValid = false,
            FailureReason = reason
        };
    }
}
