using System;
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Services.Workshop
{
    // IMPORTANT: this file must NOT reference the Steamworks namespace or any Steamworks.NET type.
    // SteamSubscriptionService depends only on what is declared here, so the service (and every
    // async state machine the compiler generates for it) stays loadable on runtimes where the
    // Steamworks.NET assembly is not present in the output (e.g. osx-arm64).

    /// <summary>Mirror of Steamworks' EItemState bit flags (stable Steam API constants).</summary>
    [Flags]
    public enum SteamItemState : uint
    {
        None = 0,
        Subscribed = 1,
        LegacyItem = 2,
        Installed = 4,
        NeedsUpdate = 8,
        Downloading = 16,
        DownloadPending = 32,
    }

    /// <summary>Result of an async Steam API call. Completed=false means timeout / IO failure.</summary>
    public readonly record struct SteamCallOutcome(bool Completed, bool IsOk, string ResultName)
    {
        public static SteamCallOutcome NoResult => new(false, false, "No result");
    }

    public readonly record struct SteamOwnershipInfo(bool OwnsApp, ulong OwnerId, ulong CurrentUserId, bool IsFamilyShared);

    /// <summary>
    /// Thin seam over Steamworks.NET. Only primitives / types declared in this file appear in
    /// signatures. The single production implementation is <see cref="SteamworksInterop"/>.
    /// </summary>
    public interface ISteamworksInterop
    {
        bool Init();
        void Shutdown();
        void RunCallbacks();

        Task<SteamCallOutcome> SubscribeItemAsync(ulong publishedFileId, CancellationToken cancellationToken);
        Task<SteamCallOutcome> UnsubscribeItemAsync(ulong publishedFileId, CancellationToken cancellationToken);
        void DownloadItem(ulong publishedFileId, bool highPriority);
        SteamItemState GetItemState(ulong publishedFileId);
        bool TryGetDownloadInfo(ulong publishedFileId, out ulong bytesDownloaded, out ulong bytesTotal);
        ulong[] GetSubscribedItems();
        SteamOwnershipInfo QueryOwnership(uint appId);

        // SteamClaimValidator needs
        bool BIsSubscribedApp(uint appId);
        ulong GetAppOwner();
        ulong GetSteamID();
        bool BIsSubscribedFromFamilySharing();
    }
}