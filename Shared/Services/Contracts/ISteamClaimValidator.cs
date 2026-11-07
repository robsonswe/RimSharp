using System;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Interface for validating a Steam RimWorld installation claim.
    /// Stage 0: Structural validation (filesystem layout, manifests).
    /// Stage 2: Can be augmented with Steamworks ownership check (BIsSubscribedApp).
    /// </summary>
    public interface ISteamClaimValidator
    {
        /// <summary>
        /// Validates that the given game path is a legitimate Steam RimWorld installation.
        /// </summary>
        /// <param name="gamePath">The game directory to validate</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Validation result with success/failure and optional resolved workshop folder</returns>
        Task<SteamClaimValidationResult> ValidateAsync(string? gamePath, CancellationToken cancellationToken = default);
    }
}