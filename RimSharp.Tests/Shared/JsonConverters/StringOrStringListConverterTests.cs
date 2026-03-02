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

            string json = "\"test-value\"";

            var result = JsonSerializer.Deserialize<List<string>>(json, _options);

            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result.Should().Contain("test-value");
        }

        [Fact]
        public void Read_WhenJsonIsArray_ShouldReturnListWithItems()
        {

            string json = "[\"item1\", \"item2\"]";

            var result = JsonSerializer.Deserialize<List<string>>(json, _options);

            result.Should().NotBeNull();
            result.Should().HaveCount(2);
            result.Should().ContainInOrder("item1", "item2");
        }

        [Fact]
        public void Read_WhenJsonIsNull_ShouldReturnEmptyList()
        {

            string json = "null";

            var result = JsonSerializer.Deserialize<List<string>>(json, _options);

            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public void Write_ShouldWriteAsArray()
        {

            var list = new List<string> { "val1", "val2" };

            var json = JsonSerializer.Serialize(list, _options);

            json.Should().Be("[\"val1\",\"val2\"]");
        }
    }
}

