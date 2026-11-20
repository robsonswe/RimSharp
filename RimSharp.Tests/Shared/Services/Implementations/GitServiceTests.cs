using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using NSubstitute;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;
using Xunit;

namespace RimSharp.Tests.Shared.Services.Implementations
{
    public class GitServiceTests
    {
        [Theory]
        [InlineData("https://github.com/owner/repo", "owner", "repo")]
        [InlineData("https://github.com/owner/repo.git", "owner", "repo")]
        [InlineData("git@github.com:owner/repo.git", "owner", "repo")]
        [InlineData("https://github.com/owner/repo/", "owner", "repo")]
        public void ParseGitHubUrl_ShouldParseCorrectly(string url, string expectedOwner, string expectedRepo)
        {

            var mockDialog = Substitute.For<IDialogService>();
            var service = new GitService(mockDialog);

            var result = service.ParseGitHubUrl(url);

            result.Should().NotBeNull();
            result!.Value.owner.Should().Be(expectedOwner);
            result!.Value.repo.Should().Be(expectedRepo);
        }

        [Theory]
        [InlineData("https://notgithub.com/owner/repo")]
        [InlineData("invalid")]
        public void ParseGitHubUrl_WithInvalidUrl_ShouldReturnNull(string url)
        {

            var mockDialog = Substitute.For<IDialogService>();
            var service = new GitService(mockDialog);

            var result = service.ParseGitHubUrl(url);

            result.Should().BeNull();
        }

        [Fact]
        public async Task FetchAsync_WithMissingOriginRemote_ShouldThrowInvalidOperationException()
        {
            var mockDialog = Substitute.For<IDialogService>();
            var service = new GitService(mockDialog);
            var tempDir = Path.Combine(Path.GetTempPath(), "rimsharp-gitservice-" + Guid.NewGuid().ToString("N"));

            try
            {
                Repository.Init(tempDir);

                Func<Task> act = () => service.FetchAsync(tempDir, "origin", CancellationToken.None);

                await act.Should().ThrowAsync<InvalidOperationException>()
                    .WithMessage("*Remote 'origin' not found*");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void BuildCredentialsProvider_WithDefaultType_ShouldReturnDefaultCredentials()
        {
            var handler = GetCredentialsProvider();

            var credentials = handler("https://github.com/owner/repo.git", "", SupportedCredentialTypes.Default);

            // Harmless on all platforms: SSH uses the OpenSSH exec transport (callback not invoked);
            // on HTTP this is Windows/Negotiate auth.
            credentials.Should().BeOfType<DefaultCredentials>();
        }

        [Fact]
        public void BuildCredentialsProvider_WithUsernamePasswordType_ShouldReturnNull()
        {
            var handler = GetCredentialsProvider();

            var credentials = handler("https://github.com/owner/repo.git", "", SupportedCredentialTypes.UsernamePassword);

            credentials.Should().BeNull();
        }

        private static CredentialsHandler GetCredentialsProvider()
        {
            var method = typeof(GitService).GetMethod("BuildCredentialsProvider", BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();
            return (CredentialsHandler)method!.Invoke(null, null)!;
        }
    }
}
