#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.WorkshopDownloader.Services
{
    /// <summary>
    /// Fetches the full Workshop description for installed mods.
    ///
    /// Two extractors, tried in order:
    ///   1. Steam Web API (ISteamRemoteStorage/GetPublishedFileDetails) —
    ///      structured, reliable, cached description included.
    ///   2. HTML fallback — scrapes the Workshop item page's
    ///      highlightContent div when the API turns up empty.
    ///
    /// The cleaned plain-text description is persisted as RimSharp.json
    /// inside the mod folder (next to About.xml) so it is available on every
    /// subsequent load without re-fetching.
    /// </summary>
    public class WorkshopDescriptionService : IWorkshopDescriptionService
    {
        private const string WorkshopItemUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=";

        private readonly ISteamApiClient _steamApiClient;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILoggerService _logger;

        public WorkshopDescriptionService(
            ISteamApiClient steamApiClient,
            IHttpClientFactory httpClientFactory,
            ILoggerService logger)
        {
            _steamApiClient = steamApiClient ?? throw new ArgumentNullException(nameof(steamApiClient));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> SaveCacheAsync(string steamId, string modFolderPath, string plainText, string source, string? steamName = null, CancellationToken ct = default, List<string>? tags = null)
        {
            if (string.IsNullOrWhiteSpace(steamId) || string.IsNullOrWhiteSpace(modFolderPath) ||
                string.IsNullOrWhiteSpace(plainText))
            {
                return false;
            }
            if (!Directory.Exists(modFolderPath))
            {
                _logger.LogWarning($"Workshop description: mod folder '{modFolderPath}' does not exist.", nameof(WorkshopDescriptionService));
                return false;
            }

            var record = new WorkshopDescriptionRecord
            {
                SteamId = steamId,
                SteamDescription = plainText,
                SteamName = steamName ?? string.Empty,
                Tags = FilterRetainedWorkshopTags(tags),
                Source = source,
                FetchedUtc = DateTimeOffset.UtcNow
            };

            try
            {
                var json = JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true });
                var cachePath = Path.Combine(modFolderPath, WorkshopDescriptionCache.FileName);
                await File.WriteAllTextAsync(cachePath, json, ct).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Non-fatal: the description is still returned to the caller
                // even if the cache write fails (e.g. locked folder).
                _logger.LogWarning($"Workshop description: cache write failed for {steamId}: {ex.Message}", nameof(WorkshopDescriptionService));
                return false;
            }
        }

        public async Task<string?> EnrichModAsync(string steamId, string modFolderPath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(steamId) || string.IsNullOrWhiteSpace(modFolderPath))
            {
                return null;
            }
            if (!Directory.Exists(modFolderPath))
            {
                _logger.LogWarning($"Workshop description: mod folder '{modFolderPath}' does not exist.", nameof(WorkshopDescriptionService));
                return null;
            }

            (string Description, string Source, string? Title, List<string> Tags)? extraction =
                await ExtractViaSteamApiAsync(steamId, ct).ConfigureAwait(false)
                ?? await ExtractViaHtmlAsync(steamId, ct).ConfigureAwait(false);

            if (extraction is null || string.IsNullOrWhiteSpace(extraction.Value.Description))
            {
                _logger.LogWarning($"Workshop description: no description obtainable for {steamId} ({extraction?.Source ?? "both extractors empty"}).", nameof(WorkshopDescriptionService));
                return null;
            }

            await SaveCacheAsync(steamId, modFolderPath, extraction.Value.Description, extraction.Value.Source, extraction.Value.Title, ct, extraction.Value.Tags).ConfigureAwait(false);
            return extraction.Value.Description;
        }

        /// <summary>Extractor 1 — Steam Web API (structured, preferred).</summary>
        private async Task<(string Description, string Source, string? Title, List<string> Tags)?> ExtractViaSteamApiAsync(string steamId, CancellationToken ct)
        {
            try
            {
                var response = await _steamApiClient.GetFileDetailsAsync(steamId, ct).ConfigureAwait(false);
                var detail = response?.Response?.PublishedFileDetails?
                    .FirstOrDefault(d => d.Result == 1);
                if (detail == null) return null;

                // Prefer the long text; some payloads only fill one of the two.
                var raw = detail.Description?.Length >= detail.DetailedDescription?.Length
                    ? detail.Description
                    : detail.DetailedDescription;
                if (string.IsNullOrWhiteSpace(raw)) return null;

                var tags = FilterRetainedWorkshopTags(detail.Tags.Select(t => t.Tag));
                return (CleanHtml(raw), "steamapi", detail.Title, tags);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning($"Workshop description: Steam API extractor failed for {steamId}: {ex.Message}", nameof(WorkshopDescriptionService));
                return null;
            }
        }

        /// <summary>Extractor 2 — Workshop item page HTML scrape (fallback).</summary>
        private async Task<(string Description, string Source, string? Title, List<string> Tags)?> ExtractViaHtmlAsync(string steamId, CancellationToken ct)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("WorkshopHtml");
                using var request = new HttpRequestMessage(HttpMethod.Get, WorkshopItemUrl + steamId);
                using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var raw = ExtractHighlightContent(html);
                var title = ExtractWorkshopItemTitle(html);
                if (string.IsNullOrWhiteSpace(raw) || raw.Length < 40) return null;

                var tags = FilterRetainedWorkshopTags(ExtractWorkshopTagValuesFromHtml(html));
                return (CleanHtml(raw), "html", title, tags);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning($"Workshop description: HTML extractor failed for {steamId}: {ex.Message}", nameof(WorkshopDescriptionService));
                return null;
            }
        }

        /// <summary>
        /// The only Workshop tags worth persisting. Version-like tags ("1.6",
        /// "1.5", …) are pure noise for RimSharp's purposes, so everything else
        /// the Steam API / HTML page reports is discarded.
        /// </summary>
        private static readonly HashSet<string> RetainedWorkshopTags =
            new(StringComparer.OrdinalIgnoreCase) { "Translation", "Scenario" };

        /// <summary>
        /// Workshop item pages link each tag as an anchor whose href carries
        /// <c>requiredtags[]=Tag</c>. The brackets are URL-encoded
        /// (<c>%5B%5D</c>) on the live page and params are joined with
        /// <c>&</c>; the capture stops at the next <c>&</c>/quote/angle
        /// bracket so both forms are handled by one pattern.
        /// </summary>
        private static readonly Regex WorkshopTagHrefRegex = new Regex(
            @"requiredtags(?:%5B%5D|\[\])=([^&""'<>]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Extracts the raw tag values from a Workshop item page's tag anchors
        /// (the <c>requiredtags[]=…</c> href params), URL-decoding each capture.
        /// No filtering is applied here — see <see cref="FilterRetainedWorkshopTags"/>.
        /// </summary>
        internal static IEnumerable<string> ExtractWorkshopTagValuesFromHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) yield break;

            foreach (Match match in WorkshopTagHrefRegex.Matches(html))
            {
                var value = match.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(value)) continue;
                yield return WebUtility.UrlDecode(value);
            }
        }

        /// <summary>
        /// Keeps only the retained tags ("Translation"/"Scenario", matched
        /// case-insensitively), trimming and de-duplicating them while
        /// preserving first-seen order and original casing. Null/empty input
        /// yields an empty list.
        /// </summary>
        internal static List<string> FilterRetainedWorkshopTags(IEnumerable<string?>? tags)
        {
            var result = new List<string>();
            if (tags is null) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                var trimmed = tag.Trim();
                if (RetainedWorkshopTags.Contains(trimmed) && seen.Add(trimmed))
                {
                    result.Add(trimmed);
                }
            }
            return result;
        }

        private static string? ExtractWorkshopItemTitle(string html)
        {
            const string Marker = "workshopItemTitle";
            var markerIndex = html.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0) return null;
            var contentStart = html.IndexOf('>', markerIndex) + 1;
            if (contentStart < 1) return null;
            contentStart++;
            var close = html.IndexOf("</div>", contentStart, StringComparison.OrdinalIgnoreCase);
            if (close < 0) return null;
            return html.Substring(contentStart, close - contentStart).Trim();
        }

        /// <summary>
        /// Captures the inner HTML of the highlightContent div (the Workshop
        /// description), handling nested divs with a balanced scan.
        /// </summary>
        private static string? ExtractHighlightContent(string html)
        {
            const string Marker = "id=\"highlightContent\"";
            var markerIndex = html.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0) return null;

            var contentStart = html.IndexOf('>', markerIndex);
            if (contentStart < 0) return null;
            contentStart++;

            var depth = 1;
            var cursor = contentStart;
            var contentEnd = -1;
            while (cursor < html.Length && depth > 0)
            {
                var open = html.IndexOf("<div", cursor, StringComparison.OrdinalIgnoreCase);
                var close = html.IndexOf("</div", cursor, StringComparison.OrdinalIgnoreCase);
                if (close < 0) break;

                if (open >= 0 && open < close)
                {
                    depth++;
                    cursor = open + 4;
                }
                else
                {
                    depth--;
                    contentEnd = close;
                    cursor = close + 5;
                }
            }

            if (depth != 0 || contentEnd <= contentStart) return null;
            return html.Substring(contentStart, contentEnd - contentStart);
        }

        /// <summary>Strips markup to readable plain text (BR → newline, tags out, entities decoded).</summary>
        public static string CleanHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            var s = Regex.Replace(html, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "</(p|div|li|h\\d)>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "<[^>]+>", string.Empty);
            s = WebUtility.HtmlDecode(s);
            s = Regex.Replace(s, "[ \\t]+", " ");
            s = Regex.Replace(s, "\\n\\s*\\n\\s*\\n+", "\n\n");
            return s.Trim();
        }
    }
}
