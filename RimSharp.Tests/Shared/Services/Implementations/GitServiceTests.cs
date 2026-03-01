using FluentAssertions;
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
            // Arrange
            var mockDialog = Substitute.For<IDialogService>();
            var service = new GitService(mockDialog);

            // Act
            var result = service.ParseGitHubUrl(url);

            // Assert
            result.Should().NotBeNull();
            result!.Value.owner.Should().Be(expectedOwner);
            result!.Value.repo.Should().Be(expectedRepo);
        }

        [Theory]
        [InlineData("https://notgithub.com/owner/repo")]
        [InlineData("invalid")]
        public void ParseGitHubUrl_WithInvalidUrl_ShouldReturnNull(string url)
        {
            // Arrange
            var mockDialog = Substitute.For<IDialogService>();
            var service = new GitService(mockDialog);

            // Act
            var result = service.ParseGitHubUrl(url);

            // Assert
            result.Should().BeNull();
        }
    }
}
