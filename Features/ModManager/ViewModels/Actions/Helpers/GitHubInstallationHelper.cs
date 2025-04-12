using LibGit2Sharp;
using RimSharp.MyApp.AppFiles; // For RunOnUIThread
using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO; // For Path
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    // Mark the class as partial
    public partial class ModActionsViewModel
    {
        // Regex to parse GitHub URLs (owner/repo)
        private static readonly Regex GitHubUrlRegex = new Regex(
            @"github\.com[/:](?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private (string owner, string repo)? ParseGitHubUrl(string url)
        {
            Debug.WriteLine($"[ParseGitHubUrl] Attempting to parse URL: '{url ?? "null"}'");
            var match = GitHubUrlRegex.Match(url ?? string.Empty);
            if (match.Success)
            {
                var result = (match.Groups["owner"].Value, match.Groups["repo"].Value);
                Debug.WriteLine($"[ParseGitHubUrl] Successfully parsed: Owner='{result.Item1}', Repo='{result.Item2}'");
                return result;
            }
            else
            {
                Debug.WriteLine($"[ParseGitHubUrl] Failed to parse URL.");
                return null;
            }
        }

        private async Task<ModItem> ValidateGitHubModRepo(string gitUrl, CancellationToken ct = default)
        {
            Debug.WriteLine($"[ValidateGitHubModRepo] Starting validation for URL: {gitUrl}");
            var repoInfo = ParseGitHubUrl(gitUrl);
            if (repoInfo == null)
            {
                Debug.WriteLine("[ValidateGitHubModRepo] Invalid GitHub URL format.");
                RunOnUIThread(() => _dialogService.ShowError("Invalid URL", "The provided URL is not a valid GitHub repository URL. Example: https://github.com/owner/repo"));
                return null;
            }

            string owner = repoInfo.Value.owner;
            string repo = repoInfo.Value.repo;
            string apiRoot = $"https://api.github.com/repos/{owner}/{repo}/contents";
            Debug.WriteLine($"[ValidateGitHubModRepo] Using API root: {apiRoot}");

            // Use a static HttpClient or HttpClientFactory for better performance/resource management
            // Consider injecting HttpClientFactory if this class has a dependency injection setup
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "RimSharp-ModManager"); // Required by GitHub API

            try
            {
                Debug.WriteLine($"[ValidateGitHubModRepo] Fetching root contents from API: {apiRoot}");
                var rootResponse = await client.GetFromJsonAsync<List<GitHubContentItem>>(apiRoot, ct);
                ct.ThrowIfCancellationRequested();

                if (rootResponse == null)
                {
                    Debug.WriteLine("[ValidateGitHubModRepo] API returned null for root contents.");
                    RunOnUIThread(() => _dialogService.ShowError("API Error", "Could not retrieve repository contents from GitHub."));
                    return null;
                }
                Debug.WriteLine($"[ValidateGitHubModRepo] Received {rootResponse.Count} items from root API call.");

                // Check for About/About.xml at root
                Debug.WriteLine("[ValidateGitHubModRepo] Checking for 'About/' directory in root.");
                var aboutDirInRoot = rootResponse.FirstOrDefault(i => i.Type == "dir" && i.Name.Equals("About", StringComparison.OrdinalIgnoreCase));
                if (aboutDirInRoot != null)
                {
                    Debug.WriteLine("[ValidateGitHubModRepo] Found 'About/' directory in root. Attempting to load metadata.");
                    var aboutPath = $"{apiRoot}/About/About.xml";
                    var modItem = await TryLoadModMetadata(client, aboutPath, ct);
                    if (modItem != null)
                    {
                        Debug.WriteLine($"[ValidateGitHubModRepo] Successfully loaded metadata from root 'About/About.xml'. PackageId: {modItem.PackageId}");
                        return modItem;
                    }
                    Debug.WriteLine("[ValidateGitHubModRepo] Failed to load metadata from root 'About/About.xml'.");
                }
                else
                {
                    Debug.WriteLine("[ValidateGitHubModRepo] No 'About/' directory found in root.");
                }

                // Check for single subfolder containing About/About.xml
                var rootDirs = rootResponse.Where(i => i.Type == "dir" && !i.Name.StartsWith(".")).ToList(); // Exclude hidden folders like .github
                Debug.WriteLine($"[ValidateGitHubModRepo] Found {rootDirs.Count} non-hidden directories in root. Checking for single subfolder structure.");
                if (rootDirs.Count == 1)
                {
                    var subfolder = rootDirs[0].Name;
                    Debug.WriteLine($"[ValidateGitHubModRepo] Found single subfolder: '{subfolder}'. Fetching its contents.");
                    var subfolderApiUrl = $"{apiRoot}/{Uri.EscapeDataString(subfolder)}"; // Ensure subfolder name is URL encoded
                    var aboutDirInSubfolderResponse = await client.GetFromJsonAsync<List<GitHubContentItem>>(subfolderApiUrl, ct);
                    ct.ThrowIfCancellationRequested();

                    if (aboutDirInSubfolderResponse?.Any(i => i.Type == "dir" && i.Name.Equals("About", StringComparison.OrdinalIgnoreCase)) ?? false)
                    {
                        Debug.WriteLine($"[ValidateGitHubModRepo] Found 'About/' directory inside subfolder '{subfolder}'. Attempting to load metadata.");
                        var nestedAboutPath = $"{subfolderApiUrl}/About/About.xml";
                        var modItem = await TryLoadModMetadata(client, nestedAboutPath, ct);
                        if (modItem != null)
                        {
                            Debug.WriteLine($"[ValidateGitHubModRepo] Successfully loaded metadata from '{subfolder}/About/About.xml'. PackageId: {modItem.PackageId}");
                            return modItem;
                        }
                        Debug.WriteLine($"[ValidateGitHubModRepo] Failed to load metadata from '{subfolder}/About/About.xml'.");
                    }
                    else
                    {
                        Debug.WriteLine($"[ValidateGitHubModRepo] No 'About/' directory found inside subfolder '{subfolder}'.");
                    }
                }
                else if (rootDirs.Count > 1)
                {
                    Debug.WriteLine($"[ValidateGitHubModRepo] Found multiple ({rootDirs.Count}) directories in root, skipping single subfolder check.");
                }
                else
                {
                    Debug.WriteLine("[ValidateGitHubModRepo] No directories found in root.");
                }

                // Check if About.xml exists directly in root (less common for mods)
                Debug.WriteLine("[ValidateGitHubModRepo] Checking for 'About.xml' file directly in root.");
                if (rootResponse.Any(i => i.Type == "file" && i.Name.Equals("About.xml", StringComparison.OrdinalIgnoreCase)))
                {
                    Debug.WriteLine("[ValidateGitHubModRepo] Found 'About.xml' file in root. Attempting to load metadata.");
                    var modItem = await TryLoadModMetadata(client, $"{apiRoot}/About.xml", ct);
                    if (modItem != null)
                    {
                        Debug.WriteLine($"[ValidateGitHubModRepo] Successfully loaded metadata from root 'About.xml'. PackageId: {modItem.PackageId}");
                        return modItem;
                    }
                    Debug.WriteLine("[ValidateGitHubModRepo] Failed to load metadata from root 'About.xml'.");
                }
                else
                {
                    Debug.WriteLine("[ValidateGitHubModRepo] No 'About.xml' file found in root.");
                }

                Debug.WriteLine("[ValidateGitHubModRepo] Could not find a valid mod structure ('About/About.xml').");
                RunOnUIThread(() => _dialogService.ShowError("Invalid Mod Structure", "Could not find 'About/About.xml' in the repository root or a single subfolder."));
                return null;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Debug.WriteLine($"[ValidateGitHubModRepo] HTTP 404 Not Found accessing GitHub API: {ex.Message}");
                RunOnUIThread(() => _dialogService.ShowError("Not Found", $"Repository not found or access denied:\n{gitUrl}\nCheck the URL and ensure the repository is public."));
                return null;
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"[ValidateGitHubModRepo] HTTP request error accessing GitHub API: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("API Error", $"Error accessing GitHub API: {ex.Message}"));
                return null;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ValidateGitHubModRepo] Operation cancelled.");
                throw; // Propagate cancellation
            }
            catch (Exception ex) // Catch other potential errors like JSON deserialization
            {
                Debug.WriteLine($"[ValidateGitHubModRepo] Unexpected error: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Validation Error", $"An unexpected error occurred while validating the repository: {ex.Message}"));
                return null;
            }
        }

        private async Task<ModItem> TryLoadModMetadata(HttpClient client, string apiPath, CancellationToken ct)
        {
            Debug.WriteLine($"[TryLoadModMetadata] Attempting to load metadata from API path: {apiPath}");
            try
            {
                // Fetch metadata about the file (includes download URL)
                Debug.WriteLine($"[TryLoadModMetadata] Fetching file metadata from: {apiPath}");
                var aboutMeta = await client.GetFromJsonAsync<GitHubContentItem>(apiPath, ct);
                ct.ThrowIfCancellationRequested();

                if (aboutMeta?.Type != "file" || string.IsNullOrEmpty(aboutMeta.DownloadUrl))
                {
                    Debug.WriteLine($"[TryLoadModMetadata] Invalid metadata or no download URL found at {apiPath}. Type: {aboutMeta?.Type}, DownloadUrl: {aboutMeta?.DownloadUrl ?? "null"}");
                    return null;
                }

                Debug.WriteLine($"[TryLoadModMetadata] Metadata found. Downloading About.xml content from: {aboutMeta.DownloadUrl}");
                // Download the actual About.xml content
                var aboutXml = await client.GetStringAsync(aboutMeta.DownloadUrl, ct);
                ct.ThrowIfCancellationRequested();

                Debug.WriteLine($"[TryLoadModMetadata] Successfully downloaded About.xml content. Parsing...");
                // Use common helper to parse
                var modItem = ParseAboutXmlFromString(aboutXml);
                if (modItem != null)
                {
                    Debug.WriteLine($"[TryLoadModMetadata] Successfully parsed About.xml. PackageId: {modItem.PackageId}");
                }
                else
                {
                    Debug.WriteLine("[TryLoadModMetadata] Failed to parse About.xml content.");
                }
                return modItem;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Debug.WriteLine($"[TryLoadModMetadata] About.xml not found (404) at API path: {apiPath}");
                return null; // Expected if About.xml doesn't exist at this specific path
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[TryLoadModMetadata] Operation cancelled while processing {apiPath}.");
                throw; // Propagate cancellation
            }
            catch (Exception ex)
            {
                // Log other errors (JSON parsing, network issues)
                Debug.WriteLine($"[TryLoadModMetadata] Error loading/parsing About.xml from {apiPath}: {ex}");
                return null;
            }
        }

        // Common helper to parse About.xml content (used by both Zip and GitHub helpers)
        private ModItem ParseAboutXmlFromString(string xml)
        {
            Debug.WriteLine("[ParseAboutXmlFromString] Attempting to parse About.xml string.");
            if (string.IsNullOrWhiteSpace(xml))
            {
                Debug.WriteLine("[ParseAboutXmlFromString] Input XML string is null or empty.");
                return null;
            }
            try
            {
                var doc = XDocument.Parse(xml);
                var root = doc.Element("ModMetaData");
                if (root == null)
                {
                    Debug.WriteLine("[ParseAboutXmlFromString] XML lacks <ModMetaData> root element.");
                    return null;
                }

                var modItem = new ModItem
                {
                    Name = root.Element("name")?.Value?.Trim(),
                    PackageId = root.Element("packageId")?.Value?.Trim(),
                    Authors = root.Element("author")?.Value?.Trim() ??
                              string.Join(", ", root.Element("authors")?.Elements("li").Select(x => x.Value?.Trim()) ?? Array.Empty<string>())
                };

                Debug.WriteLine($"[ParseAboutXmlFromString] Parsed: Name='{modItem.Name}', PackageId='{modItem.PackageId}', Authors='{modItem.Authors}'");
                // Basic validation: Check if essential fields are present
                if (string.IsNullOrWhiteSpace(modItem.Name) || string.IsNullOrWhiteSpace(modItem.PackageId))
                {
                    Debug.WriteLine($"[ParseAboutXmlFromString] Warning: Parsed About.xml is missing Name ('{modItem.Name}') or PackageId ('{modItem.PackageId}').");
                    // Decide if this should return null or allow partial data
                    // Returning null might be safer if PackageId is crucial
                    if (string.IsNullOrWhiteSpace(modItem.PackageId))
                    {
                        Debug.WriteLine("[ParseAboutXmlFromString] Missing PackageId. Returning null.");
                        return null;
                    }
                }

                return modItem;
            }
            catch (System.Xml.XmlException ex)
            {
                Debug.WriteLine($"[ParseAboutXmlFromString] Failed to parse About.xml content due to XML error: {ex.Message}");
                return null;
            }
            catch (Exception ex) // Catch unexpected errors during parsing
            {
                Debug.WriteLine($"[ParseAboutXmlFromString] Unexpected error parsing About.xml content: {ex}");
                return null;
            }
        }

        // Synchronous Git clone operation (should be run on background thread)
        private void CloneGitMod(string gitUrl, string destinationPath)
        {
            // Note: LibGit2Sharp operations are blocking.
            // The calling command (ExecuteInstallFromGithub) uses Task.Run for this.
            Debug.WriteLine($"[CloneGitMod] Starting clone operation. Source: '{gitUrl}', Destination: '{destinationPath}'");
            try
            {
                Debug.WriteLine($"[CloneGitMod] Cloning '{gitUrl}' to '{destinationPath}'...");
                // Basic clone options. Add progress handling if needed via CloneOptions.OnTransferProgress.
                var cloneOptions = new CloneOptions
                {
                    Checkout = true
                    // Example Progress Handler (uncomment and implement if needed):
                    //, OnTransferProgress = progress =>
                    //{
                    //    Debug.WriteLine($"[CloneGitMod] Progress: {progress.ReceivedObjects}/{progress.TotalObjects} objects, {progress.ReceivedBytes} bytes");
                    //    // Update UI progress here if needed (using Dispatcher or similar)
                    //    return true; // return false to cancel
                    //}
                };
                Repository.Clone(gitUrl, destinationPath, cloneOptions);
                Debug.WriteLine($"[CloneGitMod] Clone successful for '{gitUrl}' to '{destinationPath}'.");
                // Success message is shown in the calling command method after this returns.
            }
            catch (LibGit2SharpException lgEx) // Catch specific LibGit2Sharp exceptions
            {
                Debug.WriteLine($"[CloneGitMod] LibGit2Sharp error cloning repository '{gitUrl}': {lgEx}");
                // Error is shown in the calling command method's catch block.
                // Re-throw to ensure the calling method knows about the failure.
                throw new InvalidOperationException($"Failed to clone repository '{gitUrl}': {lgEx.Message}", lgEx);
            }
            catch (Exception ex) // Catch other potential errors (e.g., directory access issues)
            {
                Debug.WriteLine($"[CloneGitMod] General error cloning repository '{gitUrl}': {ex}");
                // Error is shown in the calling command method's catch block.
                // Re-throw to ensure the calling method knows about the failure.
                throw new InvalidOperationException($"Failed to clone repository '{gitUrl}': {ex.Message}", ex);
            }
        }

        // Helper class for deserializing GitHub API response
        private class GitHubContentItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } // Critical fix here

    [JsonPropertyName("url")]
    public string Url { get; set; }
}

    }
}