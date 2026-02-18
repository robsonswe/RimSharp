using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Workshop.Download.Parsing;
using RimSharp.Infrastructure.Workshop.Download.Parsing.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Workshop.Download.Parsing
{
    public class SteamCmdLogParserTests : IDisposable
    {
        private readonly ILoggerService _mockLogger;
        private readonly SteamCmdLogParser _parser;
        private readonly string _testTempDir;

        public SteamCmdLogParserTests()
        {
            _mockLogger = Substitute.For<ILoggerService>();
            _parser = new SteamCmdLogParser(_mockLogger);
            
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_Log_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        private string CreateLogFile(string name, string content)
        {
            var path = Path.Combine(_testTempDir, name);
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public async Task ParseSteamCmdSessionLogsAsync_ShouldDetectWorkshopSuccess()
        {
            // Arrange
            var workshopContent = "[2024-02-18 12:00:00] [AppID 294100] Download item 123 result: OK";
            var workshopPath = CreateLogFile("workshop.log", workshopContent);
            
            var logPaths = new SteamCmdLogFilePaths 
            { 
                WorkshopLogPath = workshopPath 
            };
            var ids = new HashSet<string> { "123" };
            var filterTime = new DateTime(2024, 02, 18, 11, 0, 0);

            // Act
            var result = await _parser.ParseSteamCmdSessionLogsAsync(logPaths, ids, filterTime, CancellationToken.None);

            // Assert
            result.WorkshopItemResults.Should().ContainKey("123");
            result.WorkshopItemResults["123"].Success.Should().BeTrue();
        }

        [Fact]
        public async Task ParseSteamCmdSessionLogsAsync_ShouldFilterByTime()
        {
            // Arrange
            var workshopContent = @"
[2024-02-18 10:00:00] [AppID 294100] Download item 123 result: OK
[2024-02-18 12:00:00] [AppID 294100] Download item 456 result: OK";
            var workshopPath = CreateLogFile("workshop.log", workshopContent);
            
            var logPaths = new SteamCmdLogFilePaths { WorkshopLogPath = workshopPath };
            var ids = new HashSet<string> { "123", "456" };
            var filterTime = new DateTime(2024, 02, 18, 11, 0, 0);

            // Act
            var result = await _parser.ParseSteamCmdSessionLogsAsync(logPaths, ids, filterTime, CancellationToken.None);

            // Assert
            result.WorkshopItemResults.Should().ContainKey("456");
            result.WorkshopItemResults.Should().NotContainKey("123");
        }

        [Fact]
        public async Task ParseSteamCmdSessionLogsAsync_ShouldDetectLoginFailure()
        {
            // Arrange
            var primaryContent = "FAILED to log in";
            var primaryPath = CreateLogFile("primary.log", primaryContent);
            
            var logPaths = new SteamCmdLogFilePaths { PrimaryExecutionLogPath = primaryPath };
            var ids = new HashSet<string> { "123" };
            var filterTime = DateTime.Now;

            // Act
            var result = await _parser.ParseSteamCmdSessionLogsAsync(logPaths, ids, filterTime, CancellationToken.None);

            // Assert
            result.OverallStatus.HasLoginFailed.Should().BeTrue();
            result.WorkshopItemResults.Should().ContainKey("123");
            result.WorkshopItemResults["123"].Success.Should().BeFalse();
            result.WorkshopItemResults["123"].Reason.Should().Be("Login Failure");
        }

        [Fact]
        public async Task ParseSteamCmdSessionLogsAsync_ShouldDetectDiskSpaceIssue()
        {
            // Arrange
            var contentLog = "[2024-02-18 12:00:00] Not enough disk space";
            var contentPath = CreateLogFile("content.log", contentLog);
            
            var logPaths = new SteamCmdLogFilePaths { ContentLogPath = contentPath };
            var ids = new HashSet<string> { "123" };
            var filterTime = new DateTime(2024, 02, 18, 11, 0, 0);

            // Act
            var result = await _parser.ParseSteamCmdSessionLogsAsync(logPaths, ids, filterTime, CancellationToken.None);

            // Assert
            result.OverallStatus.HasDiskSpaceIssue.Should().BeTrue();
        }
    }
}
