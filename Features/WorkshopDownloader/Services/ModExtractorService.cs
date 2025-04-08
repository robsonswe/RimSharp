using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
// Use the WPF version of WebView2
using Microsoft.Web.WebView2.Wpf; // <<< CHANGED
using RimSharp.Features.WorkshopDownloader.Models;

namespace RimSharp.Features.WorkshopDownloader.Services
{
    // Interface remains the same
    public interface IModExtractorService
    {
        Task<string> ExtractModName();
        Task<string> ExtractModDateInfo();
        Task<string> ConvertToStandardDate(string dateString);
        Task<ModInfoDto> ExtractFullModInfo();
    }

    public class ModExtractorService : IModExtractorService
    {
        // Use the WPF WebView2 type
        private readonly Microsoft.Web.WebView2.Wpf.WebView2 _webView; // <<< CHANGED

        // Accept the WPF WebView2 type
        public ModExtractorService(Microsoft.Web.WebView2.Wpf.WebView2 webView) // <<< CHANGED
        {
            _webView = webView;
        }

        public async Task<string> ExtractModName()
        {
            // Access CoreWebView2 the same way
            if (_webView?.CoreWebView2 == null) return null;

            try
            {
                string script = @"
                    (function() {
                        const titleElement = document.querySelector('.workshopItemTitle');
                        return titleElement ? titleElement.textContent.trim() : '';
                    })();
                ";

                string result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                return UnwrapJsonString(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting mod name: {ex.Message}");
                return null;
            }
        }

        public async Task<string> ExtractModDateInfo()
{
    if (_webView?.CoreWebView2 == null) return null;

    try
    {
        string script = @"
            (function() {
                const detailsContainer = document.querySelector('.detailsStatsContainerRight');
                if (!detailsContainer) return '';

                const dateElements = detailsContainer.querySelectorAll('.detailsStatRight');
                if (dateElements.length > 0) {
                    let dateString = dateElements[dateElements.length - 1].textContent.trim();
                    
                    // Normalize spaces and handle missing year
                    dateString = dateString.replace(/\s+/g, ' '); // Collapse multiple spaces
                    
                    if (!/\d{4}/.test(dateString) && dateString.includes('@')) {
                        const currentYear = new Date().getFullYear();
                        // Insert year before @ with proper spacing
                        dateString = dateString.replace(' @', ` ${currentYear} @`);
                    }
                    return dateString;
                }
                return '';
            })();
        ";

        string result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
        return UnwrapJsonString(result);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error extracting mod date info: {ex.Message}");
        return null;
    }
}

        public async Task<string> ConvertToStandardDate(string dateString)
{
    if (string.IsNullOrWhiteSpace(dateString))
    {
        Console.WriteLine("[ConvertToStandardDate] Input date string was null or empty");
        return string.Empty;
    }

    Console.WriteLine($"[ConvertToStandardDate] Starting conversion for: '{dateString}'");

    try
    {
        // Ensure year exists - if not, add current year (as fallback)
        if (!Regex.IsMatch(dateString, @"\b\d{4}\b") && dateString.Contains("@"))
        {
            dateString = dateString.Insert(dateString.IndexOf('@'), $" {DateTime.Now.Year}");
            Console.WriteLine($"[ConvertToStandardDate] Added current year to string: '{dateString}'");
        }

        // Normalize the string by removing extra spaces around @
        dateString = Regex.Replace(dateString, @"\s+@\s+", " @ ");
        
        // Try parsing with different formats
        var formats = new[] 
        {
            "d MMM yyyy @ h:mmtt",    // 7 Apr 2025 @ 2:17am
            "d MMMM yyyy @ h:mmtt",   // 7 April 2025 @ 2:17am
            "d MMM, yyyy @ h:mmtt",   // 7 Apr, 2025 @ 2:17am
            "d MMMM, yyyy @ h:mmtt",  // 7 April, 2025 @ 2:17am
            "d MMM yyyy",             // Fallback without time
            "d MMMM yyyy"            // Fallback without time
        };

        if (DateTime.TryParseExact(dateString, formats, 
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date))
        {
            var result = date.ToString("dd/MM/yyyy HH:mm:ss");
            Console.WriteLine($"[ConvertToStandardDate] Successfully converted to: '{result}'");
            return result;
        }

        Console.WriteLine($"[ConvertToStandardDate] All parsing attempts failed, trying DateTime.Parse");
        if (DateTime.TryParse(dateString, out var fallbackDate))
        {
            var result = fallbackDate.ToString("dd/MM/yyyy HH:mm:ss");
            Console.WriteLine($"[ConvertToStandardDate] Fallback parse succeeded: '{result}'");
            return result;
        }

        Console.WriteLine($"[ConvertToStandardDate] All parsing methods failed");
        return dateString;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ConvertToStandardDate] Error: {ex.Message}");
        return dateString;
    }
}

        public async Task<ModInfoDto> ExtractFullModInfo()
        {
            // Access Source the same way
            if (_webView?.Source == null || _webView.CoreWebView2 == null) return null;

            var url = _webView.Source.ToString();
            // Use Uri class for robust parsing
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                !(uri.Host.EndsWith("steamcommunity.com") && uri.AbsolutePath.Contains("/sharedfiles/filedetails/")))
            {
                Console.WriteLine($"URL is not a valid Steam Workshop item page: {url}");
                return null;
            }

            // Extract ID from URL using Query parameters
            var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var id = queryParams["id"];
            if (string.IsNullOrEmpty(id))
            {
                Console.WriteLine($"Could not extract 'id' from URL: {url}");
                return null;
            }

            // Extract additional information
            string modName = await ExtractModName();
            string dateInfo = await ExtractModDateInfo();
            string standardDate = await ConvertToStandardDate(dateInfo); // Use the potentially converted date

            // Handle cases where extraction might fail
            if (string.IsNullOrEmpty(modName))
            {
                modName = $"Mod {id}"; // Fallback name
                Console.WriteLine($"Could not extract mod name for ID {id}, using fallback.");
            }
            if (string.IsNullOrEmpty(dateInfo))
            {
                dateInfo = "Unknown Date";
                Console.WriteLine($"Could not extract date info for ID {id}.");
            }
            if (string.IsNullOrEmpty(standardDate)) // Should happen only if ConvertToStandardDate fails/returns null
            {
                standardDate = dateInfo; // Fallback to original date info
            }


            return new ModInfoDto
            {
                Name = modName,
                Url = url,
                SteamId = id,
                PublishDate = dateInfo, // Store the original string from Steam
                StandardDate = standardDate // Store the potentially converted date string
            };
        }

        private string UnwrapJsonString(string jsonValue)
        {
            // This utility function likely works fine as is
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
    }
}
