#nullable enable
using LibGit2Sharp;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RimSharp.Shared.Services.Implementations
{
    public class GitService : IGitService
    {
        private readonly IDialogService _dialogService;
        public static readonly Regex GitHubUrlRegex = new Regex(
            @"github\.com[/:](?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public GitService(IDialogService dialogService)
        {
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        }

        public bool IsRepository(string path)
        {
            return Repository.IsValid(path);
        }

        public async Task FetchAsync(string path, string remoteName = "origin", CancellationToken ct = default)
        {
            await Task.Run(() =>
            {
                using var repo = new Repository(path);
                var remote = repo.Network.Remotes[remoteName];
                if (remote == null) throw new InvalidOperationException($"Remote '{remoteName}' not found.");

                var fetchOptions = new FetchOptions();
                string logMessage = $"Fetching {remote.Name} for {path}...";
                Commands.Fetch(repo, remote.Name, remote.FetchRefSpecs.Select(x => x.Specification), fetchOptions, logMessage);
            }, ct);
        }

        public async Task<GitDivergence> GetDivergenceAsync(string path, string remoteName = "origin", CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var repo = new Repository(path);
                    var branch = repo.Head;
                    if (branch?.TrackedBranch == null)
                    {
                        return new GitDivergence { ErrorMessage = "No tracking branch" };
                    }

                    var divergence = repo.ObjectDatabase.CalculateHistoryDivergence(branch.Tip, branch.TrackedBranch.Tip);
                    return new GitDivergence
                    {
                        BehindBy = divergence.BehindBy.GetValueOrDefault(),
                        AheadBy = divergence.AheadBy.GetValueOrDefault()
                    };
                }
                catch (Exception ex)
                {
                    return new GitDivergence { ErrorMessage = ex.Message };
                }
            }, ct);
        }

        public async Task<GitPullResult> PullAsync(string path, string remoteName = "origin", CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var repo = new Repository(path);
                    var signature = new Signature("RimSharp Mod Manager", "rimsharp@example.com", DateTimeOffset.Now);
                    var pullOptions = new PullOptions
                    {
                        FetchOptions = new FetchOptions(),
                        MergeOptions = new MergeOptions { FastForwardStrategy = FastForwardStrategy.FastForwardOnly }
                    };

                    MergeResult result = Commands.Pull(repo, signature, pullOptions);

                    return result.Status switch
                    {
                        MergeStatus.UpToDate => new GitPullResult { Status = GitPullStatus.UpToDate },
                        MergeStatus.FastForward => new GitPullResult { Status = GitPullStatus.FastForward },
                        MergeStatus.NonFastForward => new GitPullResult { Status = GitPullStatus.NonFastForward },
                        MergeStatus.Conflicts => new GitPullResult { Status = GitPullStatus.Conflict, Message = "Conflicts detected." },
                        _ => new GitPullResult { Status = GitPullStatus.Error, Message = $"Merge failed with status: {result.Status}" }
                    };
                }
                catch (NonFastForwardException)
                {
                    return new GitPullResult { Status = GitPullStatus.NonFastForward, Message = "Non-fast-forward merge required." };
                }
                catch (CheckoutConflictException)
                {
                    return new GitPullResult { Status = GitPullStatus.Conflict, Message = "Conflicts detected during checkout." };
                }
                catch (Exception ex)
                {
                    return new GitPullResult { Status = GitPullStatus.Error, Message = ex.Message };
                }
            }, ct);
        }

        public async Task CloneAsync(string url, string destinationPath, CancellationToken ct = default)
        {
            await Task.Run(() =>
            {
                var cloneOptions = new CloneOptions { Checkout = true };
                Repository.Clone(url, destinationPath, cloneOptions);
            }, ct);
        }

        public async Task<ModItem?> ValidateGitHubModRepoAsync(string gitUrl, CancellationToken ct = default)
        {
            var repoInfo = ParseGitHubUrl(gitUrl);
            if (repoInfo == null)
            {
                _dialogService.ShowError("Invalid URL", "The provided URL is not a valid GitHub repository URL.");
                return null;
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "RimSharp-ModManager");

            try
            {
                string apiRoot = $"https://api.github.com/repos/{repoInfo.Value.owner}/{repoInfo.Value.repo}/contents";
                var rootResponse = await client.GetFromJsonAsync<List<GitHubContentItem>>(apiRoot, ct);
                if (rootResponse == null) return null;

                // 1. Check About/About.xml
                var aboutDir = rootResponse.FirstOrDefault(i => i.Type == "dir" && "About".Equals(i.Name, StringComparison.OrdinalIgnoreCase));
                if (aboutDir != null)
                {
                    var mod = await TryLoadModMetadata(client, $"{apiRoot}/About/About.xml", ct);
                    if (mod != null) return mod;
                }

                // 2. Check single subfolder
                var rootDirs = rootResponse.Where(i => i.Type == "dir" && i.Name != null && !i.Name.StartsWith(".")).ToList();
                if (rootDirs.Count == 1)
                {
                    string subName = rootDirs[0].Name!;
                    var subApiUrl = $"{apiRoot}/{Uri.EscapeDataString(subName)}";
                    var subResponse = await client.GetFromJsonAsync<List<GitHubContentItem>>(subApiUrl, ct);
                    if (subResponse?.Any(i => i.Type == "dir" && "About".Equals(i.Name, StringComparison.OrdinalIgnoreCase)) ?? false)
                    {
                        var mod = await TryLoadModMetadata(client, $"{subApiUrl}/About/About.xml", ct);
                        if (mod != null) return mod;
                    }
                }

                // 3. Check About.xml in root
                if (rootResponse.Any(i => i.Type == "file" && "About.xml".Equals(i.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    return await TryLoadModMetadata(client, $"{apiRoot}/About.xml", ct);
                }

                _dialogService.ShowError("Invalid Mod Structure", "Could not find 'About/About.xml' in the repository.");
                return null;
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Validation Error", ex.Message);
                return null;
            }
        }

        public (string owner, string repo)? ParseGitHubUrl(string url)
        {
            var match = GitHubUrlRegex.Match(url);
            return match.Success ? (match.Groups["owner"].Value, match.Groups["repo"].Value) : null;
        }

        private async Task<ModItem?> TryLoadModMetadata(HttpClient client, string apiPath, CancellationToken ct)
        {
            try
            {
                var meta = await client.GetFromJsonAsync<GitHubContentItem>(apiPath, ct);
                if (meta?.Type != "file" || string.IsNullOrEmpty(meta.DownloadUrl)) return null;

                var xml = await client.GetStringAsync(meta.DownloadUrl, ct);
                return ParseAboutXml(xml);
            }
            catch { return null; }
        }

        private ModItem? ParseAboutXml(string xml)
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
            catch { return null; }
        }

        private class GitHubContentItem
        {
            [JsonPropertyName("name")] public string? Name { get; set; }
            [JsonPropertyName("type")] public string? Type { get; set; }
            [JsonPropertyName("download_url")] public string? DownloadUrl { get; set; }
        }
    }
}
