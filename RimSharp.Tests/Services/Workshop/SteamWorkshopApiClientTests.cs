using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RimSharp.Services.Workshop;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Services.Workshop
{
    public class SteamWorkshopApiClientTests
    {
        [Fact]
        public async Task GetModDetailsBatchAsync_TransientErrorThenSuccess_RetriesAndReturnsData()
        {
            var successJson = CreateApiResponseJson("123", result: 1);
            var handler = new SequenceStubHttpHandler();
            handler.EnqueueResponse(HttpStatusCode.ServiceUnavailable, "Service Unavailable");
            handler.EnqueueResponse(HttpStatusCode.OK, successJson);

            var client = new SteamWorkshopApiClient(new HttpClient(handler));

            var results = await client.GetModDetailsBatchAsync(new[] { "123" }, TestContext.Current.CancellationToken);

            handler.CallCount.Should().Be(2);
            results.Should().ContainSingle();
            results[0].PublishedFileId.Should().Be("123");
            results[0].Result.Should().Be(1);
        }

        [Fact]
        public async Task GetModDetailsBatchAsync_PersistentTransientError_ExhaustsMaxAttemptsAndReturnsEmpty()
        {
            var handler = new SequenceStubHttpHandler();
            handler.EnqueueResponse(HttpStatusCode.InternalServerError, "Error 1");
            handler.EnqueueResponse(HttpStatusCode.InternalServerError, "Error 2");
            handler.EnqueueResponse(HttpStatusCode.InternalServerError, "Error 3");

            var client = new SteamWorkshopApiClient(new HttpClient(handler));

            var results = await client.GetModDetailsBatchAsync(new[] { "123" }, TestContext.Current.CancellationToken);

            handler.CallCount.Should().Be(3);
            results.Should().BeEmpty();
        }

        [Fact]
        public async Task GetModDetailsBatchAsync_NonRetryableStatusCode_FailsImmediatelyWithoutRetrying()
        {
            var handler = new SequenceStubHttpHandler();
            handler.EnqueueResponse(HttpStatusCode.NotFound, "Not Found");

            var client = new SteamWorkshopApiClient(new HttpClient(handler));

            var results = await client.GetModDetailsBatchAsync(new[] { "123" }, TestContext.Current.CancellationToken);

            handler.CallCount.Should().Be(1);
            results.Should().BeEmpty();
        }

        [Fact]
        public async Task GetModDetailsBatchAsync_NetworkExceptionThenSuccess_RetriesAndReturnsData()
        {
            var successJson = CreateApiResponseJson("456", result: 1);
            var handler = new SequenceStubHttpHandler();
            handler.EnqueueException(new HttpRequestException("Connection dropped"));
            handler.EnqueueResponse(HttpStatusCode.OK, successJson);

            var client = new SteamWorkshopApiClient(new HttpClient(handler));

            var results = await client.GetModDetailsBatchAsync(new[] { "456" }, TestContext.Current.CancellationToken);

            handler.CallCount.Should().Be(2);
            results.Should().ContainSingle();
            results[0].PublishedFileId.Should().Be("456");
        }

        [Fact]
        public async Task GetModDetailsBatchAsync_MalformedPayloadThenSuccess_RetriesAndReturnsData()
        {
            var successJson = CreateApiResponseJson("789", result: 1);
            var handler = new SequenceStubHttpHandler();
            // Response missing the publishedfiledetails array (deserializes with null list)
            handler.EnqueueResponse(HttpStatusCode.OK, "{\"response\":{\"result\":1}}");
            handler.EnqueueResponse(HttpStatusCode.OK, successJson);

            var client = new SteamWorkshopApiClient(new HttpClient(handler));

            var results = await client.GetModDetailsBatchAsync(new[] { "789" }, TestContext.Current.CancellationToken);

            handler.CallCount.Should().Be(2);
            results.Should().ContainSingle();
            results[0].PublishedFileId.Should().Be("789");
        }

        [Fact]
        public async Task GetModDetailsBatchAsync_MoreThanBatchSize_ChunksRequestsIntoBatchesOf300()
        {
            // 350 items: should be chunked into 300 + 50 (2 HTTP requests)
            var ids = Enumerable.Range(1, 350).Select(i => i.ToString()).ToList();
            var chunk1Json = CreateApiResponseJson("1", result: 1);
            var chunk2Json = CreateApiResponseJson("301", result: 1);

            var handler = new SequenceStubHttpHandler();
            handler.EnqueueResponse(HttpStatusCode.OK, chunk1Json);
            handler.EnqueueResponse(HttpStatusCode.OK, chunk2Json);

            var client = new SteamWorkshopApiClient(new HttpClient(handler));

            var results = await client.GetModDetailsBatchAsync(ids, TestContext.Current.CancellationToken);

            handler.CallCount.Should().Be(2);
            results.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetModDetailsAsync_InvalidSteamId_ReturnsNullWithoutCallingApi()
        {
            var handler = new SequenceStubHttpHandler();
            var client = new SteamWorkshopApiClient(new HttpClient(handler));

            var result = await client.GetModDetailsAsync("not-a-number", TestContext.Current.CancellationToken);

            handler.CallCount.Should().Be(0);
            result.Should().BeNull();
        }

        // ------------------------------------------------------------------ //

        private static string CreateApiResponseJson(string publishedFileId, int result) =>
            "{\"response\":{\"result\":1,\"publishedfiledetails\":[" +
            $"{{\"publishedfileid\":\"{publishedFileId}\",\"result\":{result}," +
            "\"consumer_appid\":294100,\"title\":\"Test\",\"time_updated\":1000,\"file_size\":100,\"tags\":[]}]}}";

        /// <summary>
        /// A test HTTP handler that yields queued responses or exceptions sequentially.
        /// </summary>
        private sealed class SequenceStubHttpHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpResponseMessage>> _responses = new();

            public int CallCount { get; private set; }

            public void EnqueueResponse(HttpStatusCode statusCode, string content)
            {
                _responses.Enqueue(() => new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(content, Encoding.UTF8, "application/json")
                });
            }

            public void EnqueueException(Exception ex)
            {
                _responses.Enqueue(() => throw ex);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CallCount++;

                if (_responses.Count == 0)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("No queued response available")
                    });
                }

                var factory = _responses.Dequeue();
                return Task.FromResult(factory());
            }
        }
    }
}