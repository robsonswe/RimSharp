#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Shared.Services.Implementations
{
    /// <summary>
    /// Persistent, general-purpose cache for read-only API responses.
    ///
    /// All entries live in a single JSON file (<c>api-responses.json</c>) under
    /// <see cref="AppDataPaths.CacheDirectory"/>. The file is loaded lazily once.
    /// Coordination is per key: callers asking for different keys fetch
    /// concurrently, while callers for the same key deduplicate onto one fetch.
    /// Writes are debounced/coalesced and atomic (write to a .tmp file, then move
    /// over the target). The in-memory entry set is capped at <see cref="MaxEntries"/>.
    /// Caching is strictly best-effort: any read/write/serialization failure is
    /// swallowed and treated as a miss, never surfaced to callers.
    /// </summary>
    public sealed class ApiResponseCache : IApiResponseCache
    {
        private const int MaxEntries = 2000;
        private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(1000);

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // Guards _entries and _dirty. Held only for short in-memory operations —
        // never across fetch (network I/O) or file I/O.
        private readonly object _entriesLock = new();
        // Guards the one-time lazy load from disk (which may do file I/O).
        private readonly object _loadLock = new();
        // Guards _saveCts while resetting the debounce window.
        private readonly object _debounceLock = new();

        // One semaphore per key so different keys fetch concurrently while
        // same-key callers deduplicate. Keys are a few thousand at most in
        // practice, so we intentionally keep these for the lifetime of the cache
        // (no cleanup) — the dictionary is bounded by the number of distinct keys.
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks =
            new(StringComparer.Ordinal);

        // Serializes persistence so only one snapshot/write happens at a time.
        private readonly SemaphoreSlim _saveLock = new(1, 1);

        private readonly string _cacheFilePath;
        private readonly ILoggerService? _logger;

        private Dictionary<string, ApiCacheEntry>? _entries;
        private volatile bool _loaded;
        private bool _dirty;
        private CancellationTokenSource? _saveCts;

        /// <param name="logger">Optional logger for cache I/O diagnostics.</param>
        /// <param name="cacheFilePath">
        /// Optional explicit file path (used by tests). Defaults to
        /// &lt;CacheDirectory&gt;/api-responses.json.
        /// </param>
        public ApiResponseCache(ILoggerService? logger = null, string? cacheFilePath = null)
        {
            _logger = logger;
            _cacheFilePath = cacheFilePath ?? Path.Combine(AppDataPaths.CacheDirectory, "api-responses.json");
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        public async Task<T?> GetOrFetchAsync<T>(
            string key,
            TimeSpan ttl,
            Func<CancellationToken, Task<T?>> fetch,
            CancellationToken ct = default) where T : class
        {
            EnsureLoaded();

            // (a) Short in-memory fresh-hit check. Copy the payload out under the
            // lock so deserialization (CPU work) happens outside it.
            string? cachedJson = null;
            lock (_entriesLock)
            {
                if (_entries!.TryGetValue(key, out var entry) && IsFresh(entry))
                {
                    cachedJson = entry.Json;
                }
            }
            if (cachedJson != null && TryDeserialize<T>(cachedJson, out var cached))
            {
                return cached;
            }
            // A corrupt fresh payload falls through and is treated as a miss.

            // (b) Per-key coordination using the caller's cancellation token.
            var keyLock = _keyLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
            await keyLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // (c) Re-check: another caller may have populated the entry while
                // we were waiting on the key semaphore.
                string? racedJson = null;
                lock (_entriesLock)
                {
                    if (_entries!.TryGetValue(key, out var entry) && IsFresh(entry))
                    {
                        racedJson = entry.Json;
                    }
                }
                if (racedJson != null && TryDeserialize<T>(racedJson, out var raced))
                {
                    return raced;
                }

                // (d) Fetch OUTSIDE all locks so different keys run concurrently.
                var fresh = await fetch(ct).ConfigureAwait(false);

                if (fresh != null)
                {
                    // Never let a serialization/IO problem break the caller.
                    try
                    {
                        var json = JsonSerializer.Serialize(fresh, SerializerOptions);
                        lock (_entriesLock)
                        {
                            // (e) Store, enforce the cap, and mark dirty.
                            _entries![key] = new ApiCacheEntry
                            {
                                Json = json,
                                CachedUtc = DateTime.UtcNow,
                                TtlTicks = ttl.Ticks
                            };
                            EvictOldestLocked();
                            _dirty = true;
                        }
                        ScheduleSave();
                    }
                    catch (Exception ex)
                    {
                        Log($"Could not cache response for '{key}': {ex.Message}");
                    }
                    return fresh;
                }

                // Fetch returned null (e.g. HTTP 429 / transient failure):
                // fall back to a possibly-expired entry, but never cache null.
                string? staleJson = null;
                lock (_entriesLock)
                {
                    if (_entries!.TryGetValue(key, out var entry))
                    {
                        staleJson = entry.Json;
                    }
                }
                if (staleJson != null && TryDeserialize<T>(staleJson, out var stale))
                {
                    return stale;
                }
                return null;
            }
            finally
            {
                // (f) Always release the key semaphore.
                keyLock.Release();
            }
        }

        private static bool IsFresh(ApiCacheEntry entry)
        {
            var cachedUtc = entry.CachedUtc.Kind == DateTimeKind.Utc
                ? entry.CachedUtc
                : DateTime.SpecifyKind(entry.CachedUtc, DateTimeKind.Utc);
            return DateTime.UtcNow - cachedUtc < TimeSpan.FromTicks(entry.TtlTicks);
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_loadLock)
            {
                if (_loaded) return;

                var entries = new Dictionary<string, ApiCacheEntry>(StringComparer.Ordinal);
                try
                {
                    if (File.Exists(_cacheFilePath))
                    {
                        var json = File.ReadAllText(_cacheFilePath);
                        var loaded = JsonSerializer.Deserialize<Dictionary<string, ApiCacheEntry>>(json, SerializerOptions);
                        if (loaded != null)
                        {
                            // Rebuild with an ordinal (case-sensitive) comparer.
                            foreach (var pair in loaded)
                            {
                                entries[pair.Key] = pair.Value;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Could not load API response cache (treating as empty): {ex.Message}");
                }

                lock (_entriesLock)
                {
                    _entries = entries;
                    // Enforce the cap on the freshly loaded set as well.
                    EvictOldestLocked();
                }
                _loaded = true;
            }
        }

        /// <summary>
        /// Removes the oldest entries until at most <see cref="MaxEntries"/> remain.
        /// Caller holds <see cref="_entriesLock"/>. Eviction is a mutation, so it
        /// marks the cache dirty and schedules a save — this covers both the store
        /// path and the load path (where an oversized file is trimmed on load).
        /// ScheduleSave only touches _debounceLock and spawns a delayed flush, so
        /// calling it while _entriesLock is held cannot re-enter _entriesLock.
        /// </summary>
        private void EvictOldestLocked()
        {
            if (_entries == null || _entries.Count <= MaxEntries) return;

            var oldestKeys = _entries
                .OrderBy(pair => pair.Value.CachedUtc)
                .Take(_entries.Count - MaxEntries)
                .Select(pair => pair.Key)
                .ToList();
            foreach (var key in oldestKeys)
            {
                _entries.Remove(key);
            }

            _dirty = true;
            ScheduleSave();
        }

        /// <summary>Resets the debounce window so rapid mutations coalesce into one write.</summary>
        private void ScheduleSave()
        {
            CancellationTokenSource cts;
            lock (_debounceLock)
            {
                // Supersede the previous window. Old CTS objects are not disposed:
                // they are only referenced by their in-flight delay task, which
                // completes shortly after cancellation and is then collected.
                _saveCts?.Cancel();
                cts = new CancellationTokenSource();
                _saveCts = cts;
            }
            _ = FlushAfterDelayAsync(cts);
        }

        private async Task FlushAfterDelayAsync(CancellationTokenSource cts)
        {
            try
            {
                await Task.Delay(SaveDebounce, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return; // A newer mutation superseded this debounce window.
            }
            await FlushAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Flushes any pending mutations to disk. Internal so tests can force a
        /// deterministic write instead of waiting for the debounce timer.
        /// </summary>
        internal async Task FlushAsync()
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                FlushCore();
            }
            finally
            {
                _saveLock.Release();
            }
        }

        /// <summary>Snapshot + atomic write. Caller holds <see cref="_saveLock"/>.</summary>
        private void FlushCore()
        {
            Dictionary<string, ApiCacheEntry> snapshot;
            lock (_entriesLock)
            {
                if (!_dirty || _entries == null) return;
                // Snapshot and clear the flag atomically: any mutation after this
                // point sets _dirty again and schedules its own save.
                snapshot = new Dictionary<string, ApiCacheEntry>(_entries, StringComparer.Ordinal);
                _dirty = false;
            }

            try
            {
                var directory = Path.GetDirectoryName(_cacheFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var tmpPath = _cacheFilePath + ".tmp";
                var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, _cacheFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Log($"Could not persist API response cache: {ex.Message}");
            }
        }

        /// <summary>Best-effort synchronous flush on normal shutdown.</summary>
        private void OnProcessExit(object? sender, EventArgs e)
        {
            try
            {
                if (!_saveLock.Wait(500)) return;
                try
                {
                    FlushCore();
                }
                finally
                {
                    _saveLock.Release();
                }
            }
            catch
            {
                // Never block or throw during process shutdown.
            }
        }

        private static bool TryDeserialize<T>(string json, out T? value) where T : class
        {
            try
            {
                value = JsonSerializer.Deserialize<T>(json, SerializerOptions);
                return value != null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ApiResponseCache] Could not deserialize cached value: {ex.Message}");
                value = null;
                return false;
            }
        }

        private void Log(string message)
        {
            if (_logger != null)
            {
                _logger.LogWarning(message, "ApiResponseCache");
            }
            else
            {
                Debug.WriteLine($"[ApiResponseCache] {message}");
            }
        }

        private sealed class ApiCacheEntry
        {
            public string Json { get; set; } = string.Empty;
            public DateTime CachedUtc { get; set; }
            public long TtlTicks { get; set; }
        }
    }
}
