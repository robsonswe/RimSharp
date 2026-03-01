using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using RimSharp.Shared.JsonConverters;
using Xunit;

namespace RimSharp.Tests.Shared.JsonConverters
{
    public class StringOrStringListConverterTests
    {
        private readonly JsonSerializerOptions _options;

        public StringOrStringListConverterTests()
        {
            _options = new JsonSerializerOptions();
            _options.Converters.Add(new StringOrStringListConverter());
        }

        [Fact]
        public void Read_WhenJsonIsString_ShouldReturnListWithOneItem()
        {
            // Arrange
            string json = "\"test-value\"";

            // Act
            var result = JsonSerializer.Deserialize<List<string>>(json, _options);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result.Should().Contain("test-value");
        }

        [Fact]
        public void Read_WhenJsonIsArray_ShouldReturnListWithItems()
        {
            // Arrange
            string json = "[\"item1\", \"item2\"]";

            // Act
            var result = JsonSerializer.Deserialize<List<string>>(json, _options);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
            result.Should().ContainInOrder("item1", "item2");
        }

        [Fact]
        public void Read_WhenJsonIsNull_ShouldReturnEmptyList()
        {
            // Arrange
            string json = "null";

            // Act
            var result = JsonSerializer.Deserialize<List<string>>(json, _options);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public void Write_ShouldWriteAsArray()
        {
            // Arrange
            var list = new List<string> { "val1", "val2" };

            // Act
            var json = JsonSerializer.Serialize(list, _options);

            // Assert
            json.Should().Be("[\"val1\",\"val2\"]");
        }
    }
}
