#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IGitService
    {
        bool IsRepository(string path);
        Task FetchAsync(string path, string remoteName = "origin", CancellationToken ct = default);
        Task<GitDivergence> GetDivergenceAsync(string path, string remoteName = "origin", CancellationToken ct = default);
        Task<GitPullResult> PullAsync(string path, string remoteName = "origin", CancellationToken ct = default);
        Task CloneAsync(string url, string destinationPath, CancellationToken ct = default);
        Task<ModItem?> ValidateGitHubModRepoAsync(string gitUrl, CancellationToken ct = default);
        (string owner, string repo)? ParseGitHubUrl(string url);
    }

    public class GitDivergence
    {
        public int BehindBy { get; set; }
        public int AheadBy { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsValid => ErrorMessage == null;
    }

    public enum GitPullStatus
    {
        UpToDate,
        FastForward,
        NonFastForward,
        Conflict,
        Error
    }

    public class GitPullResult
    {
        public GitPullStatus Status { get; set; }
        public string? Message { get; set; }
    }
}
