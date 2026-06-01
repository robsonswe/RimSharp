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

            var logger = new LoggerService(_mockConfigService, _testTempDir);
            string message = "Test log message";
            string module = "TestModule";
            var today = DateTime.Today.ToString("yyyyMMdd");
            var expectedFileName = $"RimSharp_{module}_{today}.log";
            var expectedDir = Path.Combine(_testTempDir, "Logs");

            logger.LogInfo(message, module);

            var logFiles = Directory.GetFiles(expectedDir, "*.log");
            logFiles.Should().HaveCount(1);
            Path.GetFileName(logFiles[0]).Should().Be(expectedFileName);
            
            var content = File.ReadAllText(logFiles[0]);
            content.Should().Contain("[INFO]");
            content.Should().Contain($"[{module}]");
            content.Should().Contain(message);
        }

        [Fact]
        public void ClearOldLogs_ShouldDeletePreviousLogsForAllModules()
        {
            var logsDir = Path.Combine(_testTempDir, "Logs");
            Directory.CreateDirectory(logsDir);
            
            // Create old logs for multiple modules
            var fileA = Path.Combine(logsDir, "RimSharp_ModuleA_20000101.log");
            var fileB = Path.Combine(logsDir, "RimSharp_ModuleB_20000101.log");
            File.WriteAllText(fileA, "old");
            File.WriteAllText(fileB, "old");

            // Verify they exist before triggering cleanup
            File.Exists(fileA).Should().BeTrue("old file A should exist before cleanup");
            File.Exists(fileB).Should().BeTrue("old file B should exist before cleanup");

            var logger = new LoggerService(_mockConfigService, _testTempDir);

            // Log a new message for ModuleA - this should trigger cleanup of ALL old logs
            logger.LogInfo("Today's log", "ModuleA");

            Directory.GetFiles(logsDir, "RimSharp_*.log").Should().HaveCount(1);
            File.Exists(Path.Combine(logsDir, $"RimSharp_ModuleA_{DateTime.Today:yyyyMMdd}.log")).Should().BeTrue();
            File.Exists(fileA).Should().BeFalse("old file A should be deleted");
            File.Exists(fileB).Should().BeFalse("old file B should be deleted");
        }
    }
}


