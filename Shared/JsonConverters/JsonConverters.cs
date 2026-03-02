using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimSharp.Shared.JsonConverters
{
    public class StringOrStringListConverter : JsonConverter<List<string>>
    {
        public override bool HandleNull => true;

        public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {

                string? value = reader.GetString();
                return !string.IsNullOrEmpty(value) ? new List<string> { value } : new List<string>();
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {

                var listOptions = new JsonSerializerOptions(options);
                listOptions.Converters.Remove(this); // Prevent infinite loop

                return JsonSerializer.Deserialize<List<string>>(ref reader, listOptions) ?? new List<string>();
            }

             if (reader.TokenType == JsonTokenType.Null)
             {
                 return new List<string>();
             }

            throw new JsonException($"Unexpected token type: {reader.TokenType}");
        }

        public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
        {

            if (value == null || value.Count == 0)
            {
                writer.WriteNullValue(); // Or writer.WriteStartArray(); writer.WriteEndArray();
            }

            // else if (value.Count == 1)
            // {
            //     writer.WriteStringValue(value[0]);

            else
            {

                var listOptions = new JsonSerializerOptions(options);
                listOptions.Converters.Remove(this); // Prevent infinite loop
                JsonSerializer.Serialize(writer, value, listOptions);
            }
        }
    }
}


