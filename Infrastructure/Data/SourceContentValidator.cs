#nullable enable
using System;
using System.Text.Json;
using System.Xml.Linq;

namespace RimSharp.Infrastructure.Data
{
    /// <summary>
    /// Pure, dependency-free validation of raw downloaded source content before
    /// it is written to disk. Each method mirrors the root shape expected by
    /// the loader that later consumes the file, so a malformed download (an
    /// HTML error page, a truncated payload, a wrong document shape) is rejected
    /// and the previously cached file is kept. A <c>null</c> return means the
    /// content is acceptable; any other string is a human-readable reason.
    /// </summary>
    public static class SourceContentValidator
    {
        /// <summary>
        /// Validates a rules database file. Accepts the curated
        /// <c>{"rules": {packageId: rule}}</c> shape and the community shape
        /// (same root, with an optional integer <c>timestamp</c> and the
        /// <c>incompatibleWith</c> alias handled by the loader). An empty
        /// <c>{"rules": {}}</c> document is valid.
        /// </summary>
        public static string? ValidateRules(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return "content is empty";
            }

            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return "root must be a JSON object";
                }

                if (TryGetPropertyIgnoreCase(root, "rules", out var rules))
                {
                    if (rules.ValueKind != JsonValueKind.Object && rules.ValueKind != JsonValueKind.Null)
                    {
                        return "'rules' must be a JSON object";
                    }

                    if (rules.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var rule in rules.EnumerateObject())
                        {
                            if (rule.Value.ValueKind != JsonValueKind.Object &&
                                rule.Value.ValueKind != JsonValueKind.Null)
                            {
                                return $"rule '{rule.Name}' must be a JSON object";
                            }
                        }
                    }
                }

                if (TryGetPropertyIgnoreCase(root, "timestamp", out var timestamp) &&
                    (timestamp.ValueKind != JsonValueKind.Number || !timestamp.TryGetInt64(out _)))
                {
                    return "'timestamp' must be an integer";
                }

                return null;
            }
            catch (JsonException ex)
            {
                return $"invalid JSON: {ex.Message}";
            }
        }

        /// <summary>
        /// Validates a mod-info database file. The loader expects the root
        /// <c>{"mods": {packageId: {steamId: details}}}</c> object; an empty
        /// <c>{"mods": {}}</c> document is valid.
        /// </summary>
        public static string? ValidateModInfo(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return "content is empty";
            }

            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return "root must be a JSON object";
                }

                if (TryGetPropertyIgnoreCase(root, "mods", out var mods))
                {
                    if (mods.ValueKind != JsonValueKind.Object && mods.ValueKind != JsonValueKind.Null)
                    {
                        return "'mods' must be a JSON object";
                    }

                    if (mods.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var package in mods.EnumerateObject())
                        {
                            if (package.Value.ValueKind != JsonValueKind.Object &&
                                package.Value.ValueKind != JsonValueKind.Null)
                            {
                                return $"mod entry '{package.Name}' must be a JSON object";
                            }
                        }
                    }
                }

                return null;
            }
            catch (JsonException ex)
            {
                return $"invalid JSON: {ex.Message}";
            }
        }

        /// <summary>
        /// Validates a replacements file. The Use This Instead repository
        /// snapshot uses <c>{"rules": [ ... ]}</c>; the curated database uses
        /// <c>{"mods": { ... }}</c>. Both (including their empty forms) are
        /// accepted because both are valid inputs to the replacement loader.
        /// </summary>
        public static string? ValidateReplacements(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return "content is empty";
            }

            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return "root must be a JSON object";
                }

                if (TryGetPropertyIgnoreCase(root, "rules", out var rules) &&
                    rules.ValueKind != JsonValueKind.Array)
                {
                    return "'rules' must be a JSON array";
                }

                if (TryGetPropertyIgnoreCase(root, "mods", out var mods) &&
                    mods.ValueKind != JsonValueKind.Object &&
                    mods.ValueKind != JsonValueKind.Null)
                {
                    return "'mods' must be a JSON object";
                }

                return null;
            }
            catch (JsonException ex)
            {
                return $"invalid JSON: {ex.Message}";
            }
        }

        /// <summary>
        /// Validates a No Version Warning <c>ModIdsToFix.xml</c> snapshot. The
        /// loader reads <c>&lt;li&gt;</c> children of the document root, so the
        /// content must be well-formed XML with a root element. An empty
        /// <c>&lt;ModIdsToFix /&gt;</c> document is valid.
        /// </summary>
        public static string? ValidateNoVersionWarningXml(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return "content is empty";
            }

            try
            {
                var doc = XDocument.Parse(content);
                if (doc.Root == null)
                {
                    return "XML has no root element";
                }

                return null;
            }
            catch (System.Xml.XmlException ex)
            {
                return $"invalid XML: {ex.Message}";
            }
        }

        /// <summary>
        /// Case-insensitive property lookup, mirroring the loaders' case-
        /// insensitive JSON deserialization.
        /// </summary>
        private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
        {
            foreach (var property in obj.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }
}
