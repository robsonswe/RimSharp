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
                // If it's a single string, return a list containing just that string
                string value = reader.GetString();
                return !string.IsNullOrEmpty(value) ? new List<string> { value } : new List<string>();
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                // If it's an array, deserialize it as a list normally
                // Create a new options instance without this converter to avoid recursion
                var listOptions = new JsonSerializerOptions(options);
                listOptions.Converters.Remove(this); // Prevent infinite loop

                return JsonSerializer.Deserialize<List<string>>(ref reader, listOptions);
            }

            // Handle null or unexpected types
             if (reader.TokenType == JsonTokenType.Null)
             {
                 return new List<string>(); // Return empty list for null
             }

            throw new JsonException($"Unexpected token type: {reader.TokenType}");
        }

        public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
        {
            // Decide how to write back to JSON (optional, but good practice)
            if (value == null || value.Count == 0)
            {
                writer.WriteNullValue(); // Or writer.WriteStartArray(); writer.WriteEndArray();
            }
            // Optional: Write single-element lists as strings to mimic source inconsistency
            // else if (value.Count == 1)
            // {
            //     writer.WriteStringValue(value[0]);
            // }
            else
            {
                // Default: Write as an array always
                // Create a new options instance without this converter to avoid recursion
                var listOptions = new JsonSerializerOptions(options);
                listOptions.Converters.Remove(this); // Prevent infinite loop
                JsonSerializer.Serialize(writer, value, listOptions);
            }
        }
    }
}