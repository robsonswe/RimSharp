using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Steamworks;

namespace RimSharp.Services.Workshop
{
    /// <summary>
    /// Creates the Steamworks adapter. Kept separate and NoInlining so that merely having this
    /// code path in the service never forces Steamworks.NET to load: the factory is only invoked
    /// on runtimes where Steamworks is supported.
    /// </summary>
    internal static class SteamworksInteropFactory
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ISteamworksInterop Create() => new SteamworksInterop();
    }

    /// <summary>
    /// The ONLY type in the app that touches Steamworks.NET. Public/internal signatures use
    /// primitives only (no Steamworks types), so loading this type never needs the Steamworks.NET
    /// assembly; only JIT-compiling one of its method bodies does.
    /// </summary>
    internal sealed class SteamworksInterop : ISteamworksInterop
    {
        private static readonly TimeSpan ApiCallTimeout = TimeSpan.FromSeconds(30);

        public bool Init() => SteamAPI.Init();

        public void Shutdown() => SteamAPI.Shutdown();

        public void RunCallbacks() => SteamAPI.RunCallbacks();

        public Task<SteamCallOutcome> SubscribeItemAsync(ulong publishedFileId, CancellationToken cancellationToken)
        {
            var call = SteamUGC.SubscribeItem(new PublishedFileId_t(publishedFileId));
            return WaitForApiCallAsync<RemoteStorageSubscribePublishedFileResult_t>(
                call.m_SteamAPICall, r => r.m_eResult, cancellationToken);
        }

        public Task<SteamCallOutcome> UnsubscribeItemAsync(ulong publishedFileId, CancellationToken cancellationToken)
        {
            var call = SteamUGC.UnsubscribeItem(new PublishedFileId_t(publishedFileId));
            return WaitForApiCallAsync<RemoteStorageUnsubscribePublishedFileResult_t>(
                call.m_SteamAPICall, r => r.m_eResult, cancellationToken);
        }

        public void DownloadItem(ulong publishedFileId, bool highPriority)
            => SteamUGC.DownloadItem(new PublishedFileId_t(publishedFileId), highPriority);

        public SteamItemState GetItemState(ulong publishedFileId)
            => (SteamItemState)SteamUGC.GetItemState(new PublishedFileId_t(publishedFileId));

        public bool TryGetDownloadInfo(ulong publishedFileId, out ulong bytesDownloaded, out ulong bytesTotal)
            => SteamUGC.GetItemDownloadInfo(new PublishedFileId_t(publishedFileId), out bytesDownloaded, out bytesTotal);

        public ulong[] GetSubscribedItems()
        {
            var count = SteamUGC.GetNumSubscribedItems();
            var items = new PublishedFileId_t[count];
            SteamUGC.GetSubscribedItems(items, count);

            var result = new ulong[items.Length];
            for (var i = 0; i < items.Length; i++)
                result[i] = items[i].m_PublishedFileId;
            return result;
        }

        public SteamOwnershipInfo QueryOwnership(uint appId)
        {
            var owns = SteamApps.BIsSubscribedApp(new AppId_t(appId));
            if (!owns)
                return new SteamOwnershipInfo(false, 0, 0, false);

            // Family-sharing diagnostics (logged for triage, never gates)
            return new SteamOwnershipInfo(
                true,
                SteamApps.GetAppOwner().m_SteamID,
                SteamUser.GetSteamID().m_SteamID,
                SteamApps.BIsSubscribedFromFamilySharing());
        }

        public bool BIsSubscribedApp(uint appId)
            => SteamApps.BIsSubscribedApp(new AppId_t(appId));

        public ulong GetAppOwner()
            => SteamApps.GetAppOwner().m_SteamID;

        public ulong GetSteamID()
            => SteamUser.GetSteamID().m_SteamID;

        public bool BIsSubscribedFromFamilySharing()
            => SteamApps.BIsSubscribedFromFamilySharing();

        private static async Task<SteamCallOutcome> WaitForApiCallAsync<T>(
            ulong apiCallHandle, Func<T, EResult> getResult, CancellationToken cancellationToken) where T : struct
        {
            var tcs = new TaskCompletionSource<SteamCallOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

            var callback = CallResult<T>.Create((result, ioFailure) =>
            {
                if (ioFailure)
                {
                    tcs.TrySetResult(SteamCallOutcome.NoResult);
                    return;
                }

                var code = getResult(result);
                tcs.TrySetResult(new SteamCallOutcome(true, code == EResult.k_EResultOK, code.ToString()));
            });

            callback.Set(new SteamAPICall_t(apiCallHandle));

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(ApiCallTimeout);

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                return completed == tcs.Task ? await tcs.Task.ConfigureAwait(false) : SteamCallOutcome.NoResult;
            }
            finally
            {
                callback.Dispose();
            }
        }
    }
}