#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Shared.Services.Implementations;
using Xunit;

namespace RimSharp.Tests.Services.Workshop
{
    /// <summary>
    /// Regression tests for GetPublishedFileDetailsBatchAsync caching: a failed
    /// or empty HTTP-200 payload must be rejected by the fetch delegate so the
    /// cache never stores it as a 12-hour success.
    /// </summary>
    public class SteamApiClientBatchCacheTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _cacheFilePath;

        public SteamApiClientBatchCacheTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_BatchCache_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _cacheFilePath = Path.Combine(_tempDir, "api-responses.json");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        [Theory]
        [InlineData("{\"response\":{\"result\":9,\"publishedfiledetails\":[{\"publishedfileid\":\"123\",\"result\":9,\"title\":\"Failed\"}]}}")]
        [InlineData("{\"response\":{\"result\":1,\"publishedfiledetails\":[]}}")]
        public async Task GetPublishedFileDetailsBatchAsync_FailedOrEmptyResponse_IsNotCached(string json)
        {
            var handler = new CountingHttpHandler(json);
            var cache = new ApiResponseCache(cacheFilePath: _cacheFilePath);
            var client = new SteamApiClient(new HttpClient(handler), cache);

            var first = await client.GetPublishedFileDetailsBatchAsync(new[] { "123" }, TestContext.Current.CancellationToken);
            var second = await client.GetPublishedFileDetailsBatchAsync(new[] { "123" }, TestContext.Current.CancellationToken);

            first.Should().BeNull();
            second.Should().BeNull();
            handler.CallCount.Should().Be(2, "a failed/empty batch must not be cached — the second call must re-fetch");
        }

        [Fact]
        public async Task GetPublishedFileDetailsBatchAsync_SuccessfulResponse_IsCached()
        {
            const string json = "{\"response\":{\"result\":1,\"publishedfiledetails\":[" +
                                "{\"publishedfileid\":\"123\",\"result\":1,\"title\":\"Live Title\"}]}}";
            var handler = new CountingHttpHandler(json);
            var cache = new ApiResponseCache(cacheFilePath: _cacheFilePath);
            var client = new SteamApiClient(new HttpClient(handler), cache);

            var first = await client.GetPublishedFileDetailsBatchAsync(new[] { "123" }, TestContext.Current.CancellationToken);
            var second = await client.GetPublishedFileDetailsBatchAsync(new[] { "123" }, TestContext.Current.CancellationToken);

            first!.Response.Result.Should().Be(1);
            second!.Response.PublishedFileDetails.Should().ContainSingle();
            handler.CallCount.Should().Be(1, "a successful batch should be cached");
        }

        private sealed class CountingHttpHandler : HttpMessageHandler
        {
            private readonly string _json;

            public CountingHttpHandler(string json) => _json = json;

            public int CallCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CallCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_json, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
