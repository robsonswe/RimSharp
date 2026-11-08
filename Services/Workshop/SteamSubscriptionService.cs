using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Models;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Services.Workshop
{
    /// <summary>
    /// Service for managing Steam workshop subscriptions using Steamworks.NET.
    /// Based on RimSort's SteamworksSubscriptionHandler pattern.
    /// </summary>
    public class SteamSubscriptionService : ISteamSubscriptionService, IDisposable
    {
        private readonly GameInstance _gameInstance;
        private readonly ILoggerService _logger;
        private readonly SteamWorkshopApiClient _apiClient;
        private readonly Func<bool> _steamApiInit;
        private ISteamworksInterop? _interop;
        private readonly object _initLock = new();
        private bool _steamworksInitialized = false;
        private DateTime _lastInitAttemptUtc = DateTime.MinValue;
        private CancellationTokenSource? _callbackCts;
        private Task? _callbackTask;

        /// <summary>
        /// Cooldown between SteamAPI.Init attempts after a failure. A failed init is
        /// the "unknown ownership" state (design ladder): interactive ops stay off and
        /// the user gets a clear message, but starting Steam later and retrying the
        /// action must work — a one-shot latch would brick the whole session on a
        /// client-startup race.
        /// </summary>
        private static readonly TimeSpan InitRetryCooldown = TimeSpan.FromSeconds(30);

        private readonly TimeSpan _initRetryCooldown;

        /// <summary>
        /// Test seam: override the Steamworks runtime support check.
        /// When null (default), uses the real OS/platform check.
        /// </summary>
        internal static Func<bool>? TestSteamworksSupportedOverride { get; set; }

        /// <summary>Set to "1" to enable (experimental) Steamworks integration on macOS.</summary>
        internal const string MacOsOptInEnvVar = "RIMSHARP_STEAMWORKS_MACOS";

        /// <summary>
        /// Cached result of the Steamworks runtime support check.
        /// False on macOS (especially arm64 where Steamworks.NET lacks native assets),
        /// true on Windows/Linux.
        /// </summary>
        private readonly bool _isSteamworksRuntimeSupported;

        /// <summary>
        /// Lazily created Steamworks adapter. Only reached on runtimes where
        /// <see cref="_isSteamworksRuntimeSupported"/> is true. This service never references a
        /// Steamworks.NET type itself, so it (and its async state machines) load and run on
        /// runtimes where the Steamworks.NET assembly is absent.
        /// </summary>
        private ISteamworksInterop Interop => _interop ??= SteamworksInteropFactory.Create();

        public SteamSubscriptionService(GameInstance gameInstance, ILoggerService logger, SteamWorkshopApiClient apiClient, Func<bool>? steamApiInit = null, TimeSpan? initRetryCooldown = null, ISteamworksInterop? interop = null)
        {
            _gameInstance = gameInstance ?? throw new ArgumentNullException(nameof(gameInstance));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _interop = interop;
            _steamApiInit = steamApiInit ?? (() => Interop.Init());
            _initRetryCooldown = initRetryCooldown ?? InitRetryCooldown;

            // Determine once at construction whether the current runtime can support Steamworks.NET.
            // On macOS (especially arm64), Steamworks.NET 2024.8.0 lacks native assets and will
            // throw FileNotFoundException on any Steamworks type access. We gate all Steamworks
            // touch points behind this flag so the service degrades gracefully with the same
            // failure shapes used for "non-Steam installation" / "not initialized" paths.
            _isSteamworksRuntimeSupported = IsSteamworksRuntimeSupported(hasInjectedBackend: steamApiInit != null || interop != null);
        }

        /// <summary>
        /// Checks whether the current runtime can load and use Steamworks.NET.
        /// Windows and Linux return true. macOS is opt-in via RIMSHARP_STEAMWORKS_MACOS=1 until
        /// real Steam init has been verified on a Mac.
        /// </summary>
        private static bool IsSteamworksRuntimeSupported(bool hasInjectedBackend)
        {
            // A caller-supplied init/interop never touches the real Steamworks.NET assembly, so it
            // is always usable. Checked first so the static test override below cannot leak into
            // tests that inject their own backend when xunit runs test classes in parallel.
            if (hasInjectedBackend)
                return true;

            // Test seam (for unit tests driving the unsupported outcome)
            if (TestSteamworksSupportedOverride != null)
                return TestSteamworksSupportedOverride();

            // macOS uses the Steamworks.NET.AnyCPU package (see RimSharp.csproj). It builds and loads in CI,
            // but real Steam init has not been verified on an Apple Silicon machine yet, so it is opt-in.
            // To enable by default once a Mac user confirms it works, return true here instead.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return Environment.GetEnvironmentVariable(MacOsOptInEnvVar) == "1";

            // Windows and Linux are supported
            return true;
        }

        /// <summary>
        /// Initialize Steamworks API. Must be called before any subscription operations.
        /// </summary>
        private bool EnsureSteamworksInitialized()
        {
            // Fast path: if the runtime cannot support Steamworks, never attempt init.
            // This avoids FileNotFoundException on macOS arm64 where Steamworks.NET
            // lacks native assets. The failure shape matches "not initialized" paths.
            if (!_isSteamworksRuntimeSupported)
            {
                _logger.LogWarning("Steamworks not supported on this runtime (macOS; set RIMSHARP_STEAMWORKS_MACOS=1 to try experimental support); subscription features unavailable", nameof(SteamSubscriptionService));
                return false;
            }

            lock (_initLock)
            {
                if (_steamworksInitialized)
                    return true;

                if (DateTime.UtcNow - _lastInitAttemptUtc < _initRetryCooldown)
                {
                    _logger.LogWarning("Steamworks initialization failed recently; retry after a short wait", nameof(SteamSubscriptionService));
                    return false;
                }

                _lastInitAttemptUtc = DateTime.UtcNow;

                try
                {
                    // Steamworks.NET requires steam_appid.txt (294100) next to the executable
                    var result = _steamApiInit();
                    if (!result)
                    {
                        _logger.LogError("SteamAPI.Init() failed - Steam not running or not logged in", nameof(SteamSubscriptionService));
                        return false;
                    }

                    _steamworksInitialized = true;
                    _logger.LogInfo("Steamworks initialized successfully", nameof(SteamSubscriptionService));

                    // Start callback pumping on a background thread
                    _callbackCts = new CancellationTokenSource();
                    _callbackTask = Task.Run(() => RunCallbacks(_callbackCts.Token), _callbackCts.Token);

                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Steamworks initialization failed: {ex.Message}", nameof(SteamSubscriptionService));
                    return false;
                }
            }
        }

        /// <summary>
        /// Pump SteamAPI.RunCallbacks() on a background thread.
        /// </summary>
        private void RunCallbacks(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    Interop.RunCallbacks();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Steamworks callback error: {ex.Message}", nameof(SteamSubscriptionService));
                }

                // Sleep briefly to avoid busy loop
                Thread.Sleep(100);
            }
        }

        /// <summary>
        /// Shutdown Steamworks API.
        /// </summary>
        public void Dispose()
        {
            try
            {
                _callbackCts?.Cancel();
                if (_callbackTask != null)
                {
                    _callbackTask.Wait(TimeSpan.FromSeconds(2));
                }
            }
            catch { }

            // Only call SteamAPI.Shutdown if we actually initialized on a supported runtime.
            // On unsupported runtimes (macOS), _steamworksInitialized is always false and
            // we never touched Steamworks types, so Shutdown would throw FileNotFoundException.
            if (_steamworksInitialized && _isSteamworksRuntimeSupported)
            {
                try
                {
                    _interop?.Shutdown();
                    _steamworksInitialized = false;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"SteamAPI.Shutdown() error: {ex.Message}", nameof(SteamSubscriptionService));
                }
            }
        }

        /// <summary>
        /// Subscribe to a workshop mod.
        /// </summary>
        public async Task<bool> SubscribeAsync(string steamId, IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !ulong.TryParse(steamId, out var pfid))
            {
                _logger.LogError($"Invalid Steam ID provided: '{steamId}'", nameof(SteamSubscriptionService));
                return false;
            }

            if (!_gameInstance.IsSteamInstallation || string.IsNullOrEmpty(_gameInstance.WorkshopFolder))
            {
                _logger.LogError("Steam installation not configured or workshop folder not available", nameof(SteamSubscriptionService));
                return false;
            }

            if (!EnsureSteamworksInitialized())
            {
                _logger.LogError("Cannot subscribe: Steamworks not initialized", nameof(SteamSubscriptionService));
                return false;
            }

            try
            {
                progress?.Report((1, 4, $"Subscribing to mod {steamId}..."));

                // Check if already subscribed using true Steam subscription state
                if (await IsSubscribedAsync(steamId))
                {
                    _logger.LogInfo($"Mod {steamId} is already subscribed", nameof(SteamSubscriptionService));
                    return true;
                }

                progress?.Report((2, 4, $"Calling Steam SubscribeItem for mod {steamId}..."));

                // Call Steamworks SubscribeItem - async, returns SteamAPICall_t
                var result = await Interop.SubscribeItemAsync(pfid, cancellationToken);
                
                if (!result.IsOk)
                {
                    var error = result.ResultName;
                    _logger.LogError($"SubscribeItem failed for {steamId}: {error}", nameof(SteamSubscriptionService));
                    return false;
                }

                progress?.Report((3, 4, "Waiting for Steam to download mod..."));

                // Wait for download completion using Steamworks state
                var downloaded = await WaitForModDownloadAsync(pfid, cancellationToken, progress);
                
                if (downloaded)
                {
                    progress?.Report((4, 4, "Subscription complete"));
                    _logger.LogInfo($"Successfully subscribed to mod {steamId}", nameof(SteamSubscriptionService));
                    return true;
                }
                else
                {
                    _logger.LogError($"Failed to subscribe to mod {steamId} - download timed out", nameof(SteamSubscriptionService));
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo($"Subscription to mod {steamId} was cancelled", nameof(SteamSubscriptionService));
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error subscribing to mod {steamId}: {ex.Message}", nameof(SteamSubscriptionService));
                return false;
            }
        }

        /// <summary>
        /// Unsubscribe from a workshop mod.
        /// </summary>
        public async Task<bool> UnsubscribeAsync(string steamId, IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !ulong.TryParse(steamId, out var pfid))
            {
                _logger.LogError($"Invalid Steam ID provided: '{steamId}'", nameof(SteamSubscriptionService));
                return false;
            }

            if (!_gameInstance.IsSteamInstallation || string.IsNullOrEmpty(_gameInstance.WorkshopFolder))
            {
                _logger.LogError("Steam installation not configured or workshop folder not available", nameof(SteamSubscriptionService));
                return false;
            }

            if (!EnsureSteamworksInitialized())
            {
                _logger.LogError("Cannot unsubscribe: Steamworks not initialized", nameof(SteamSubscriptionService));
                return false;
            }

            try
            {
                progress?.Report((1, 3, $"Unsubscribing from mod {steamId}..."));

                // Check if currently subscribed
                if (!await IsSubscribedAsync(steamId))
                {
                    _logger.LogInfo($"Mod {steamId} is not subscribed", nameof(SteamSubscriptionService));
                    return true;
                }

                progress?.Report((2, 3, $"Calling Steam UnsubscribeItem for mod {steamId}..."));

                // Call Steamworks UnsubscribeItem
                var result = await Interop.UnsubscribeItemAsync(pfid, cancellationToken);
                
                if (!result.IsOk)
                {
                    var error = result.ResultName;
                    _logger.LogError($"UnsubscribeItem failed for {steamId}: {error}", nameof(SteamSubscriptionService));
                    return false;
                }

                progress?.Report((3, 3, "Waiting for Steam to remove mod..."));

                // Wait for removal
                var removed = await WaitForModRemovalAsync(pfid, cancellationToken);
                
                if (removed)
                {
                    _logger.LogInfo($"Successfully unsubscribed from mod {steamId}", nameof(SteamSubscriptionService));
                    return true;
                }
                else
                {
                    _logger.LogError($"Failed to unsubscribe from mod {steamId} - removal timed out", nameof(SteamSubscriptionService));
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo($"Unsubscription from mod {steamId} was cancelled", nameof(SteamSubscriptionService));
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error unsubscribing from mod {steamId}: {ex.Message}", nameof(SteamSubscriptionService));
                return false;
            }
        }

        /// <summary>
        /// Resubscribe to a list of mods (RimSort pattern: unsub all → 1s → sub all → 1s → DownloadItem).
        /// </summary>
        public async Task<int> ResubscribeAsync(IEnumerable<string> steamIds, IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default)
        {
            var idList = steamIds?.Where(id => ulong.TryParse(id, out _)).ToList() ?? new List<string>();
            var successCount = 0;

            if (idList.Count == 0)
                return 0;

            if (!EnsureSteamworksInitialized())
            {
                _logger.LogError("Cannot resubscribe: Steamworks not initialized", nameof(SteamSubscriptionService));
                return 0;
            }

            try
            {
                progress?.Report((1, 4, $"Resubscribing {idList.Count} mods: unsubscribing all..."));

                // Stage 1: Unsubscribe all
                foreach (var id in idList)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ulong.TryParse(id, out var pfid))
                    {
                        await Interop.UnsubscribeItemAsync(pfid, cancellationToken);
                    }
                }

                await Task.Delay(1000, cancellationToken); // 1s wait

                progress?.Report((2, 4, $"Resubscribing {idList.Count} mods: subscribing all..."));

                // Stage 2: Subscribe all
                foreach (var id in idList)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ulong.TryParse(id, out var pfid))
                    {
                        await Interop.SubscribeItemAsync(pfid, cancellationToken);
                    }
                }

                await Task.Delay(1000, cancellationToken); // 1s wait

                progress?.Report((3, 4, $"Resubscribing {idList.Count} mods: triggering downloads..."));

                // Stage 3: Trigger high-priority download for each
                foreach (var id in idList)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ulong.TryParse(id, out var pfid))
                    {
                        Interop.DownloadItem(pfid, true); // highPriority = true
                    }
                }

                // Stage 4: Wait for all downloads (with timeout)
                progress?.Report((4, 4, "Waiting for downloads to complete..."));
                
                var timeout = TimeSpan.FromMinutes(15);
                var startTime = DateTime.UtcNow;

                while (DateTime.UtcNow - startTime < timeout)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var remaining = 0;
                    foreach (var id in idList)
                    {
                        if (ulong.TryParse(id, out var pfid))
                        {
                            var state = Interop.GetItemState(pfid);
                            if ((state & SteamItemState.Installed) == 0 || (state & SteamItemState.NeedsUpdate) != 0)
                            {
                                remaining++;
                            }
                        }
                    }

                    if (remaining == 0)
                    {
                        successCount = idList.Count;
                        break;
                    }

                    await Task.Delay(2000, cancellationToken); // Check every 2 seconds
                }

                _logger.LogInfo($"Resubscribe completed: {successCount}/{idList.Count} mods ready", nameof(SteamSubscriptionService));
                return successCount;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("Resubscribe was cancelled", nameof(SteamSubscriptionService));
                return successCount;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during resubscribe: {ex.Message}", nameof(SteamSubscriptionService));
                return successCount;
            }
        }

        /// <summary>
        /// Get subscription status for a mod using true Steam subscription state.
        /// </summary>
        public async Task<bool> IsSubscribedAsync(string steamId)
        {
            if (string.IsNullOrEmpty(steamId) || !ulong.TryParse(steamId, out var pfid))
            {
                return false;
            }

            if (!EnsureSteamworksInitialized())
            {
                // Fallback to filesystem check if Steamworks unavailable
                if (string.IsNullOrEmpty(_gameInstance.WorkshopFolder))
                    return false;

                try
                {
                    var modPath = Path.Combine(_gameInstance.WorkshopFolder ?? "", steamId);
                    return Directory.Exists(modPath);
                }
                catch
                {
                    return false;
                }
            }

            try
            {
                // Use Steamworks GetSubscribedItems to get true subscription state
                var subscribedItems = Interop.GetSubscribedItems();
                
                foreach (var item in subscribedItems)
                {
                    if (item == pfid)
                        return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking subscription status for mod {steamId}: {ex.Message}", nameof(SteamSubscriptionService));
                
                // Fallback to filesystem
                try
                {
                    var modPath = Path.Combine(_gameInstance.WorkshopFolder ?? "", steamId);
                    return Directory.Exists(modPath);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Get all subscribed workshop mods using true Steam subscription list.
        /// </summary>
        public async Task<List<string>> GetSubscribedModsAsync()
        {
            if (!EnsureSteamworksInitialized())
            {
                // Fallback to filesystem
                return await GetSubscribedModsFromFilesystemAsync();
            }

            try
            {
                var subscribedItems = Interop.GetSubscribedItems();
                
                var result = new List<string>();
                foreach (var item in subscribedItems)
                {
                    result.Add(item.ToString());
                }
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting subscribed mods: {ex.Message}", nameof(SteamSubscriptionService));
                return await GetSubscribedModsFromFilesystemAsync();
            }
        }

        private async Task<List<string>> GetSubscribedModsFromFilesystemAsync()
        {
            if (string.IsNullOrEmpty(_gameInstance.WorkshopFolder))
                return new List<string>();

            try
            {
                if (!Directory.Exists(_gameInstance.WorkshopFolder))
                    return new List<string>();

                var directories = Directory.GetDirectories(_gameInstance.WorkshopFolder);
                var subscribedMods = new List<string>();

                foreach (var dir in directories)
                {
                    var dirName = Path.GetFileName(dir);
                    if (ulong.TryParse(dirName, out _))
                    {
                        subscribedMods.Add(dirName);
                    }
                }

                return subscribedMods;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting subscribed mods from filesystem: {ex.Message}", nameof(SteamSubscriptionService));
                return new List<string>();
            }
        }

        /// <summary>
        /// Refresh subscription status from Steam.
        /// </summary>
        public async Task<int> RefreshSubscriptionsAsync(IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default)
        {
            var subscribedMods = await GetSubscribedModsAsync();
            progress?.Report((1, 2, $"Found {subscribedMods.Count} subscribed mods"));

            // Validate each subscription using Steamworks state
            var validSubscriptions = 0;
            foreach (var modId in subscribedMods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await IsSubscribedAsync(modId))
                {
                    validSubscriptions++;
                }
            }

            progress?.Report((2, 2, $"Validated {validSubscriptions} of {subscribedMods.Count} subscriptions"));
            return validSubscriptions;
        }

        /// <summary>
        /// Get download progress for a mod.
        /// </summary>
        public async Task<(bool isDownloading, ulong bytesDownloaded, ulong bytesTotal)?> GetDownloadProgressAsync(string steamId)
        {
            if (string.IsNullOrEmpty(steamId) || !ulong.TryParse(steamId, out var pfid) || !EnsureSteamworksInitialized())
                return null;

            try
            {
                if (Interop.TryGetDownloadInfo(pfid, out ulong bytesDownloaded, out ulong bytesTotal))
                {
                    var state = Interop.GetItemState(pfid);
                    var isDownloading = (state & SteamItemState.Downloading) != 0;
                    return (isDownloading, bytesDownloaded, bytesTotal);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting download progress for {steamId}: {ex.Message}", nameof(SteamSubscriptionService));
            }

            return null;
        }

        /// <summary>
        /// Update a workshop mod (trigger Steam to download latest version).
        /// </summary>
        public async Task<bool> UpdateModAsync(string steamId, IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !ulong.TryParse(steamId, out var pfid))
            {
                _logger.LogError($"Invalid Steam ID provided: '{steamId}'", nameof(SteamSubscriptionService));
                return false;
            }

            if (!_gameInstance.IsSteamInstallation || string.IsNullOrEmpty(_gameInstance.WorkshopFolder))
            {
                _logger.LogError("Steam installation not configured or workshop folder not available", nameof(SteamSubscriptionService));
                return false;
            }

            if (!EnsureSteamworksInitialized())
            {
                _logger.LogError("Cannot update: Steamworks not initialized", nameof(SteamSubscriptionService));
                return false;
            }

            try
            {
                progress?.Report((1, 3, $"Updating mod {steamId}..."));

                // Check if currently subscribed
                if (!await IsSubscribedAsync(steamId))
                {
                    _logger.LogError($"Mod {steamId} is not subscribed, cannot update", nameof(SteamSubscriptionService));
                    return false;
                }

                progress?.Report((2, 3, $"Triggering Steam download for mod {steamId}..."));

                // Trigger high-priority download for the update
                Interop.DownloadItem(pfid, true);

                progress?.Report((3, 3, "Waiting for Steam to download update..."));

                // Wait for download completion using Steamworks state
                var updated = await WaitForModDownloadAsync(pfid, cancellationToken, progress);
                
                if (updated)
                {
                    _logger.LogInfo($"Successfully updated mod {steamId}", nameof(SteamSubscriptionService));
                    return true;
                }
                else
                {
                    _logger.LogError($"Failed to update mod {steamId} - download timed out", nameof(SteamSubscriptionService));
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo($"Update for mod {steamId} was cancelled", nameof(SteamSubscriptionService));
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating mod {steamId}: {ex.Message}", nameof(SteamSubscriptionService));
                return false;
            }
        }

        /// <summary>
        /// Wait for a mod to be downloaded using Steamworks state.
        /// </summary>
        private async Task<bool> WaitForModDownloadAsync(ulong pfid, CancellationToken cancellationToken, IProgress<(int current, int total, string message)>? progress = null)
        {
            var timeout = TimeSpan.FromMinutes(15); // 15 minute timeout
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var state = Interop.GetItemState(pfid);
                    
                    // Check if installed
                    if ((state & SteamItemState.Installed) != 0)
                    {
                        // Also ensure it's not still downloading or needs update
                        if ((state & SteamItemState.Downloading) == 0 && (state & SteamItemState.NeedsUpdate) == 0)
                        {
                            return true;
                        }
                    }

                    // Report progress if available
                    if (Interop.TryGetDownloadInfo(pfid, out ulong bytesDownloaded, out ulong bytesTotal))
                    {
                        if (bytesTotal > 0)
                        {
                            var percent = (int)((double)bytesDownloaded / bytesTotal * 100);
                            progress?.Report((percent, 100, $"Downloading... {percent}%"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error checking download state for {pfid}: {ex.Message}", nameof(SteamSubscriptionService));
                }

                await Task.Delay(1000, cancellationToken);
            }

            return false;
        }

        /// <summary>
        /// Wait for a mod to be removed.
        /// </summary>
        private async Task<bool> WaitForModRemovalAsync(ulong pfid, CancellationToken cancellationToken)
        {
            var timeout = TimeSpan.FromMinutes(5);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (!await IsSubscribedAsync(pfid.ToString()))
                    {
                        return true;
                    }
                }
                catch { }

                await Task.Delay(1000, cancellationToken);
            }

            return false;
        }

        /// <summary>
        /// Check if the current Steam account owns RimWorld (AppID 294100).
        /// This is the ownership gate for all workshop operations.
        /// </summary>
        public async Task<bool> CheckOwnershipAsync(CancellationToken cancellationToken = default)
        {
            if (!EnsureSteamworksInitialized())
            {
                _logger.LogWarning("Cannot check ownership: Steamworks not initialized", nameof(SteamSubscriptionService));
                return false;
            }

            try
            {
                // Steamworks.NET: SteamApps.BIsSubscribedApp checks if the current user owns the app
                var ownership = Interop.QueryOwnership(294100);
                
                if (ownership.OwnsApp)
                {
                    _logger.LogDebug("Ownership check passed: Account owns RimWorld (294100)", nameof(SteamSubscriptionService));
                    
                    // Family-sharing diagnostics (logged for triage, never gates)
                    var ownerId = ownership.OwnerId;
                    var currentUser = ownership.CurrentUserId;
                    var isFamilyShared = ownership.IsFamilyShared;
                    
                    if (ownerId != currentUser)
                    {
                        _logger.LogInfo($"Family sharing detected: Owner={ownerId}, CurrentUser={currentUser}, IsFamilyShared={isFamilyShared}", nameof(SteamSubscriptionService));
                    }
                    
                    return true;
                }
                else
                {
                    _logger.LogWarning("Ownership check failed: Account does not own RimWorld (294100)", nameof(SteamSubscriptionService));
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ownership check error: {ex.Message}", nameof(SteamSubscriptionService));
                return false;
            }
        }

        /// <summary>
        /// Launch Steam's "Verify integrity of game files" for RimWorld (AppID 294100).
        /// Uses the steam://validate/294100 protocol handler.
        /// </summary>
        public async Task<bool> VerifyGameFilesAsync()
        {
            if (!_gameInstance.IsSteamInstallation)
            {
                _logger.LogWarning("Cannot verify game files: Not a Steam installation", nameof(SteamSubscriptionService));
                return false;
            }

            if (!_isSteamworksRuntimeSupported)
            {
                _logger.LogWarning("Steamworks not supported on this runtime (macOS; set RIMSHARP_STEAMWORKS_MACOS=1 to try experimental support); subscription features unavailable", nameof(SteamSubscriptionService));
                return false;
            }

            try
            {
                // Launch the steam://validate protocol. Linux users often have no
                // registered URL handler, so the launcher falls back to a terminal
                // emulator running the steam client (RimSort reference behavior).
                var verifyUrl = "steam://validate/294100";

                _logger.LogInfo($"Launching Steam file verification for RimWorld (294100)", nameof(SteamSubscriptionService));

                var launched = Core.Services.SteamProtocolLauncher.TryOpen(verifyUrl, _logger);
                if (launched)
                {
                    _logger.LogInfo("Steam verify game files launched successfully", nameof(SteamSubscriptionService));
                    return true;
                }

                _logger.LogError("Failed to launch Steam verify game files", nameof(SteamSubscriptionService));
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error launching Steam verify game files: {ex.Message}", nameof(SteamSubscriptionService));
                return false;
            }
        }
    }
}