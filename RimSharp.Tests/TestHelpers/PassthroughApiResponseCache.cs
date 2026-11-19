#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Tests.TestHelpers
{
    /// <summary>
    /// Test double that bypasses caching entirely: every call invokes the
    /// fetch delegate. Used by client tests that assert raw HTTP behavior
    /// (retry counts, call counts) and must not observe cached results.
    /// </summary>
    public sealed class PassthroughApiResponseCache : IApiResponseCache
    {
        public async Task<T?> GetOrFetchAsync<T>(
            string key,
            TimeSpan ttl,
            Func<CancellationToken, Task<T?>> fetch,
            CancellationToken ct = default) where T : class
            => await fetch(ct);
    }
}
