using System;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Services.Workshop;
using Steamworks;

namespace RimSharp.Services.Workshop
{
    /// <summary>
    /// Steamworks-backed claim validator that combines structural validation
    /// with Steamworks ownership check (BIsSubscribedApp).
    /// </summary>
    public class SteamClaimValidator : ISteamClaimValidator
    {
        private readonly GameInstallationDetectionService _structuralValidator;
        private readonly ILoggerService _logger;
        private readonly GameInstance _gameInstance;

        public SteamClaimValidator(
            GameInstallationDetectionService structuralValidator,
            ILoggerService logger,
            GameInstance gameInstance)
        {
            _structuralValidator = structuralValidator ?? throw new ArgumentNullException(nameof(structuralValidator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gameInstance = gameInstance ?? throw new ArgumentNullException(nameof(gameInstance));
        }

        public async Task<SteamClaimValidationResult> ValidateAsync(string? gamePath, CancellationToken cancellationToken = default)
        {
            // Stage 1: Structural validation (filesystem layout, manifests)
            var structuralResult = _structuralValidator.ValidateSteamClaim(gamePath);
            
            if (!structuralResult.IsValid)
            {
                _logger.LogDebug($"Structural validation failed: {structuralResult.FailureReason}", nameof(SteamClaimValidator));
                return structuralResult;
            }

            _logger.LogDebug("Structural validation passed, proceeding to Steamworks ownership check", nameof(SteamClaimValidator));

            // Stage 2: Steamworks ownership gate
            try
            {
                // Ensure Steamworks is initialized
                if (!SteamAPI.Init())
                {
                    _logger.LogWarning("SteamAPI.Init() failed - cannot perform Steamworks ownership check. Falling back to structural validation only.", nameof(SteamClaimValidator));
                    // Don't fail the claim - structural validation passed, Steamworks unavailable = unknown, not not-owned
                    return structuralResult;
                }

                // Check if the current user owns RimWorld (AppID 294100)
                var ownsApp = SteamApps.BIsSubscribedApp(new AppId_t(294100));

                if (!ownsApp)
                {
                    _logger.LogWarning("Steamworks ownership check failed: Account does not own RimWorld (294100)", nameof(SteamClaimValidator));
                    return SteamClaimValidationResult.Fail(
                        "Your Steam account does not own RimWorld (AppID 294100). " +
                        "Steam Workshop features require ownership of the game on Steam.");
                }

                // Family-sharing diagnostics (logged for triage, never gates)
                var ownerId = SteamApps.GetAppOwner();
                var currentUser = SteamUser.GetSteamID();
                var isFamilyShared = SteamApps.BIsSubscribedFromFamilySharing();

                if (ownerId != currentUser)
                {
                    _logger.LogInfo($"Family sharing detected: Owner={ownerId}, CurrentUser={currentUser}, IsFamilyShared={isFamilyShared}", nameof(SteamClaimValidator));
                }

                _logger.LogDebug("Steamworks ownership check passed", nameof(SteamClaimValidator));

                // Shutdown Steamworks (we only needed it for the check)
                SteamAPI.Shutdown();

                return structuralResult; // Success - return the structural result with resolved workshop folder
            }
            catch (Exception ex)
            {
                _logger.LogError($"Steamworks ownership check error: {ex.Message}", nameof(SteamClaimValidator));
                // On error, fall back to structural validation only (unknown, not not-owned)
                try { SteamAPI.Shutdown(); } catch { }
                return structuralResult;
            }
        }
    }
}