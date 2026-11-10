#nullable enable
namespace RimSharp.Services.Workshop
{
    /// <summary>
    /// The possible outcomes of comparing a Workshop item's externally-reported
    /// content time (Steam WebAPI time_updated) against the locally-recorded
    /// internal time (max of ACF manifest time and timestamp.txt-derived date).
    /// Based on the researched community approach for workshop update decisions.
    /// </summary>
    public enum WorkshopUpdateDecision
    {
        /// <summary>Internal time is at least the external time — nothing to fetch.</summary>
        UpToDate,

        /// <summary>External time is strictly newer than the internal time — content changed on Workshop.</summary>
        UpdateAvailable,

        /// <summary>No local record exists (internal == 0) but the item exists remotely — a first
        /// sync/download is possible. Callers decide whether to act (a fresh download vs. a skip).</summary>
        InitialSyncPossible,

        /// <summary>The remote detail returned no usable timestamp (external == 0) — fetch the
        /// item details first before any comparison can be made.</summary>
        NeedsFirstFetch
    }

    /// <summary>
    /// Pure, side-effect-free update decision function — the single place in the code
    /// base where "does this workshop item have newer content?" is answered. No
    /// clock-watching, no tolerance logic, no IO: callers apply any grace windows
    /// they want to the timestamps they pass in.
    /// </summary>
    public static class WorkshopUpdateDecider
    {
        public const string Rationale =
            "external = Steam WebAPI time_updated; internal = max(ACF manifest, timestamp.txt)";

        public static WorkshopUpdateDecision Decide(long external, long internalTime)
        {
            // Order matters: the more specific situations first.
            if (external <= 0)
            {
                return WorkshopUpdateDecision.NeedsFirstFetch;
            }

            if (internalTime == 0)
            {
                return WorkshopUpdateDecision.InitialSyncPossible;
            }

            if (external > internalTime)
            {
                return WorkshopUpdateDecision.UpdateAvailable;
            }

            return WorkshopUpdateDecision.UpToDate;
        }
    }
}
