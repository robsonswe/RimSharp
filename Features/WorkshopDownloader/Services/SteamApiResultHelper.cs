#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using RimSharp.Features.WorkshopDownloader.Models;

namespace RimSharp.Features.WorkshopDownloader.Services
{
    public static class SteamApiResultHelper
    {
        /// <summary>
        /// Gets a user-friendly description for a Steam API result code.
        /// </summary>
        /// <param name="resultCode">The integer result code from the Steam API.</param>
        /// <returns>A descriptive string.</returns>
        public static string GetDescription(int resultCode)
        {
            // Cast the int to the enum, handling undefined values
            SteamApiResultCode code = Enum.IsDefined(typeof(SteamApiResultCode), resultCode)
                                        ? (SteamApiResultCode)resultCode
                                        : SteamApiResultCode.Unknown;

            switch (code)
            {
                case SteamApiResultCode.OK: return "Success."; // Should not typically be requested for errors
                case SteamApiResultCode.Fail: return "Generic failure reported by Steam.";
                case SteamApiResultCode.NoConnection: return "No connection to Steam services.";
                case SteamApiResultCode.InvalidParam: return "Invalid parameter sent to Steam API.";
                case SteamApiResultCode.FileNotFound: return "Workshop item not found (Item is deleted or unlisted).";
                case SteamApiResultCode.Busy: return "Steam API is busy, try again later.";
                case SteamApiResultCode.InvalidState: return "Steam API reported an invalid internal state.";
                case SteamApiResultCode.AccessDenied: return "Access Denied (Item might be hidden, friends-only, or require login).";
                case SteamApiResultCode.Timeout: return "Request to Steam API timed out.";
                case SteamApiResultCode.InvalidSteamID: return "Steam reported the Workshop ID as invalid.";
                case SteamApiResultCode.ServiceUnavailable: return "Steam service is currently unavailable.";
                case SteamApiResultCode.NotLoggedOn: return "Operation may require Steam login (for non-public items).";
                case SteamApiResultCode.LimitExceeded: return "Limit exceeded (Possibly too many items requested at once).";
                case SteamApiResultCode.RegionLocked: return "Item access is restricted by region.";
                case SteamApiResultCode.RateLimitExceeded: return "API rate limit exceeded, please wait before trying again.";
                case SteamApiResultCode.ItemDeleted: return "Workshop item has been deleted.";
                case SteamApiResultCode.IPBanned: return "Your IP address may be temporarily banned by Steam API due to excessive requests.";
                case SteamApiResultCode.TooManyPending: return "Too many pending operations on Steam's side.";

                case SteamApiResultCode.Unknown:
                default:
                    return $"Unknown or unhandled Steam API result code ({resultCode}).";
            }
        }
        
        /// <summary>
        /// Extracts version-like tags from a list of Steam API tags, then sorts them.
        /// </summary>
        /// <param name="apiTags">The list of tags from the API response.</param>
        /// <returns>A sorted list of version strings.</returns>
        public static List<string> ExtractAndSortVersionTags(List<SteamTag>? apiTags)
        {
            if (apiTags == null || !apiTags.Any())
            {
                return new List<string>();
            }

            var versionTags = apiTags
                .Select(t => t.Tag)
                .Where(t => !string.IsNullOrWhiteSpace(t) && Version.TryParse(t, out _))
                .ToList();

            // Sort using Version class for correct numeric sorting (e.g., 1.10 > 1.2)
            versionTags.Sort((v1, v2) => new Version(v1).CompareTo(new Version(v2)));

            return versionTags;
        }
    }
}
