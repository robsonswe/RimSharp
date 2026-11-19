#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Persistent, general-purpose cache for read-only API responses.
    /// Implementations must never break the caller: cache I/O failures are
    /// treated as a miss and the fetch delegate remains the source of truth.
    /// </summary>
    public interface IApiResponseCache
    {
        /// <summary>
        /// Returns a fresh (still-valid) cached value for <paramref name="key"/>,
        /// otherwise invokes <paramref name="fetch"/> and caches a non-null
        /// result for <paramref name="ttl"/>. If <paramref name="fetch"/>
        /// returns null (e.g. a transient HTTP 429) and a possibly-expired
        /// entry exists, the stale value is returned instead. Null is never
        /// cached. Exceptions thrown by <paramref name="fetch"/> propagate.
        /// </summary>
        Task<T?> GetOrFetchAsync<T>(
            string key,
            TimeSpan ttl,
            Func<CancellationToken, Task<T?>> fetch,
            CancellationToken ct = default) where T : class;
    }
}
