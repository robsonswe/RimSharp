using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
// Use the WPF version of WebView2
using Microsoft.Web.WebView2.Wpf; // <<< CHANGED
using RimSharp.Features.WorkshopDownloader.Models;

namespace RimSharp.Features.WorkshopDownloader.Services
{
    // Interface remains the same - EXCEPT ADDING THE PROPERTY
    public interface IModExtractorService
    {
        Task<string> ExtractModName();
        Task<string> ExtractModDateInfo();
        Task<string> ExtractFileSizeRawString(); // Added
        Task<List<CollectionItemInfo>> ExtractCollectionItemsAsync();
        Task<long> ParseFileSizeAsync(string sizeString); // Added

        Task<string> ConvertToStandardDate(string dateString);
        Task<ModInfoDto> ExtractFullModInfo();
        bool IsModInfoAvailable { get; } // <<< ADDED
        event EventHandler IsModInfoAvailableChanged; // <<< ADDED EVENT
    }

    public class ModExtractorService : IModExtractorService
    {
        // Use the WPF WebView2 type
        private readonly Microsoft.Web.WebView2.Wpf.WebView2 _webView; // <<< CHANGED

        // Keep internal state private, expose composite state via interface
        private bool _isModNameAvailable;
        private bool _isModDateAvailable;
        private bool _lastReportedModInfoAvailableState;

        // Implement the interface property
        public bool IsModInfoAvailable => _isModNameAvailable && _isModDateAvailable;
        public event EventHandler IsModInfoAvailableChanged; // <<< ADDED EVENT


        // You could make the component properties internal or private if only used here
        // public bool IsModNameAvailable => _isModNameAvailable;
        // public bool IsModDateAvailable => _isModDateAvailable;


        // Accept the WPF WebView2 type
        public ModExtractorService(Microsoft.Web.WebView2.Wpf.WebView2 webView) // <<< CHANGED
        {
            _webView = webView;
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


        public async Task<string> ExtractModName()
        {
            if (_webView?.CoreWebView2 == null)
            {
                _isModNameAvailable = false; // <<< Update internal state
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
                _isModNameAvailable = !string.IsNullOrEmpty(name); // <<< Update internal state
                CheckAndNotifyModInfoAvailability(); // <<< Notify on change

                return name;
            }
            catch (Exception ex)
            {
                _isModNameAvailable = false; // <<< Update internal state
                CheckAndNotifyModInfoAvailability(); // <<< Notify on change

                Console.WriteLine($"Error extracting mod name: {ex.Message}");
                return null;
            }
        }


        public async Task<string> ExtractModDateInfo()
        {
            if (_webView?.CoreWebView2 == null)
            {
                _isModDateAvailable = false; // <<< Update internal state
                CheckAndNotifyModInfoAvailability(); // <<< Notify on change

                return null;
            }

            try
            {
                string script = @"(function() {
                const detailsContainer = document.querySelector('.detailsStatsContainerRight');
                if (!detailsContainer) return '';
                const dateElements = detailsContainer.querySelectorAll('.detailsStatRight');
                if (dateElements.length > 0) {
                    let dateString = dateElements[dateElements.length - 1].textContent.trim();
                    dateString = dateString.replace(/\s+/g, ' ');
                    if (!dateString.includes(',')) {
                        dateString = dateString.replace(/(\d+ [A-Za-z]+) (\d{4})/, '$1, $2');
                    }
                    if (!/\d{4}/.test(dateString) && dateString.includes('@')) {
                        const currentYear = new Date().getFullYear();
                        dateString = dateString.replace(' @', `, ${currentYear} @`);
                    }
                    return dateString;
                }
                return '';
            })();";

                string result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                string date = UnwrapJsonString(result);
                _isModDateAvailable = !string.IsNullOrEmpty(date); // <<< Update internal state
                CheckAndNotifyModInfoAvailability(); // <<< Notify on change

                return date;
            }
            catch (Exception ex)
            {
                _isModDateAvailable = false; // <<< Update internal state
                CheckAndNotifyModInfoAvailability(); // <<< Notify on change

                Console.WriteLine($"Error extracting mod date info: {ex.Message}");
                return null;
            }
        }
        public async Task<string> ExtractFileSizeRawString() // Added
        {
            if (_webView?.CoreWebView2 == null) return null;

            try
            {
                // Script targets the FIRST element, which should be the file size
                string script = @"(function() {
                    const detailsContainer = document.querySelector('.detailsStatsContainerRight');
                    if (!detailsContainer) return '';
                    const statElements = detailsContainer.querySelectorAll('.detailsStatRight');
                    if (statElements.length > 0) {
                        return statElements[0].textContent.trim(); // Get the first element
                    }
                    return ''; // Return empty if no elements
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

        public Task<long> ParseFileSizeAsync(string sizeString) // Added
        {
            if (string.IsNullOrWhiteSpace(sizeString))
            {
                return Task.FromResult(0L); // Return 0 if input is empty
            }

            long fileSizeInBytes = 0;
            try
            {
                // Normalize string: remove extra spaces, use invariant culture for decimals
                sizeString = sizeString.Trim();
                var match = Regex.Match(sizeString, @"^([\d.,]+)\s*([a-zA-Z]*)", RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    string numberPart = match.Groups[1].Value.Replace(",", ""); // Remove thousands separators if any
                    string unitPart = match.Groups[2].Value.Trim().ToUpperInvariant();

                    // Use InvariantCulture to handle '.' as decimal separator regardless of system locale
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
                            case "": // Assume bytes if no unit
                                fileSizeInBytes = (long)sizeValue;
                                break;
                            default:
                                Debug.WriteLine($"[ParseFileSizeAsync] Unknown unit: '{unitPart}' in string '{sizeString}'");
                                fileSizeInBytes = 0; // Unknown unit
                                break;
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[ParseFileSizeAsync] Could not parse number part: '{numberPart}' from string '{sizeString}'");
                    }
                }
                else
                {
                    Debug.WriteLine($"[ParseFileSizeAsync] Could not match pattern in string: '{sizeString}'");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ParseFileSizeAsync] Error parsing '{sizeString}': {ex.Message}");
                fileSizeInBytes = 0; // Return 0 on error
            }

            return Task.FromResult(fileSizeInBytes);
        }


        // No changes needed in ConvertToStandardDate, ExtractFullModInfo, or UnwrapJsonString
        // as they don't directly use the IsMod...Available properties.

        public Task<string> ConvertToStandardDate(string dateString)
        {
            if (string.IsNullOrWhiteSpace(dateString))
            {
                Console.WriteLine("[ConvertToStandardDate] Input date string was null or empty");
                return Task.FromResult(string.Empty);
            }

            Console.WriteLine($"[ConvertToStandardDate] Starting conversion for: '{dateString}'");

            try
            {
                // Ensure the date string has a comma after the month
                if (!dateString.Contains(",") && Regex.IsMatch(dateString, @"\d+ [A-Za-z]+ \d{4}"))
                {
                    dateString = Regex.Replace(dateString, @"(\d+ [A-Za-z]+) (\d{4})", "$1, $2");
                    Console.WriteLine($"[ConvertToStandardDate] Added comma to date string: '{dateString}'");
                }

                // Ensure year exists - if not, add current year (as fallback)
                if (!Regex.IsMatch(dateString, @"\b\d{4}\b") && dateString.Contains("@"))
                {
                    dateString = dateString.Insert(dateString.IndexOf('@'), $", {DateTime.Now.Year}");
                    Console.WriteLine($"[ConvertToStandardDate] Added current year to string: '{dateString}'");
                }

                // Normalize the string by removing extra spaces around @
                dateString = Regex.Replace(dateString, @"\s+@\s+", " @ ");

                // Try parsing with different formats (comma formats first)
                var formats = new[]
                {
            "d MMM, yyyy @ h:mmtt",    // 7 Apr, 2025 @ 2:17am
            "d MMMM, yyyy @ h:mmtt",   // 7 April, 2025 @ 2:17am
            "d MMM yyyy @ h:mmtt",     // Fallback without comma
            "d MMMM yyyy @ h:mmtt",    // Fallback without comma
            "d MMM, yyyy",             // Fallback without time
            "d MMMM, yyyy",            // Fallback without time
            "d MMM yyyy",              // Fallback without comma or time
            "d MMMM yyyy"             // Fallback without comma or time
        };

                if (DateTime.TryParseExact(dateString, formats,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date))
                {
                    var result = date.ToString("dd/MM/yyyy HH:mm:ss");
                    Console.WriteLine($"[ConvertToStandardDate] Successfully converted to: '{result}'");
                    return Task.FromResult(result);
                }

                Console.WriteLine($"[ConvertToStandardDate] All parsing attempts failed, trying DateTime.Parse");
                if (DateTime.TryParse(dateString, out var fallbackDate))
                {
                    var result = fallbackDate.ToString("dd/MM/yyyy HH:mm:ss");
                    Console.WriteLine($"[ConvertToStandardDate] Fallback parse succeeded: '{result}'");
                    return Task.FromResult(result);
                }

                Console.WriteLine($"[ConvertToStandardDate] All parsing methods failed");
                return Task.FromResult(dateString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConvertToStandardDate] Error: {ex.Message}");
                return Task.FromResult(dateString);
            }
        }

        public async Task<ModInfoDto> ExtractFullModInfo()
        {
            if (_webView?.Source == null || _webView.CoreWebView2 == null) return null;

            var url = _webView.Source.ToString();
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                !(uri.Host.EndsWith("steamcommunity.com") && uri.AbsolutePath.Contains("/sharedfiles/filedetails/")))
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

            string modName = await ExtractModName();
            string dateInfo = await ExtractModDateInfo();
            string standardDate = await ConvertToStandardDate(dateInfo);
            string fileSizeRaw = await ExtractFileSizeRawString(); // Added
            long fileSize = await ParseFileSizeAsync(fileSizeRaw); // Added

            if (string.IsNullOrEmpty(modName)) modName = $"Mod {id}";
            if (string.IsNullOrEmpty(dateInfo)) dateInfo = "Unknown Date";
            if (string.IsNullOrEmpty(standardDate)) standardDate = dateInfo;


            return new ModInfoDto
            {
                Name = modName,
                Url = url,
                SteamId = id,
                PublishDate = dateInfo,
                StandardDate = standardDate,
                FileSize = fileSize // Added
            };
        }


        private string UnwrapJsonString(string jsonValue)
        {
             // (Implementation remains the same)
            if (string.IsNullOrEmpty(jsonValue) || jsonValue == "null")
                return string.Empty;

            try
            {
                // Try standard JSON deserialization first
                return JsonSerializer.Deserialize<string>(jsonValue) ?? string.Empty;
            }
            catch (JsonException)
            {
                // Handle cases where ExecuteScriptAsync might return a raw string
                // that looks like JSON ("\"Some Text\"") but isn't quite right,
                // or just a plain string if the script returns one directly.
                if (jsonValue.StartsWith("\"") && jsonValue.EndsWith("\"") && jsonValue.Length >= 2)
                {
                    // Manually unescape common sequences if needed
                    return jsonValue.Substring(1, jsonValue.Length - 2)
                                  .Replace("\\\"", "\"")
                                  .Replace("\\\\", "\\")
                                  .Replace("\\n", "\n")
                                  .Replace("\\r", "\r")
                                  .Replace("\\t", "\t");
                    // Add more escapes if necessary
                }
                // If it wasn't wrapped in quotes or deserialization failed, return as is
                return jsonValue;
            }
        }
                public async Task<List<CollectionItemInfo>> ExtractCollectionItemsAsync()
        {
            var items = new List<CollectionItemInfo>();
            if (_webView?.CoreWebView2 == null)
            {
                Debug.WriteLine("[ModExtractorService] WebView not ready for collection extraction.");
                return items; // Return empty list
            }

            try
            {
                // JavaScript to extract IDs, Names, and Authors from collection items
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
                        } else {
                             console.warn('Could not extract Steam ID from collection item:', item.id);
                        }
                    });
                    return JSON.stringify(results); // Return as JSON string
                })();";

                string jsonResult = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                Debug.WriteLine($"[ModExtractorService] Raw JSON result from collection script: {jsonResult?.Substring(0, Math.Min(jsonResult?.Length ?? 0, 500))}");

                // Check if the result is null or empty JSON string representation like "\"[]\"" or "null"
                 if (string.IsNullOrEmpty(jsonResult) || jsonResult == "null" || jsonResult == "\"[]\"")
                 {
                     Debug.WriteLine("[ModExtractorService] Collection script returned no data.");
                     return items;
                 }


                // Deserialize the JSON array string
                // Note: ExecuteScriptAsync often returns the result as a JSON *string* itself (double-encoded)
                string unwrappedJson = UnwrapJsonString(jsonResult); // Use existing helper

                if (string.IsNullOrWhiteSpace(unwrappedJson))
                 {
                     Debug.WriteLine("[ModExtractorService] Unwrapped JSON string is empty.");
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
                     Debug.WriteLine($"[ModExtractorService] Successfully extracted {items.Count} items from collection.");
                }
                else
                {
                     Debug.WriteLine("[ModExtractorService] Deserialization of collection data resulted in null.");
                }

            }
            catch (JsonException jsonEx)
            {
                // Log the error and the problematic JSON if possible
                Debug.WriteLine($"[ModExtractorService] Error deserializing collection items JSON: {jsonEx.Message}");
                // Debug.WriteLine($"[ModExtractorService] Problematic JSON: {jsonResult}"); // Be careful logging potentially large strings
                // Consider logging only a snippet around jsonEx.Path or BytePositionInLine if available
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModExtractorService] Error extracting collection items: {ex.Message}");
                // Optionally raise an event or log more formally
            }

            return items;
        }

    }
}