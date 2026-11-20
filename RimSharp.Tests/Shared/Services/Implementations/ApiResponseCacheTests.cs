#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RimSharp.Shared.Services.Implementations;
using Xunit;

namespace RimSharp.Tests.Shared.Services.Implementations
{
    public class ApiResponseCacheTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _cacheFilePath;

        public ApiResponseCacheTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_ApiCache_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _cacheFilePath = Path.Combine(_tempDir, "api-responses.json");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        private ApiResponseCache CreateCache() => new ApiResponseCache(cacheFilePath: _cacheFilePath);

        [Fact]
        public async Task GetOrFetchAsync_CacheHit_ReturnsCachedWithoutFetching()
        {
            var cache = CreateCache();
            var fetchCalls = 0;

            var first = await cache.GetOrFetchAsync<TestPayload>("key", TimeSpan.FromHours(1), ct =>
            {
                fetchCalls++;
                return Task.FromResult<TestPayload?>(new TestPayload { Value = "hello" });
            }, TestContext.Current.CancellationToken);

            var second = await cache.GetOrFetchAsync<TestPayload>("key", TimeSpan.FromHours(1), ct =>
            {
                fetchCalls++;
                return Task.FromResult<TestPayload?>(new TestPayload { Value = "should-not-be-used" });
            }, TestContext.Current.CancellationToken);

            first!.Value.Should().Be("hello");
            second!.Value.Should().Be("hello");
            fetchCalls.Should().Be(1);
        }

        [Fact]
        public async Task GetOrFetchAsync_ExpiredEntry_Refetches()
        {
            var cache = CreateCache();
            var fetchCalls = 0;

            await cache.GetOrFetchAsync<TestPayload>("key", TimeSpan.FromTicks(-1), ct =>
            {
                fetchCalls++;
                return Task.FromResult<TestPayload?>(new TestPayload { Value = "first" });
            }, TestContext.Current.CancellationToken);

            var second = await cache.GetOrFetchAsync<TestPayload>("key", TimeSpan.FromHours(1), ct =>
            {
                fetchCalls++;
                return Task.FromResult<TestPayload?>(new TestPayload { Value = "second" });
            }, TestContext.Current.CancellationToken);

            second!.Value.Should().Be("second");
            fetchCalls.Should().Be(2);
        }

        [Fact]
        public async Task GetOrFetchAsync_NullFetchWithExistingEntry_ReturnsStaleValue()
        {
            var cache = CreateCache();

            await cache.GetOrFetchAsync<TestPayload>("key", TimeSpan.FromTicks(-1), ct =>
                Task.FromResult<TestPayload?>(new TestPayload { Value = "stale" }), TestContext.Current.CancellationToken);

            var result = await cache.GetOrFetchAsync<TestPayload>("key", TimeSpan.FromHours(1), ct =>
                Task.FromResult<TestPayload?>(null), TestContext.Current.CancellationToken);

            result!.Value.Should().Be("stale");
        }

        [Fact]
        public async Task GetOrFetchAsync_NullFetchWithoutEntry_ReturnsNullAndCachesNothing()
        {
            var cache = CreateCache();

            var result = await cache.GetOrFetchAsync<TestPayload>("missing", TimeSpan.FromHours(1), ct =>
                Task.FromResult<TestPayload?>(null), TestContext.Current.CancellationToken);

            result.Should().BeNull();
            File.Exists(_cacheFilePath).Should().BeFalse();
        }

        [Fact]
        public async Task GetOrFetchAsync_FetchThrows_PropagatesException()
        {
            var cache = CreateCache();

            Func<Task> act = async () => await cache.GetOrFetchAsync<TestPayload>("key", TimeSpan.FromHours(1),
                ct => throw new InvalidOperationException("boom"), TestContext.Current.CancellationToken);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        }

        [Fact]
        public async Task GetOrFetchAsync_PersistsAcrossInstances()
        {
            var first = CreateCache();
            await first.GetOrFetchAsync<TestPayload>("key", TimeSpan.FromHours(1), ct =>
                Task.FromResult<TestPayload?>(new TestPayload { Value = "persisted" }), TestContext.Current.CancellationToken);

            // Writes are debounced; force a deterministic flush before checking disk.
            await first.FlushAsync();
            File.Exists(_cacheFilePath).Should().BeTrue();

            var second = CreateCache();
            var fetchCalls = 0;
            var result = await second.GetOrFetchAsync<TestPayload>("key", TimeSpan.FromHours(1), ct =>
            {
                fetchCalls++;
                return Task.FromResult<TestPayload?>(new TestPayload { Value = "refetched" });
            }, TestContext.Current.CancellationToken);

            result!.Value.Should().Be("persisted");
            fetchCalls.Should().Be(0);
        }

        [Fact]
        public async Task GetOrFetchAsync_ConcurrentCalls_FetchOnlyOnce()
        {
            var cache = CreateCache();
            var fetchCalls = 0;
            var tcs = new TaskCompletionSource<TestPayload?>();

            var tasks = Enumerable.Range(0, 10).Select(_ =>
                cache.GetOrFetchAsync<TestPayload>("key", TimeSpan.FromHours(1), async ct =>
                {
                    Interlocked.Increment(ref fetchCalls);
                    return await tcs.Task;
                }, TestContext.Current.CancellationToken)).ToArray();

            // Give all callers a chance to queue on the key semaphore before releasing.
            await Task.Delay(50, TestContext.Current.CancellationToken);
            tcs.SetResult(new TestPayload { Value = "once" });

            var results = await Task.WhenAll(tasks);
            results.Should().OnlyContain(r => r!.Value == "once");
            fetchCalls.Should().Be(1);
        }

        [Fact]
        public async Task GetOrFetchAsync_DifferentKeys_FetchConcurrently()
        {
            var cache = CreateCache();
            var aStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var bStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // Each fetch signals its start and then waits for the other. If the
            // cache serialized all fetches behind one global lock, the second
            // fetch could never start and both would time out.
            var taskA = cache.GetOrFetchAsync<TestPayload>("key-A", TimeSpan.FromHours(1), async ct =>
            {
                aStarted.TrySetResult();
                await bStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                return new TestPayload { Value = "A" };
            }, TestContext.Current.CancellationToken);

            var taskB = cache.GetOrFetchAsync<TestPayload>("key-B", TimeSpan.FromHours(1), async ct =>
            {
                bStarted.TrySetResult();
                await aStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                return new TestPayload { Value = "B" };
            }, TestContext.Current.CancellationToken);

            var results = await Task.WhenAll(taskA, taskB);

            aStarted.Task.IsCompletedSuccessfully.Should().BeTrue("both fetches must start concurrently");
            bStarted.Task.IsCompletedSuccessfully.Should().BeTrue("both fetches must start concurrently");
            results[0]!.Value.Should().Be("A");
            results[1]!.Value.Should().Be("B");
        }

        [Fact]
        public async Task FlushAsync_MultipleMutations_WritesAllEntriesInOneFile()
        {
            var cache = CreateCache();

            for (var i = 0; i < 25; i++)
            {
                var value = i.ToString();
                await cache.GetOrFetchAsync<TestPayload>("key-" + i, TimeSpan.FromHours(1), ct =>
                    Task.FromResult<TestPayload?>(new TestPayload { Value = value }), TestContext.Current.CancellationToken);
            }

            // Debounced writes have not necessarily hit disk yet; a single
            // explicit flush must persist every mutation made so far.
            await cache.FlushAsync();

            File.Exists(_cacheFilePath).Should().BeTrue();
            var json = File.ReadAllText(_cacheFilePath);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.EnumerateObject().Count().Should().Be(25);

            // A fresh instance sees all of them without fetching.
            var second = CreateCache();
            for (var i = 0; i < 25; i++)
            {
                var expected = i.ToString();
                var result = await second.GetOrFetchAsync<TestPayload>("key-" + i, TimeSpan.FromHours(1), ct =>
                    Task.FromResult<TestPayload?>(new TestPayload { Value = "should-not-be-used" }),
                    TestContext.Current.CancellationToken);
                result!.Value.Should().Be(expected);
            }
        }

        [Fact]
        public async Task GetOrFetchAsync_OverCap_EvictsOldestEntries()
        {
            const int total = 2005; // MaxEntries (2000) + 5
            var cache = CreateCache();

            // Establish a clearly older first entry so eviction order is deterministic.
            await cache.GetOrFetchAsync<TestPayload>("key-0", TimeSpan.FromHours(1), ct =>
                Task.FromResult<TestPayload?>(new TestPayload { Value = "0" }), TestContext.Current.CancellationToken);
            await Task.Delay(100, TestContext.Current.CancellationToken);

            for (var i = 1; i < total; i++)
            {
                var value = i.ToString();
                await cache.GetOrFetchAsync<TestPayload>("key-" + i, TimeSpan.FromHours(1), ct =>
                    Task.FromResult<TestPayload?>(new TestPayload { Value = value }), TestContext.Current.CancellationToken);
            }

            await cache.FlushAsync();

            var json = File.ReadAllText(_cacheFilePath);
            using var doc = JsonDocument.Parse(json);
            var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();

            keys.Count.Should().BeLessThanOrEqualTo(2000);
            keys.Should().NotContain("key-0", "the oldest entry should be evicted first");
            keys.Should().Contain("key-" + (total - 1), "the newest entry should be retained");
        }

        [Fact]
        public async Task EnsureLoaded_OversizedFileOnDisk_PersistsTrimmedSetOnFlush()
        {
            const int total = 2005; // MaxEntries (2000) + 5

            // Simulate a legacy/oversized cache file written before the cap was
            // enforced (or by an older build). The store path already trims, so
            // this must be seeded directly on disk to exercise the load path.
            var seeded = new Dictionary<string, object>();
            for (var i = 0; i < total; i++)
            {
                seeded[$"key-{i}"] = new
                {
                    Json = "{\"Value\":\"" + i + "\"}",
                    // key-0 is the newest so it survives eviction and can be
                    // used as a pure cache-hit probe below.
                    CachedUtc = DateTime.UtcNow.AddSeconds(-i),
                    TtlTicks = TimeSpan.FromHours(1).Ticks
                };
            }
            File.WriteAllText(_cacheFilePath, JsonSerializer.Serialize(seeded));

            var cache = CreateCache();

            // A cache-hit probe forces EnsureLoaded() without any store mutation,
            // so the only possible source of a save is the load-time eviction.
            var probe = await cache.GetOrFetchAsync<TestPayload>("key-0", TimeSpan.FromHours(1), ct =>
                Task.FromResult<TestPayload?>(new TestPayload { Value = "should-not-be-used" }), TestContext.Current.CancellationToken);
            probe!.Value.Should().Be("0");

            await cache.FlushAsync();

            var json = File.ReadAllText(_cacheFilePath);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.EnumerateObject().Count().Should().BeLessThanOrEqualTo(2000,
                "the trimmed set must be persisted so the file stops growing");

            // A fresh instance must see the trimmed file, not the oversized one.
            var second = CreateCache();
            var reloadProbe = await second.GetOrFetchAsync<TestPayload>("key-0", TimeSpan.FromHours(1), ct =>
                Task.FromResult<TestPayload?>(new TestPayload { Value = "should-not-be-used" }), TestContext.Current.CancellationToken);
            reloadProbe!.Value.Should().Be("0");
        }

        [Fact]
        public async Task Dispose_FlushesPendingEntriesAndIsIdempotent()
        {
            var first = CreateCache();
            await first.GetOrFetchAsync<TestPayload>("key", TimeSpan.FromHours(1), ct =>
                Task.FromResult<TestPayload?>(new TestPayload { Value = "flushed-on-dispose" }), TestContext.Current.CancellationToken);

            // No explicit FlushAsync: disposal itself must persist the pending write.
            first.Dispose();
            first.Dispose(); // idempotent — must not throw on a second call

            File.Exists(_cacheFilePath).Should().BeTrue();

            // A fresh instance sees the flushed entry without any explicit flush.
            var second = CreateCache();
            var fetchCalls = 0;
            var result = await second.GetOrFetchAsync<TestPayload>("key", TimeSpan.FromHours(1), ct =>
            {
                fetchCalls++;
                return Task.FromResult<TestPayload?>(new TestPayload { Value = "should-not-be-used" });
            }, TestContext.Current.CancellationToken);

            result!.Value.Should().Be("flushed-on-dispose");
            fetchCalls.Should().Be(0);
        }

        [Fact]
        public async Task EnsureLoaded_OverCap_EvictsExpiredEntriesBeforeFreshOnes()
        {
            const int freshCount = 1995;
            const int expiredCount = 10; // 1995 + 10 = 2005 > MaxEntries (2000)
            var now = DateTime.UtcNow;

            // Seed a mix directly on disk so CachedUtc/TtlTicks are exact.
            var seeded = new Dictionary<string, object>();

            // Clearly expired: cached 2 days ago with a 1-hour TTL. Distinct
            // timestamps make the eviction order deterministic (expired-0 oldest).
            for (var i = 0; i < expiredCount; i++)
            {
                seeded[$"expired-{i}"] = new
                {
                    Json = "{\"Value\":\"expired-" + i + "\"}",
                    CachedUtc = now.AddDays(-2).AddSeconds(i),
                    TtlTicks = TimeSpan.FromHours(1).Ticks
                };
            }

            // Fresh: cached now with a 30-day TTL — all still valid.
            for (var i = 0; i < freshCount; i++)
            {
                seeded[$"fresh-{i}"] = new
                {
                    Json = "{\"Value\":\"fresh-" + i + "\"}",
                    CachedUtc = now,
                    TtlTicks = TimeSpan.FromDays(30).Ticks
                };
            }

            File.WriteAllText(_cacheFilePath, JsonSerializer.Serialize(seeded));

            var cache = CreateCache();

            // A cache-hit probe forces EnsureLoaded() (and its load-time eviction)
            // without adding a new entry.
            var probe = await cache.GetOrFetchAsync<TestPayload>("fresh-0", TimeSpan.FromDays(30), ct =>
                Task.FromResult<TestPayload?>(new TestPayload { Value = "should-not-be-used" }), TestContext.Current.CancellationToken);
            probe!.Value.Should().Be("fresh-0");

            await cache.FlushAsync();

            var json = File.ReadAllText(_cacheFilePath);
            using var doc = JsonDocument.Parse(json);
            var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();

            keys.Count.Should().Be(2000, "the cap must be enforced");

            // All fresh entries survive: expired ones are evicted first.
            for (var i = 0; i < freshCount; i++)
            {
                keys.Should().Contain("fresh-" + i);
            }

            // Only 5 of the 10 expired entries fit alongside all fresh ones;
            // the 5 oldest expired entries must be the ones dropped.
            for (var i = 0; i < 5; i++)
            {
                keys.Should().NotContain("expired-" + i, "the oldest expired entries must be evicted first");
            }
            for (var i = 5; i < expiredCount; i++)
            {
                keys.Should().Contain("expired-" + i);
            }
        }

        public sealed class TestPayload
        {
            public string Value { get; set; } = string.Empty;
        }
    }
}
