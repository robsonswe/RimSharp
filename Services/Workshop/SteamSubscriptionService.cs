using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Models;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Services.Workshop
{
    /// <summary>
    /// Service for managing Steam workshop subscriptions.
    /// Based on RimSort's workshop management approach.
    /// </summary>
    public class SteamSubscriptionService : ISteamSubscriptionService
    {
        private readonly GameInstance _gameInstance;
        private readonly ILoggerService _logger;
        private readonly SteamWorkshopApiClient _apiClient;

        public SteamSubscriptionService(GameInstance gameInstance, ILoggerService logger, SteamWorkshopApiClient apiClient)
        {
            _gameInstance = gameInstance ?? throw new ArgumentNullException(nameof(gameInstance));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        }

        /// <summary>
        /// Subscribe to a workshop mod.
        /// </summary>
        public async Task<bool> SubscribeAsync(string steamId, IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !long.TryParse(steamId, out _))
            {
                _logger.LogError($"Invalid Steam ID provided: '{steamId}'", nameof(SteamSubscriptionService));
                return false;
            }

            if (!_gameInstance.IsSteamInstallation || string.IsNullOrEmpty(_gameInstance.WorkshopFolder))
            {
                _logger.LogError("Steam installation not configured or workshop folder not available", nameof(SteamSubscriptionService));
                return false;
            }

            try
            {
                progress?.Report((1, 3, $"Subscribing to mod {steamId}..."));

                // Check if already subscribed
                if (await IsSubscribedAsync(steamId))
                {
                    _logger.LogInfo($"Mod {steamId} is already subscribed", nameof(SteamSubscriptionService));
                    return true;
                }

                progress?.Report((2, 3, $"Starting Steam subscription for mod {steamId}..."));

                // Use steam:// protocol to trigger Steam subscription
                var steamUrl = $"steam://rungameid/294100//subscribe/{steamId}";
                var startInfo = new ProcessStartInfo
                {
                    FileName = steamUrl,
                    UseShellExecute = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    // Wait a bit for Steam to start processing
                    await Task.Delay(5000, cancellationToken);
                }

                progress?.Report((3, 3, "Waiting for Steam to download mod..."));

                // Wait for the mod to be downloaded (with timeout)
                var downloaded = await WaitForModDownloadAsync(steamId, cancellationToken);
                
                if (downloaded)
                {
                    _logger.LogInfo($"Successfully subscribed to mod {steamId}", nameof(SteamSubscriptionService));
                    return true;
                }
                else
                {
                    _logger.LogError($"Failed to subscribe to mod {steamId} - download timed out", nameof(SteamSubscriptionService));
                    return false;
                }
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
            if (string.IsNullOrWhiteSpace(steamId) || !long.TryParse(steamId, out _))
            {
                _logger.LogError($"Invalid Steam ID provided: '{steamId}'", nameof(SteamSubscriptionService));
                return false;
            }

            if (!_gameInstance.IsSteamInstallation || string.IsNullOrEmpty(_gameInstance.WorkshopFolder))
            {
                _logger.LogError("Steam installation not configured or workshop folder not available", nameof(SteamSubscriptionService));
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

                progress?.Report((2, 3, $"Starting Steam unsubscription for mod {steamId}..."));

                // Use steam:// protocol to trigger Steam unsubscription
                var steamUrl = $"steam://rungameid/294100//unsubscribe/{steamId}";
                var startInfo = new ProcessStartInfo
                {
                    FileName = steamUrl,
                    UseShellExecute = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    // Wait a bit for Steam to start processing
                    await Task.Delay(3000, cancellationToken);
                }

                progress?.Report((3, 3, "Waiting for Steam to remove mod..."));

                // Wait for the mod to be removed (with timeout)
                var removed = await WaitForModRemovalAsync(steamId, cancellationToken);
                
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
            catch (Exception ex)
            {
                _logger.LogError($"Error unsubscribing from mod {steamId}: {ex.Message}", nameof(SteamSubscriptionService));
                return false;
            }
        }

        /// <summary>
        /// Get subscription status for a mod.
        /// </summary>
        public async Task<bool> IsSubscribedAsync(string steamId)
        {
            if (string.IsNullOrEmpty(steamId) || string.IsNullOrEmpty(_gameInstance.WorkshopFolder))
            {
                return false;
            }

            try
            {
                var modPath = Path.Combine(_gameInstance.WorkshopFolder, steamId);
                return Directory.Exists(modPath);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking subscription status for mod {steamId}: {ex.Message}", nameof(SteamSubscriptionService));
                return false;
            }
        }

        /// <summary>
        /// Get all subscribed workshop mods.
        /// </summary>
        public async Task<List<string>> GetSubscribedModsAsync()
        {
            if (string.IsNullOrEmpty(_gameInstance.WorkshopFolder))
            {
                return new List<string>();
            }

            try
            {
                if (!Directory.Exists(_gameInstance.WorkshopFolder))
                {
                    return new List<string>();
                }

                var directories = Directory.GetDirectories(_gameInstance.WorkshopFolder);
                var subscribedMods = new List<string>();

                foreach (var dir in directories)
                {
                    var dirName = Path.GetFileName(dir);
                    if (long.TryParse(dirName, out _))
                    {
                        subscribedMods.Add(dirName);
                    }
                }

                return subscribedMods;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting subscribed mods: {ex.Message}", nameof(SteamSubscriptionService));
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

            // Validate each subscription
            var validSubscriptions = 0;
            foreach (var modId in subscribedMods)
            {
                if (await IsSubscribedAsync(modId))
                {
                    validSubscriptions++;
                }
            }

            progress?.Report((2, 2, $"Validated {validSubscriptions} of {subscribedMods.Count} subscriptions"));
            return validSubscriptions;
        }

        /// <summary>
        /// Wait for a mod to be downloaded.
        /// </summary>
        private async Task<bool> WaitForModDownloadAsync(string steamId, CancellationToken cancellationToken)
        {
            var timeout = TimeSpan.FromMinutes(10); // 10 minute timeout
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await IsSubscribedAsync(steamId))
                {
                    return true;
                }

                await Task.Delay(1000, cancellationToken); // Check every second
            }

            return false;
        }

        /// <summary>
        /// Wait for a mod to be removed.
        /// </summary>
        private async Task<bool> WaitForModRemovalAsync(string steamId, CancellationToken cancellationToken)
        {
            var timeout = TimeSpan.FromMinutes(5); // 5 minute timeout
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!await IsSubscribedAsync(steamId))
                {
                    return true;
                }

                await Task.Delay(1000, cancellationToken); // Check every second
            }

            return false;
        }
    }
}