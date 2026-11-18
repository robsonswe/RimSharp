#nullable enable
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using RimSharp.Infrastructure.Mods.Rules;

namespace RimSharp.Shared.Models
{
    /// <summary>
    /// Fetch mode of a rule database source.
    /// <list type="bullet">
    /// <item><b>RepoFile</b> — a raw file GET (GitHub raw or any static URL),
    /// cached locally and refreshed with ETag/304 conditional requests.</item>
    /// <item><b>LocalFile</b> — a user-picked file on disk, read as-is (never
    /// fetched over the network).</item>
    /// </list>
    /// Serialized as the lowercase literals "repoFile" / "localFile".
    /// </summary>
    [JsonConverter(typeof(ModRuleSourceTypeConverter))]
    public enum ModRuleSourceType
    {
        RepoFile = 0,
        LocalFile = 1,
        Mod = 2
    }

    /// <summary>
    /// One entry of an external data source registry (Rules, ModInfo, Replacements,
    /// NoVersionWarning). The registry is a priority-ordered list of these records;
    /// array order IS the priority (first entry = highest priority).
    /// </summary>
    public class ModRuleSourceRecord
    {
        /// <summary>Stable identifier; also the cache folder name under Data/<Category>/.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Display name shown in the settings UI.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>How the file is obtained (repoFile / localFile / mod).</summary>
        public ModRuleSourceType Type { get; set; } = ModRuleSourceType.RepoFile;

        /// <summary>Raw file URL for <see cref="ModRuleSourceType.RepoFile"/> sources.</summary>
        public string? Url { get; set; }

        /// <summary>Absolute path on disk for <see cref="ModRuleSourceType.LocalFile"/> sources.</summary>
        public string? LocalPath { get; set; }

        /// <summary>Steam workshop ID or folder name for <see cref="ModRuleSourceType.Mod"/> sources.</summary>
        public string? ModFolderId { get; set; }

        /// <summary>Disabled sources are kept in the registry but never synced or merged.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Reserved for a future installer-shipped (read-only) source. Always
        /// false today: no bundled overlay exists (the curated RimSharpDB stays
        /// a synced source).
        /// </summary>
        public bool Bundled { get; set; }

        /// <summary>True for the built-in curated RimSharpDB entry.</summary>
        [JsonIgnore]
        public bool Curated => string.Equals(Id, ModRuleSourceRegistry.CuratedSourceId, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether this source is locked (its identity and URL cannot be changed
        /// or removed). Curated RimSharpDB and installed local mods are locked:
        /// they can only be enabled/disabled and reordered, but not edited or deleted.
        /// </summary>
        [JsonIgnore]
        public bool Locked => Curated || Type == ModRuleSourceType.Mod;
    }

    /// <summary>
    /// Serializes <see cref="ModRuleSourceType"/> as the lowercase literals
    /// "repoFile" / "localFile" / "mod" (and accepts any casing on read).
    /// </summary>
    public sealed class ModRuleSourceTypeConverter : JsonConverter<ModRuleSourceType>
    {
        public override ModRuleSourceType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numeric))
            {
                return numeric switch
                {
                    1 => ModRuleSourceType.LocalFile,
                    2 => ModRuleSourceType.Mod,
                    _ => ModRuleSourceType.RepoFile
                };
            }

            var value = reader.GetString();
            return value?.Trim().ToLowerInvariant() switch
            {
                "localfile" => ModRuleSourceType.LocalFile,
                "mod" => ModRuleSourceType.Mod,
                _ => ModRuleSourceType.RepoFile
            };
        }

        public override void Write(Utf8JsonWriter writer, ModRuleSourceType value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                ModRuleSourceType.LocalFile => "localFile",
                ModRuleSourceType.Mod => "mod",
                _ => "repoFile"
            });
        }
    }
}