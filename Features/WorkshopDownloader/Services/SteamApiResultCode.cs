#nullable enable

namespace RimSharp.Features.WorkshopDownloader.Services
{
    /// <summary>
    /// Represents relevant result codes returned by the Steam Web API,
    /// particularly for GetPublishedFileDetails.
    /// See: https://partner.steamgames.com/doc/api/steam_api#EResult
    /// </summary>
    public enum SteamApiResultCode
    {
        /// <summary>
        /// Code not explicitly handled or recognized.
        /// </summary>
        Unknown = 0, // Assuming 0 isn't a used success/fail code here, otherwise use -1

        /// <summary>
        /// Success. (k_EResultOK)
        /// </summary>
        OK = 1,

        /// <summary>
        /// Generic failure. (k_EResultFail)
        /// </summary>
        Fail = 2,

        /// <summary>
        /// Steam client connection issue or server unavailable. (k_EResultNoConnection)
        /// </summary>
        NoConnection = 3,

        /// <summary>
        /// A parameter was incorrect (e.g., invalid ID format, though pre-checked). (k_EResultInvalidParam)
        /// </summary>
        InvalidParam = 8,

        /// <summary>
        /// File was not found (possibly deleted or private). (k_EResultFileNotFound)
        /// </summary>
        FileNotFound = 9,

        /// <summary>
        /// Called method is busy - action not taken. (k_EResultBusy)
        /// </summary>
        Busy = 10,

        /// <summary>
        /// Called object was in an invalid state. (k_EResultInvalidState)
        /// </summary>
        InvalidState = 11,

        /// <summary>
        /// Access is denied (e.g., item is hidden, friends-only, requires login). (k_EResultAccessDenied)
        /// </summary>
        AccessDenied = 15,

        /// <summary>
        /// Operation timed out. (k_EResultTimeout)
        /// </summary>
        Timeout = 16,

        /// <summary>
        /// The Steam ID provided was invalid according to Steam. (k_EResultInvalidSteamID)
        /// </summary>
        InvalidSteamID = 19,

        /// <summary>
        /// The requested service is currently unavailable. (k_EResultServiceUnavailable)
        /// </summary>
        ServiceUnavailable = 20,

        /// <summary>
        /// The user is not logged on (might be relevant for non-public items). (k_EResultNotLoggedOn)
        /// </summary>
        NotLoggedOn = 21, // Less common for anonymous GetPublishedFileDetails

        /// <summary>
        /// Rate limit exceeded, or too many items requested. (k_EResultLimitExceeded)
        /// </summary>
        LimitExceeded = 25,

        /// <summary>
        /// Action could not be completed due to region restrictions. (k_EResultRegionLocked)
        /// </summary>
        RegionLocked = 83,

        /// <summary>
        /// Temporary rate limit exceeded, try again later. (k_EResultRateLimitExceeded)
        /// </summary>
        RateLimitExceeded = 84,

        /// <summary>
        /// The item being accessed has been deleted. (k_EResultItemDeleted)
        /// </summary>
        ItemDeleted = 86,

        /// <summary>
        /// The IP address has been banned from taking this action. (k_EResultIPBanned)
        /// </summary>
        IPBanned = 105,

        /// <summary>
        /// Too many pending operations. (k_EResultTooManyPending)
        /// </summary>
        TooManyPending = 108,
    }
}
