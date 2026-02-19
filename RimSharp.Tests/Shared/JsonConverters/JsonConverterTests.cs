using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using RimSharp.Shared.JsonConverters;
using Xunit;

namespace RimSharp.Tests.Shared.JsonConverters
{
    public class JsonConverterTests
    {
        private readonly JsonSerializerOptions _options;

        public JsonConverterTests()
        {
            _options = new JsonSerializerOptions();
            _options.Converters.Add(new StringOrStringListConverter());
        }

        private class TestWrapper
        {
            [JsonConverter(typeof(StringOrStringListConverter))]
            public List<string>? Items { get; set; }
        }

        [Fact]
        public void StringOrStringListConverter_WhenInputIsSingleString_ShouldReturnListWithOneItem()
        {
            // Arrange
            string json = "{\"Items\": \"Value1\"}";

            // Act
            var result = JsonSerializer.Deserialize<TestWrapper>(json, _options);

            // Assert
            result.Should().NotBeNull();
            result!.Items.Should().NotBeNull();
            result.Items.Should().HaveCount(1);
            result.Items![0].Should().Be("Value1");
        }

        [Fact]
        public void StringOrStringListConverter_WhenInputIsArray_ShouldReturnFullList()
        {
            // Arrange
            string json = "{\"Items\": [\"Value1\", \"Value2\"]}";

            // Act
            var result = JsonSerializer.Deserialize<TestWrapper>(json, _options);

            // Assert
            result.Should().NotBeNull();
            result!.Items.Should().NotBeNull();
            result.Items.Should().HaveCount(2);
            result.Items.Should().Contain("Value1", "Value2");
        }

        [Fact]
        public void StringOrStringListConverter_WhenInputIsNull_ShouldReturnEmptyList()
        {
            // Arrange
            string json = "{\"Items\": null}";

            // Act
            var result = JsonSerializer.Deserialize<TestWrapper>(json, _options);

            // Assert
            result.Should().NotBeNull();
            // StringOrStringListConverter returns new List<string>() for null token
            result!.Items.Should().NotBeNull();
            result.Items.Should().BeEmpty();
        }
    }
}
