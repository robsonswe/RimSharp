using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Logging;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Logging
{
    public class LoggerServiceTests : IDisposable
    {
        private readonly IConfigService _mockConfigService;
        private readonly string _testTempDir;

        public LoggerServiceTests()
        {
            _mockConfigService = Substitute.For<IConfigService>();
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharp_LoggerTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        [Fact]
        public void Log_ShouldCreateLogFileAndWriteMessage()
        {
            // Arrange
            var logger = new LoggerService(_mockConfigService, _testTempDir);
            string message = "Test log message";
            string module = "TestModule";
            var today = DateTime.Today.ToString("yyyyMMdd");
            var expectedFileName = $"RimSharp_{module}_{today}.log";
            var expectedDir = Path.Combine(_testTempDir, "Logs");

            // Act
            logger.LogInfo(message, module);

            // Assert
            var logFiles = Directory.GetFiles(expectedDir, "*.log");
            logFiles.Should().HaveCount(1);
            Path.GetFileName(logFiles[0]).Should().Be(expectedFileName);
            
            var content = File.ReadAllText(logFiles[0]);
            content.Should().Contain("[INFO]");
            content.Should().Contain($"[{module}]");
            content.Should().Contain(message);
        }

        [Fact]
        public void ClearOldLogs_ShouldDeletePreviousLogsForSameModule()
        {
            // Arrange
            var logsDir = Path.Combine(_testTempDir, "Logs");
            Directory.CreateDirectory(logsDir);
            string module = "Rotate";
            
            // Create a "yesterday" log
            var oldFile = Path.Combine(logsDir, $"RimSharp_{module}_20000101.log");
            File.WriteAllText(oldFile, "old logs");

            var logger = new LoggerService(_mockConfigService, _testTempDir);

            // Act - Logging today should trigger cleanup of old files for this module
            logger.LogInfo("Today's log", module);

            // Assert
            File.Exists(oldFile).Should().BeFalse();
            Directory.GetFiles(logsDir, $"RimSharp_{module}_*.log").Should().HaveCount(1);
        }
    }
}
