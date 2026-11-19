#nullable enable
using System;
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

        public sealed class TestPayload
        {
            public string Value { get; set; } = string.Empty;
        }
    }
}
