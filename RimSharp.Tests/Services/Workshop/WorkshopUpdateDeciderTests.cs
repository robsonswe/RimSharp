using FluentAssertions;
using RimSharp.Services.Workshop;
using Xunit;

namespace RimSharp.Tests.Services.Workshop
{
    /// <summary>
    /// The pure decision function is the single source of truth for "does this
    /// item have newer content?" — the matrix here is the contract every
    /// consumer must agree with.
    /// </summary>
    public class WorkshopUpdateDeciderTests
    {
        [Fact]
        public void Decide_ExternalStrictlyNewer_IsUpdateAvailable()
        {
            WorkshopUpdateDecider.Decide(external: 1700000061, internalTime: 1700000000)
                .Should().Be(WorkshopUpdateDecision.UpdateAvailable);
        }

        [Fact]
        public void Decide_EqualTimestamps_IsUpToDate()
        {
            WorkshopUpdateDecider.Decide(external: 1700000000, internalTime: 1700000000)
                .Should().Be(WorkshopUpdateDecision.UpToDate);
        }

        [Fact]
        public void Decide_InternalNewerThanExternal_IsUpToDate()
        {
            // Rollback protection: a locally-known record never regresses
            WorkshopUpdateDecider.Decide(external: 1700000000, internalTime: 1700000061)
                .Should().Be(WorkshopUpdateDecision.UpToDate);
        }

        [Fact]
        public void Decide_NoInternalDataButItemExists_IsInitialSyncPossible()
        {
            WorkshopUpdateDecider.Decide(external: 1700000000, internalTime: 0)
                .Should().Be(WorkshopUpdateDecision.InitialSyncPossible);
        }

        [Fact]
        public void Decide_NoExternalData_IsNeedsFirstFetch()
        {
            WorkshopUpdateDecider.Decide(external: 0, internalTime: 1700000000)
                .Should().Be(WorkshopUpdateDecision.NeedsFirstFetch);
        }

        [Fact]
        public void Decide_BothZero_IsNeedsFirstFetch()
        {
            WorkshopUpdateDecider.Decide(external: 0, internalTime: 0)
                .Should().Be(WorkshopUpdateDecision.NeedsFirstFetch);
        }
    }
}
