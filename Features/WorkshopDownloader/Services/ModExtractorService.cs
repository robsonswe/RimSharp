#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Wpf;
using RimSharp.Features.WorkshopDownloader.Models;
using System.Linq;

namespace RimSharp.Features.WorkshopDownloader.Services
{
    public interface IModExtractorService
    {
        Task<string?> ExtractModName();
        Task<string?> ExtractModDateInfo();
        Task<string?> ExtractFileSizeRawString();
        Task<List<CollectionItemInfo>> ExtractCollectionItemsAsync();
        Task<long> ParseFileSizeAsync(string sizeString);
        Task<List<string>> ExtractVersionTagsAsync();
        Task<string> ConvertToStandardDate(string dateString);
        Task<ModInfoDto?> ExtractFullModInfo();
        bool IsModInfoAvailable { get; }
        event EventHandler? IsModInfoAvailableChanged;
        void Reset();
    }

    public class ModExtractorService : IModExtractorService, IDisposable
    {
        private readonly Microsoft.Web.WebView2.Wpf.WebView2 _webView;
        private bool _isModNameAvailable;
        private bool _isModDateAvailable;
        private bool _lastReportedModInfoAvailableState;

        public bool IsModInfoAvailable => _isModNameAvailable && _isModDateAvailable;
        public event EventHandler? IsModInfoAvailableChanged;

        public ModExtractorService(Microsoft.Web.WebView2.Wpf.WebView2 webView)
        {
            _webView = webView;
        }

        public void Reset()
        {
            _isModNameAvailable = false;
            _isModDateAvailable = false;
            CheckAndNotifyModInfoAvailability();
        }

        private void CheckAndNotifyModInfoAvailability()
        {
            bool currentState = IsModInfoAvailable;
            if (currentState != _lastReportedModInfoAvailableState)
            {
                _lastReportedModInfoAvailableState = currentState;
                IsModInfoAvailableChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public async Task<string?> ExtractModName()
        {
            if (_webView?.CoreWebView2 == null)
            {
                _isModNameAvailable = false;
                CheckAndNotifyModInfoAvailability();
                return null;
            }

            try
            {
                string script = @"(function() {
                    const titleElement = document.querySelector('.workshopItemTitle');
                    return titleElement ? titleElement.textContent.trim() : '';
                })();";

                string result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                string name = UnwrapJsonString(result);
                _isModNameAvailable = !string.IsNullOrEmpty(name);
                CheckAndNotifyModInfoAvailability();
                return name;
            }
            catch (Exception ex)
            {
                _isModNameAvailable = false;
                CheckAndNotifyModInfoAvailability();
                Debug.WriteLine($"Error extracting mod name: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> ExtractModDateInfo()
        {
            if (_webView?.CoreWebView2 == null)
            {
                _isModDateAvailable = false;
                CheckAndNotifyModInfoAvailability();
                return null;
            }

            try
            {
                string script = @"(function() {
                    const labels = document.querySelectorAll('.detailsStatsContainerLeft .detailsStatLeft');
                    const values = document.querySelectorAll('.detailsStatsContainerRight .detailsStatRight');
                    let updatedDate = '';
                    let postedDate = '';

                    for (let i = 0; i < labels.length; i++) {
                        const labelText = labels[i].textContent.trim();
                        if (labelText === 'Updated') {
                            updatedDate = values[i].textContent.trim();
                        } else if (labelText === 'Posted') {
                            postedDate = values[i].textContent.trim();
                        }
                    }

                    // Prioritize 'Updated' date. If not found, use 'Posted' date.
                    return updatedDate || postedDate;
                })();";

                string result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                string date = UnwrapJsonString(result);
                _isModDateAvailable = !string.IsNullOrEmpty(date);
                CheckAndNotifyModInfoAvailability();
                return date;
            }
            catch (Exception ex)
            {
                _isModDateAvailable = false;
                CheckAndNotifyModInfoAvailability();
                Debug.WriteLine($"Error extracting mod date info: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> ExtractFileSizeRawString()
        {
            if (_webView?.CoreWebView2 == null) return null;

            try
            {
                string script = @"(function() {
                    const labels = document.querySelectorAll('.detailsStatsContainerLeft .detailsStatLeft');
                    const values = document.querySelectorAll('.detailsStatsContainerRight .detailsStatRight');
                    for (let i = 0; i < labels.length; i++) {
                        if (labels[i].textContent.trim() === 'File Size') {
                            return values[i].textContent.trim();
                        }
                    }
                    return '';
                })();";

                string result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                return UnwrapJsonString(result);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error extracting file size string: {ex.Message}");
                return null;
            }
        }

        public async Task<List<string>> ExtractVersionTagsAsync()
        {
            if (_webView?.CoreWebView2 == null) return new List<string>();

            try
            {
                string script = @"(function() {
                    const detailsBlock = document.querySelector('.rightDetailsBlock');
                    if (!detailsBlock) return '[]';
                    const tagElements = detailsBlock.querySelectorAll('a');
                    const tags = Array.from(tagElements).map(a => a.textContent.trim());
                    return JSON.stringify(tags);
                })();";

                string jsonResult = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                string unwrappedJson = UnwrapJsonString(jsonResult);

                if (string.IsNullOrWhiteSpace(unwrappedJson))
                {
                    return new List<string>();
                }

                var tags = JsonSerializer.Deserialize<List<string>>(unwrappedJson) ?? new List<string>();

                var versionTags = tags
                    .Where(t => !string.IsNullOrWhiteSpace(t) && Version.TryParse(t, out _))
                    .ToList();

                versionTags.Sort((v1, v2) => new Version(v1).CompareTo(new Version(v2)));

                return versionTags;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error extracting version tags from page: {ex.Message}");
                return new List<string>();
            }
        }

        public Task<long> ParseFileSizeAsync(string sizeString)
        {
            if (string.IsNullOrWhiteSpace(sizeString))
            {
                return Task.FromResult(0L);
            }

            long fileSizeInBytes = 0;
            try
            {
                sizeString = sizeString.Trim();
                var match = Regex.Match(sizeString, @"^([\d.,]+)\s*([a-zA-Z]*)", RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    string numberPart = match.Groups[1].Value.Replace(",", "");
                    string unitPart = match.Groups[2].Value.Trim().ToUpperInvariant();

                    if (double.TryParse(numberPart, NumberStyles.Any, CultureInfo.InvariantCulture, out double sizeValue))
                    {
                        switch (unitPart)
                        {
                            case "GB":
                                fileSizeInBytes = (long)(sizeValue * 1024 * 1024 * 1024);
                                break;
                            case "MB":
                                fileSizeInBytes = (long)(sizeValue * 1024 * 1024);
                                break;
                            case "KB":
                                fileSizeInBytes = (long)(sizeValue * 1024);
                                break;
                            case "B":
                            case "":
                                fileSizeInBytes = (long)sizeValue;
                                break;
                            default:
                                fileSizeInBytes = 0;
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ParseFileSizeAsync] Error parsing '{sizeString}': {ex.Message}");
                fileSizeInBytes = 0;
            }

            return Task.FromResult(fileSizeInBytes);
        }

        public Task<string> ConvertToStandardDate(string dateString)
        {
            if (string.IsNullOrWhiteSpace(dateString))
            {
                return Task.FromResult(string.Empty);
            }

            try
            {
                // STEP 1: Normalize the string to handle scraping inconsistencies.
                string cleanedDateString = dateString.Trim();
                cleanedDateString = Regex.Replace(cleanedDateString, @"\s+", " "); // Collapse multiple spaces
                cleanedDateString = cleanedDateString.Replace(" ,", ",");         // Fix space before comma

                // STEP 2: Handle missing year by intelligently injecting the current year.
                if (!Regex.IsMatch(cleanedDateString, @"\b\d{4}\b"))
                {
                    // This handles cases like "16 Jul @ 12:36am"
                    var parts = cleanedDateString.Split(new[] { " @ " }, StringSplitOptions.None);
                    if (parts.Length == 2)
                    {
                        // Reconstruct the string with the year added.
                        // E.g., "16 Jul" + ", 2025" + " @ 12:36am"
                        cleanedDateString = $"{parts[0].Trim()}, {DateTime.Now.Year} @ {parts[1].Trim()}";
                    }
                }

                // STEP 3: Parse the date string.
                // We try multiple cultures and methods because Steam localizes dates.
                DateTime parsedDate;
                bool parsed = DateTime.TryParse(cleanedDateString, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsedDate) ||
                              DateTime.TryParse(cleanedDateString, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate);

                if (!parsed)
                {
                    // Fallback to exact formats for English dates if generic parsing fails.
                    var formats = new[]
                    {
                        "d MMM, yyyy @ h:mmtt", "d MMMM, yyyy @ h:mmtt", "d MMM yyyy @ h:mmtt",
                        "d MMMM yyyy @ h:mmtt", "d MMM, yyyy", "d MMMM, yyyy", "d MMM yyyy", "d MMMM yyyy"
                    };
                    parsed = DateTime.TryParseExact(cleanedDateString, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate);
                }


                if (parsed)
                {
                    // STEP 4: Steam's web interface shows the date/time in the user's local timezone
                    // So we treat the parsed date as already being in local time
                    var localDateTime = DateTime.SpecifyKind(parsedDate, DateTimeKind.Local);

                    // Convert to UTC to match the Steam API format
                    var utcDateTime = localDateTime.ToUniversalTime();

                    // Debug logging to verify conversion
                    Debug.WriteLine($"[ConvertToStandardDate] Original: '{dateString}' -> Cleaned: '{cleanedDateString}'");
                    Debug.WriteLine($"[ConvertToStandardDate] Parsed as local: {localDateTime:yyyy-MM-dd HH:mm:ss}");
                    Debug.WriteLine($"[ConvertToStandardDate] Converted to UTC: {utcDateTime:yyyy-MM-dd HH:mm:ss}");
                    Debug.WriteLine($"[ConvertToStandardDate] Local timezone offset: {TimeZoneInfo.Local.GetUtcOffset(localDateTime)}");

                    // Return in the same format as before (UTC time in dd/MM/yyyy HH:mm:ss format)
                    return Task.FromResult(utcDateTime.ToString("dd/MM/yyyy HH:mm:ss"));
                }

                Debug.WriteLine($"[ConvertToStandardDate] All parsing attempts failed for cleaned string: '{cleanedDateString}' (Original: '{dateString}')");
                return Task.FromResult(dateString);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConvertToStandardDate] Error parsing date '{dateString}': {ex.Message}");
                return Task.FromResult(dateString);
            }
        }
        public async Task<ModInfoDto?> ExtractFullModInfo()
        {
            if (_webView?.Source == null || _webView.CoreWebView2 == null) return null;

            var url = _webView.Source.ToString();

            // Step 1: Safely try to create the Uri and ensure it's not null.
            // 'out var uri' infers uri as a nullable Uri (Uri?)
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri is null)
            {
                Debug.WriteLine($"URL is not a valid URI: {url}");
                return null;
            }

            // Step 2: Now that we know 'uri' is not null, validate its properties.
            if (!(uri.Host.EndsWith("steamcommunity.com") &&
                  (uri.AbsolutePath.Contains("/sharedfiles/filedetails/") || uri.AbsolutePath.Contains("/workshop/filedetails/"))))
            {
                Debug.WriteLine($"URL is not a valid Steam Workshop item page: {url}");
                return null;
            }

            var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var id = queryParams["id"];
            if (string.IsNullOrEmpty(id))
            {
                Debug.WriteLine($"Could not extract 'id' from URL: {url}");
                return null;
            }

            string? modName = await ExtractModName();
            string? dateInfo = await ExtractModDateInfo();
            string standardDate = await ConvertToStandardDate(dateInfo ?? string.Empty);
            string? fileSizeRaw = await ExtractFileSizeRawString();
            long fileSize = await ParseFileSizeAsync(fileSizeRaw ?? string.Empty);
            List<string> latestVersions = await ExtractVersionTagsAsync();

            // Ensure modName is not null before assigning it to the DTO
            if (string.IsNullOrEmpty(modName))
            {
                modName = $"Mod {id}";
            }

            string finalPublishDate = dateInfo ?? string.Empty;
            if (DateTime.TryParseExact(standardDate, "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utcDateTime))
            {
                finalPublishDate = utcDateTime.ToString("d MMM, yyyy @ h:mmtt", CultureInfo.InvariantCulture);
            }

            return new ModInfoDto
            {
                Name = modName,
                Url = url,
                SteamId = id,
                PublishDate = finalPublishDate,
                StandardDate = standardDate,
                FileSize = fileSize,
                LatestVersions = latestVersions
            };
        }

        public void Dispose()
        {
            // In case you need to unsubscribe from any events from the WebView2 instance
            Debug.WriteLine("[ModExtractorService] Disposed.");
        }

        private string UnwrapJsonString(string? jsonValue)
        {
            if (string.IsNullOrEmpty(jsonValue) || jsonValue == "null")
                return string.Empty;

            try
            {
                return JsonSerializer.Deserialize<string>(jsonValue) ?? string.Empty;
            }
            catch (JsonException)
            {
                if (jsonValue.StartsWith("\"") && jsonValue.EndsWith("\"") && jsonValue.Length >= 2)
                {
                    return jsonValue.Substring(1, jsonValue.Length - 2)
                                  .Replace("\\\"", "\"")
                                  .Replace("\\\\", "\\")
                                  .Replace("\\n", "\n")
                                  .Replace("\\r", "\r")
                                  .Replace("\\t", "\t");
                }
                return jsonValue;
            }
        }

        public async Task<List<CollectionItemInfo>> ExtractCollectionItemsAsync()
        {
            var items = new List<CollectionItemInfo>();
            if (_webView?.CoreWebView2 == null) return items;

            try
            {
                string script = @"(function() {
                    const collectionItems = document.querySelectorAll('.collectionItem');
                    const results = [];
                    const idRegex = /sharedfile_(\d+)/;
                    collectionItems.forEach(item => {
                        const idMatch = item.id ? item.id.match(idRegex) : null;
                        const steamId = idMatch ? idMatch[1] : null;
                        const titleElement = item.querySelector('.collectionItemDetails .workshopItemTitle');
                        const name = titleElement ? titleElement.textContent.trim() : 'Unknown Name';
                        const authorElement = item.querySelector('.collectionItemDetails .workshopItemAuthorName a');
                        const author = authorElement ? authorElement.textContent.trim() : 'Unknown Author';

                        if (steamId) {
                            results.push({ steamId: steamId, name: name, author: author });
                        }
                    });
                    return JSON.stringify(results);
                })();";

                string? jsonResult = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                if (string.IsNullOrEmpty(jsonResult) || jsonResult == "null" || jsonResult == "\"[]\"")
                {
                    return items;
                }
                string unwrappedJson = UnwrapJsonString(jsonResult);
                if (string.IsNullOrWhiteSpace(unwrappedJson))
                {
                    return items;
                }
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var extractedData = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(unwrappedJson, options);

                if (extractedData != null)
                {
                    foreach (var data in extractedData)
                    {
                        items.Add(new CollectionItemInfo
                        {
                            SteamId = data.GetValueOrDefault("steamId", string.Empty),
                            Name = data.GetValueOrDefault("name", "Unknown Name"),
                            Author = data.GetValueOrDefault("author", "Unknown Author")
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModExtractorService] Error extracting collection items: {ex.Message}");
            }
            return items;
        }
    }
}