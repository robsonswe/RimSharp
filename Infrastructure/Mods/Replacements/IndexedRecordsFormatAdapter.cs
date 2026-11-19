#nullable enable
using System;
using System.Text.Json;

namespace RimSharp.Infrastructure.Mods.Replacements
{
    /// <summary>
    /// Format adapter for index-driven replacement records. Parses serde's
    /// externally-tagged enums (a unit variant is a plain string, a payload
    /// variant is a 1-key object) and extracts curated UseAlternative notices.
    ///
    /// Corpus note: the dataset carries 2,861 UseAlternative notices, but 2,853
    /// are sourced from the upstream database (all certainty=Inapplicable, reason
    /// null) and contribute NOTHING. Only the 8 curated JumliDataset records carry
    /// reasons, so only notices that are BOTH UseAlternative AND JumliDataset are
    /// returned.
    /// </summary>
    public static class IndexedRecordsFormatAdapter
    {
        private const string UseAlternativeTag = "UseAlternative";
        private const string CuratedSourceTag = "JumliDataset";

        public sealed class Alternative
        {
            public string? TargetName { get; init; }
            public string? TargetWorkshopId { get; init; }
            public string? Reason { get; init; }
        }

        public sealed class Record
        {
            public string? PackageId { get; init; }
            public string? WorkshopId { get; init; }
            public Alternative? CuratedAlternative { get; init; }
        }

        public static Record? ParseRecord(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string? packageId = null;
            string? workshopId = null;
            if (root.TryGetProperty("identifiers", out var identifiers) && identifiers.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in identifiers.EnumerateArray())
                {
                    if (id.ValueKind != JsonValueKind.Object) continue;
                    foreach (var prop in id.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, "PackageId", StringComparison.Ordinal) &&
                            prop.Value.ValueKind == JsonValueKind.String)
                        {
                            packageId = prop.Value.GetString();
                        }
                        else if (string.Equals(prop.Name, "WorkshopId", StringComparison.Ordinal))
                        {
                            workshopId = prop.Value.ValueKind switch
                            {
                                JsonValueKind.Number => prop.Value.GetRawText(),
                                JsonValueKind.String => prop.Value.GetString(),
                                _ => workshopId
                            };
                        }
                    }
                }
            }

            Alternative? alternative = null;
            if (root.TryGetProperty("notices", out var notices) && notices.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in notices.EnumerateArray())
                {
                    if (!entry.TryGetProperty("source", out var sourceEl) || !IsCuratedSource(sourceEl)) continue;
                    if (!entry.TryGetProperty("notice", out var noticeEl)) continue;
                    if (TryReadUseAlternative(noticeEl, out var alt))
                    {
                        alternative = alt;
                        break;
                    }
                }
            }

            return new Record
            {
                PackageId = packageId,
                WorkshopId = workshopId,
                CuratedAlternative = alternative
            };
        }

        private static bool IsCuratedSource(JsonElement sourceEl)
        {
            if (sourceEl.ValueKind == JsonValueKind.String)
            {
                return string.Equals(sourceEl.GetString(), CuratedSourceTag, StringComparison.Ordinal);
            }
            if (sourceEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in sourceEl.EnumerateObject())
                {
                    return string.Equals(prop.Name, CuratedSourceTag, StringComparison.Ordinal);
                }
            }
            return false;
        }

        private static bool TryReadUseAlternative(JsonElement noticeEl, out Alternative? alternative)
        {
            alternative = null;
            if (noticeEl.ValueKind != JsonValueKind.Object) return false; // unit variant — no payload

            foreach (var prop in noticeEl.EnumerateObject())
            {
                if (!string.Equals(prop.Name, UseAlternativeTag, StringComparison.Ordinal)) return false;
                if (prop.Value.ValueKind != JsonValueKind.Array) return false;

                string? name = null;
                string? targetId = null;
                string? reason = null;
                var index = 0;
                foreach (var item in prop.Value.EnumerateArray())
                {
                    if (index == 0 && item.ValueKind == JsonValueKind.String)
                    {
                        name = item.GetString();
                    }
                    else if (index == 1)
                    {
                        targetId = item.ValueKind switch
                        {
                            JsonValueKind.Number => item.GetRawText(),
                            JsonValueKind.String => item.GetString(),
                            _ => null
                        };
                    }
                    else if (index == 2 && item.ValueKind == JsonValueKind.String)
                    {
                        reason = item.GetString();
                    }
                    index++;
                }

                alternative = new Alternative
                {
                    TargetName = name,
                    TargetWorkshopId = targetId,
                    Reason = reason
                };
                return true;
            }
            return false;
        }
    }
}
