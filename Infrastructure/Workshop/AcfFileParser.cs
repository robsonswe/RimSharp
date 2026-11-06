using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Workshop
{
    /// <summary>
    /// Parses Valve KeyValues ("ACF") text as written by the Steam client / SteamCMD
    /// (AppWorkshop section of appworkshop_294100.acf).
    ///
    /// Shape being parsed:
    /// <code>
    /// "AppState"   { ... }
    /// "AppWorkshop"
    /// {
    ///     "WorkshopItemsInstalled"
    ///     {
    ///         "818773962"  "1694112958"
    ///     }
    ///     "WorkshopItemDetails"
    ///     {
    ///         "818773962"
    ///         {
    ///             "sourceid"     "1694112958"
    ///             "timeupdated"  "1694112958"
    ///             "timetouched"  "1696540000"
    ///         }
    ///     }
    /// }
    /// </code>
    ///
    /// Grammar tolerance: quoted or bare keys/values, values on the same line as their
    /// key (Key&lt;TAB&gt;"Value") or on following lines, escaped \" and \\ inside values.
    /// Extraction is case-insensitive on section/field names; item ids keep exact case.
    /// WorkshopItemDetails\timeupdated resolves against the WorkshopItemsInstalled
    /// record when missing or zero (the installed stale-index as fallback source).
    /// </summary>
    public class AcfFileParser : IAcfFileParser
    {
        public bool TryParse(string acfFilePath, out WorkshopAcfData data, out string? error)
        {
            data = WorkshopAcfData.Empty;
            error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(acfFilePath))
                {
                    error = "No ACF file path given.";
                    return false;
                }

                if (!File.Exists(acfFilePath))
                {
                    error = $"ACF file does not exist: '{acfFilePath}'.";
                    return false;
                }

                var text = File.ReadAllText(acfFilePath);

                var index = 0;
                // Only the root object may be terminated by EOF unexpectedly.
                var root = ReadNode(text, ref index, isRoot: true, out error);
                if (root == null)
                {
                    data = WorkshopAcfData.Empty;
                    return false;
                }

                // Trailing non-whitespace content after the root object is an error —
                // a truncated/corrupt ACF should never look like a successful parse.
                SkipWhitespace(text, ref index);
                if (index < text.Length)
                {
                    error = $"Unexpected content after the ACF root object (position {index}).";
                    return false;
                }

                // The workshop manifest must actually contain the AppWorkshop section —
                // an arbitrary Document/garbage file must not parse as an empty success.
                if (GetNode(root, "AppWorkshop") == null)
                {
                    error = "The file contains no 'AppWorkshop' section — not a workshop ACF manifest.";
                    return false;
                }

                data = ExtractWorkshopData(root);
                return true;
            }
            catch (Exception ex)
            {
                data = WorkshopAcfData.Empty;
                error = $"Failed to read ACF file '{acfFilePath}': {ex.Message}";
                return false;
            }
        }

        // ------------------------------------------------------------------ //
        //  Value tokenizer                                                    //
        // ------------------------------------------------------------------ //

        /// <summary>Nodes mirror the ACF tree: a value is either a string or a nested node.</summary>
        private sealed class AcfNode : Dictionary<string, object>
        {
            public AcfNode() : base(StringComparer.OrdinalIgnoreCase)
            {
            }
        }

        private static void SkipWhitespace(string text, ref int i)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }
        }

        /// <summary>Parses one object: a sequence of key/value pairs where each value is a quoted/
        /// bare string or a nested { ... } object. Only the root node may end at EOF —
        /// nested nodes must see their closing '}' before end-of-file, or the content
        /// was truncated (a corrupt ACF must never parse as a success).</summary>
        private static AcfNode? ReadNode(string text, ref int i, bool isRoot, out string? error)
        {
            error = null;
            var node = new AcfNode();

            while (true)
            {
                SkipWhitespace(text, ref i);
                if (i >= text.Length)
                {
                    if (isRoot)
                    {
                        return node; // Root object legitimately ends at EOF
                    }

                    error = "Unexpected end of file before the closing '}' (unbalanced braces).";
                    return null;
                }

                if (text[i] == '}')
                {
                    i++; // consume close brace
                    return node;
                }

                // Key
                if (!ReadToken(text, ref i, out var key, out error))
                {
                    return null;
                }

                SkipWhitespace(text, ref i);

                // Value: nested object or scalar
                if (i < text.Length && text[i] == '{')
                {
                    i++; // consume open brace
                    var child = ReadNode(text, ref i, isRoot: false, out error);
                    if (child == null)
                    {
                        return null;
                    }
                    node[key] = child;
                }
                else
                {
                    if (!ReadToken(text, ref i, out var value, out error))
                    {
                        error = $"Missing value for key '{key}'. {error}";
                        return null;
                    }
                    node[key] = value;
                }
            }
        }

        /// <summary>Reads one token: a quoted string (handling \" and \\ escapes) or a bare word
        /// (up to whitespace / brace — tolerated for loose ACF writers).</summary>
        private static bool ReadToken(string text, ref int i, out string token, out string? error)
        {
            token = string.Empty;
            error = null;

            SkipWhitespace(text, ref i);
            if (i >= text.Length)
            {
                error = "Unexpected end of file.";
                return false;
            }

            if (text[i] == '{' || text[i] == '}')
            {
                error = $"Expected a quoted value at position {i}.";
                return false;
            }

            if (text[i] == '"')
            {
                var sb = new System.Text.StringBuilder();
                i++; // consume opening quote
                while (i < text.Length)
                {
                    var c = text[i];
                    if (c == '\\' && i + 1 < text.Length &&
                        (text[i + 1] == '"' || text[i + 1] == '\\'))
                    {
                        sb.Append(text[i + 1]);
                        i += 2;
                        continue;
                    }
                    if (c == '"')
                    {
                        i++; // consume closing quote
                        token = sb.ToString();
                        return true;
                    }
                    sb.Append(c);
                    i++;
                }

                error = $"Unterminated quoted string (started near position {i}).";
                return false;
            }

            // Bare (unquoted) token — ends at whitespace or a brace
            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}')
            {
                i++;
            }
            token = text.Substring(start, i - start);
            return true;
        }

        // ------------------------------------------------------------------ //
        //  Extraction                                                         //
        // ------------------------------------------------------------------ //

        private static WorkshopAcfData ExtractWorkshopData(AcfNode root)
        {
            var appWorkshop = GetNode(root, "AppWorkshop");

            var installedMap = ExtractLongMap(appWorkshop, "WorkshopItemsInstalled");
            var detailsNode = GetNode(appWorkshop, "WorkshopItemDetails");

            var items = new Dictionary<string, WorkshopAcfItem>(StringComparer.Ordinal);

            foreach (var (installedId, installedStamp) in installedMap)
            {
                items[installedId] = new WorkshopAcfItem
                {
                    ResolvedTimeUpdated = installedStamp
                };
            }

            if (detailsNode != null)
            {
                foreach (var (genericId, genericValue) in detailsNode)
                {
                    var itemId = genericId;
                    if (genericValue is not AcfNode details)
                    {
                        continue; // malformed details entry — keep the installed fallback
                    }

                    var timeUpdated = GetLong(details, "timeupdated");
                    var timeTouched = GetLong(details, "timetouched");
                    var sourceId = GetLong(details, "sourceid");

                    var resolved = timeUpdated != 0
                        ? timeUpdated
                        : (installedMap.TryGetValue(itemId, out var fallbackStamp) ? fallbackStamp : 0);

                    var rawValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (fieldName, fieldValue) in details)
                    {
                        if (fieldValue is string text)
                        {
                            rawValues[fieldName] = text;
                        }
                    }

                    items[itemId] = new WorkshopAcfItem
                    {
                        TimeUpdated = timeUpdated,
                        TimeTouched = timeTouched,
                        SourceId = sourceId,
                        ResolvedTimeUpdated = resolved,
                        RawValues = rawValues
                    };
                }
            }

            return new WorkshopAcfData
            {
                Items = items
            };
        }

        private static AcfNode? GetNode(AcfNode? node, string key)
        {
            if (node == null) return null;
            return node.TryGetValue(key, out var value) ? value as AcfNode : null;
        }

        private static Dictionary<string, long> ExtractLongMap(AcfNode? node, string key)
        {
            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            var map = GetNode(node, key);
            if (map == null) return result;

            foreach (var (itemKey, itemValue) in map)
            {
                if (itemValue is string text && long.TryParse(text, out var stamp))
                {
                    result[itemKey] = stamp;
                }
            }

            return result;
        }

        private static long GetLong(AcfNode node, string key)
        {
            var text = node.TryGetValue(key, out var value) ? value as string : null;
            return text != null &&
                   long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }
    }
}
