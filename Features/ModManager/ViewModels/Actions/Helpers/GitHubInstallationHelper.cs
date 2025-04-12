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
            @"github\.com[/:](?<owner>[^/]+)/(?<repo>[^/\\s]+?)(?:\.git)?/?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);


        private (string owner, string repo)? ParseGitHubUrl(string url)
        {
            var match = GitHubUrlRegex.Match(url ?? string.Empty);
            return match.Success ? (match.Groups["owner"].Value, match.Groups["repo"].Value) : null;
        }

        private async Task<ModItem> ValidateGitHubModRepo(string gitUrl, CancellationToken ct = default)
        {
            var repoInfo = ParseGitHubUrl(gitUrl);
            if (repoInfo == null)
            {
                RunOnUIThread(() => _dialogService.ShowError("Invalid URL", "The provided URL is not a valid GitHub repository URL. Example: https://github.com/owner/repo"));
                return null;
            }

            string apiRoot = $"https://api.github.com/repos/{repoInfo.Value.owner}/{repoInfo.Value.repo}/contents";
            // Use a static HttpClient or HttpClientFactory for better performance/resource management
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "RimSharp-ModManager"); // Required by GitHub API

            try
            {
                var rootResponse = await client.GetFromJsonAsync<List<GitHubContentItem>>(apiRoot, ct);
                ct.ThrowIfCancellationRequested();

                if (rootResponse == null)
                {
                     RunOnUIThread(() => _dialogService.ShowError("API Error", "Could not retrieve repository contents from GitHub."));
                     return null;
                }

                // Check for About/About.xml at root
                var aboutDirInRoot = rootResponse.FirstOrDefault(i => i.Type == "dir" && i.Name.Equals("About", StringComparison.OrdinalIgnoreCase));
                if (aboutDirInRoot != null)
                {
                    var aboutPath = $"{apiRoot}/About/About.xml";
                    var modItem = await TryLoadModMetadata(client, aboutPath, ct);
                    if (modItem != null) return modItem;
                }

                // Check for single subfolder containing About/About.xml
                var rootDirs = rootResponse.Where(i => i.Type == "dir").ToList();
                if (rootDirs.Count == 1)
                {
                    var subfolder = rootDirs[0].Name;
                    var aboutDirInSubfolder = await client.GetFromJsonAsync<List<GitHubContentItem>>($"{apiRoot}/{subfolder}", ct);
                    ct.ThrowIfCancellationRequested();
                    if (aboutDirInSubfolder?.Any(i => i.Type == "dir" && i.Name.Equals("About", StringComparison.OrdinalIgnoreCase)) ?? false)
                    {
                         var nestedAboutPath = $"{apiRoot}/{subfolder}/About/About.xml";
                         var modItem = await TryLoadModMetadata(client, nestedAboutPath, ct);
                         if (modItem != null) return modItem;
                    }
                }

                // Check if About.xml exists directly in root (less common for mods)
                if (rootResponse.Any(i => i.Type == "file" && i.Name.Equals("About.xml", StringComparison.OrdinalIgnoreCase)))
                {
                    var modItem = await TryLoadModMetadata(client, $"{apiRoot}/About.xml", ct);
                    if (modItem != null) return modItem;
                }

                RunOnUIThread(() => _dialogService.ShowError("Invalid Mod Structure", "Could not find 'About/About.xml' in the repository root or a single subfolder."));
                return null;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                 RunOnUIThread(() => _dialogService.ShowError("Not Found", $"Repository not found or access denied:\n{gitUrl}\nCheck the URL and ensure the repository is public."));
                 return null;
            }
            catch (HttpRequestException ex)
            {
                RunOnUIThread(() => _dialogService.ShowError("API Error", $"Error accessing GitHub API: {ex.Message}"));
                return null;
            }
             catch (OperationCanceledException) { throw; } // Propagate cancellation
             catch (Exception ex) // Catch other potential errors like JSON deserialization
             {
                 Debug.WriteLine($"[ValidateGitHubModRepo] Unexpected error: {ex}");
                 RunOnUIThread(() => _dialogService.ShowError("Validation Error", $"An unexpected error occurred while validating the repository: {ex.Message}"));
                 return null;
             }
        }

        private async Task<ModItem> TryLoadModMetadata(HttpClient client, string apiPath, CancellationToken ct)
        {
            try
            {
                // Fetch metadata about the file (includes download URL)
                var aboutMeta = await client.GetFromJsonAsync<GitHubContentItem>(apiPath, ct);
                ct.ThrowIfCancellationRequested();

                if (aboutMeta?.Type != "file" || string.IsNullOrEmpty(aboutMeta.DownloadUrl))
                    return null;

                // Download the actual About.xml content
                var aboutXml = await client.GetStringAsync(aboutMeta.DownloadUrl, ct);
                ct.ThrowIfCancellationRequested();

                // Use common helper to parse
                return ParseAboutXmlFromString(aboutXml);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Debug.WriteLine($"[TryLoadModMetadata] About.xml not found at API path: {apiPath}");
                return null; // Expected if About.xml doesn't exist at this specific path
            }
            catch (OperationCanceledException) { throw; } // Propagate cancellation
            catch (Exception ex)
            {
                // Log other errors (JSON parsing, network issues)
                Debug.WriteLine($"[TryLoadModMetadata] Error loading/parsing About.xml from {apiPath}: {ex.Message}");
                return null;
            }
        }

        // Common helper to parse About.xml content (used by both Zip and GitHub helpers)
        private ModItem ParseAboutXmlFromString(string xml)
        {
            try
            {
                var doc = XDocument.Parse(xml);
                var root = doc.Element("ModMetaData");
                if (root == null) return null;

                return new ModItem
                {
                    Name = root.Element("name")?.Value?.Trim(),
                    PackageId = root.Element("packageId")?.Value?.Trim(),
                    Authors = root.Element("author")?.Value?.Trim() ??
                              string.Join(", ", root.Element("authors")?.Elements("li").Select(x => x.Value?.Trim()) ?? Array.Empty<string>())
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ParseAboutXmlFromString] Failed to parse About.xml content: {ex.Message}");
                return null;
            }
        }

        // Synchronous Git clone operation (should be run on background thread)
        private void CloneGitMod(string gitUrl, string destinationPath)
        {
            // Note: LibGit2Sharp operations are blocking.
            // The calling command (ExecuteInstallFromGithub) uses Task.Run for this.
            try
            {
                 Debug.WriteLine($"Cloning '{gitUrl}' to '{destinationPath}'...");
                // Basic clone options. Add progress handling if needed.
                var cloneOptions = new CloneOptions { Checkout = true };
                Repository.Clone(gitUrl, destinationPath, cloneOptions);
                Debug.WriteLine($"Clone successful.");
                 // Success message is shown in the calling command method after this returns.
            }
            catch (Exception ex) // Catch specific LibGit2Sharp exceptions if needed
            {
                 Debug.WriteLine($"[CloneGitMod] Error cloning repository: {ex}");
                 // Error is shown in the calling command method's catch block.
                 // Re-throw to ensure the calling method knows about the failure.
                throw new InvalidOperationException($"Failed to clone repository '{gitUrl}': {ex.Message}", ex);
            }
        }
    }
}