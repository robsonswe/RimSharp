#nullable enable
using System.Text.Json;
using FluentAssertions;
using RimSharp.Features.WorkshopDownloader.Models;
using Xunit;

namespace RimSharp.Tests.Features.WorkshopDownloader.Models
{
    public class SteamApiResponseTests
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        [Theory]
        [InlineData("consumer_app_id")]
        [InlineData("consumer_appid")]
        public void ConsumerAppId_AcceptsBothSteamSpellings(string fieldName)
        {
            var json = $"{{\"response\":{{\"result\":1,\"publishedfiledetails\":[" +
                       $"{{\"publishedfileid\":\"123\",\"result\":1,\"{fieldName}\":294100,\"title\":\"Test\"}}]}}}}";

            var response = JsonSerializer.Deserialize<SteamApiResponse>(json, Options);

            response.Should().NotBeNull();
            var detail = response!.Response.PublishedFileDetails.Should().ContainSingle().Subject;
            detail.ConsumerAppId.Should().Be(294100);
        }
    }
}
