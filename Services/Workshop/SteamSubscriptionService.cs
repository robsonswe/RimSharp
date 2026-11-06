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
using Steamworks;

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
        private readonly object _initLock = new();
        private bool _steamworksInitialized = false;
        private bool _initializationAttempted = false;
        private CancellationTokenSource? _callbackCts;
        private Task? _callbackTask;

        public SteamSubscriptionService(GameInstance gameInstance, ILoggerService logger, SteamWorkshopApiClient apiClient)
        {
            _gameInstance = gameInstance ?? throw new ArgumentNullException(nameof(gameInstance));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        }

        /// <summary>
        /// Initialize Steamworks API. Must be called before any subscription operations.
        /// </summary>
        private bool EnsureSteamworksInitialized()
        {
            lock (_initLock)
            {
                if (_steamworksInitialized)
                    return true;

                if (_initializationAttempted)
                {
                    _logger.LogWarning("Steamworks initialization was previously attempted and failed", nameof(SteamSubscriptionService));
                    return false;
                }

                _initializationAttempted = true;

                try
                {
                    // Steamworks.NET requires steam_appid.txt (294100) next to the executable
                    var result = SteamAPI.Init();
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
                    SteamAPI.RunCallbacks();
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

            if (_steamworksInitialized)
            {
                try
                {
                    SteamAPI.Shutdown();
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
                var call = SteamUGC.SubscribeItem(new PublishedFileId_t(pfid));
                var result = await WaitForApiCallAsync<RemoteStorageSubscribePublishedFileResult_t>(call, cancellationToken);
                
                if (result?.m_eResult != EResult.k_EResultOK)
                {
                    var error = result?.m_eResult.ToString() ?? "No result";
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
                var call = SteamUGC.UnsubscribeItem(new PublishedFileId_t(pfid));
                var result = await WaitForApiCallAsync<RemoteStorageUnsubscribePublishedFileResult_t>(call, cancellationToken);
                
                if (result?.m_eResult != EResult.k_EResultOK)
                {
                    var error = result?.m_eResult.ToString() ?? "No result";
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
                        var call = SteamUGC.UnsubscribeItem(new PublishedFileId_t(pfid));
                        await WaitForApiCallAsync<RemoteStorageUnsubscribePublishedFileResult_t>(call, cancellationToken);
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
                        var call = SteamUGC.SubscribeItem(new PublishedFileId_t(pfid));
                        await WaitForApiCallAsync<RemoteStorageSubscribePublishedFileResult_t>(call, cancellationToken);
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
                        SteamUGC.DownloadItem(new PublishedFileId_t(pfid), true); // highPriority = true
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
                            var state = SteamUGC.GetItemState(new PublishedFileId_t(pfid));
                            if ((state & (uint)EItemState.k_EItemStateInstalled) == 0 || (state & (uint)EItemState.k_EItemStateNeedsUpdate) != 0)
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
                var subscribedCount = SteamUGC.GetNumSubscribedItems();
                var subscribedItems = new PublishedFileId_t[subscribedCount];
                SteamUGC.GetSubscribedItems(subscribedItems, (uint)subscribedCount);
                
                foreach (var item in subscribedItems)
                {
                    if (item.m_PublishedFileId == pfid)
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
                var subscribedCount = SteamUGC.GetNumSubscribedItems();
                var subscribedItems = new PublishedFileId_t[subscribedCount];
                SteamUGC.GetSubscribedItems(subscribedItems, (uint)subscribedCount);
                
                var result = new List<string>();
                foreach (var item in subscribedItems)
                {
                    result.Add(item.m_PublishedFileId.ToString());
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
                if (SteamUGC.GetItemDownloadInfo(new PublishedFileId_t(pfid), out ulong bytesDownloaded, out ulong bytesTotal))
                {
                    var state = SteamUGC.GetItemState(new PublishedFileId_t(pfid));
                    var isDownloading = (state & (uint)EItemState.k_EItemStateDownloading) != 0;
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
                SteamUGC.DownloadItem(new PublishedFileId_t(pfid), true);

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
        /// Wait for a Steam API call to complete and return the result.
        /// </summary>
        private async Task<T?> WaitForApiCallAsync<T>(SteamAPICall_t call, CancellationToken cancellationToken) where T : struct
        {
            var tcs = new TaskCompletionSource<T?>();
            
            // Register callback for the API call using Steamworks.NET CallResult pattern
            var callback = CallResult<T>.Create((result, ioFailure) =>
            {
                if (ioFailure)
                {
                    tcs.TrySetResult(default);
                }
                else
                {
                    tcs.TrySetResult(result);
                }
            });
            
            callback.Set(call);
            
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout for API call
                
                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token));
                
                if (completedTask == tcs.Task)
                {
                    return await tcs.Task;
                }
                else
                {
                    return default;
                }
            }
            finally
            {
                callback.Dispose();
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
                    var state = SteamUGC.GetItemState(new PublishedFileId_t(pfid));
                    
                    // Check if installed
                    if ((state & (uint)EItemState.k_EItemStateInstalled) != 0)
                    {
                        // Also ensure it's not still downloading or needs update
                        if ((state & (uint)EItemState.k_EItemStateDownloading) == 0 && (state & (uint)EItemState.k_EItemStateNeedsUpdate) == 0)
                        {
                            return true;
                        }
                    }

                    // Report progress if available
                    if (SteamUGC.GetItemDownloadInfo(new PublishedFileId_t(pfid), out ulong bytesDownloaded, out ulong bytesTotal))
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
                var ownsApp = SteamApps.BIsSubscribedApp(new AppId_t(294100));
                
                if (ownsApp)
                {
                    _logger.LogDebug("Ownership check passed: Account owns RimWorld (294100)", nameof(SteamSubscriptionService));
                    
                    // Family-sharing diagnostics (logged for triage, never gates)
                    var ownerId = SteamApps.GetAppOwner();
                    var currentUser = SteamUser.GetSteamID();
                    var isFamilyShared = SteamApps.BIsSubscribedFromFamilySharing();
                    
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

            try
            {
                // Use the steam://validate protocol to trigger Steam's file verification
                // This is a valid Steam protocol verb (unlike fake subscribe/update)
                var verifyUrl = "steam://validate/294100";
                
                _logger.LogInfo($"Launching Steam file verification for RimWorld (294100)", nameof(SteamSubscriptionService));
                
                var psi = new ProcessStartInfo
                {
                    FileName = verifyUrl,
                    UseShellExecute = true
                };
                
                var process = Process.Start(psi);
                if (process != null)
                {
                    _logger.LogInfo("Steam verify game files launched successfully", nameof(SteamSubscriptionService));
                    return true;
                }
                else
                {
                    _logger.LogError("Failed to launch Steam verify game files", nameof(SteamSubscriptionService));
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error launching Steam verify game files: {ex.Message}", nameof(SteamSubscriptionService));
                return false;
            }
        }
    }
}