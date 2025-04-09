using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Logging
{
    public class LoggerService : ILoggerService
    {
        private readonly IConfigService _configService;
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private DateTime _currentLogDate = DateTime.MinValue;

        public LoggerService(IConfigService configService)
        {
            _configService = configService;
        }

        public void Log(LogLevel level, string message, string module = "General")
        {
            var logMessage = FormatLogMessage(level, message, module);

            // Write to console
            Console.WriteLine(logMessage);
            Debug.WriteLine(logMessage);

            // Write to file
            WriteToLogFile(logMessage, module);
        }

        public void LogDebug(string message, string module = "General") => Log(LogLevel.Debug, message, module);
        public void LogInfo(string message, string module = "General") => Log(LogLevel.Info, message, module);
        public void LogWarning(string message, string module = "General") => Log(LogLevel.Warning, message, module);
        public void LogError(string message, string module = "General") => Log(LogLevel.Error, message, module);
        public void LogCritical(string message, string module = "General") => Log(LogLevel.Critical, message, module);

        public void LogException(Exception ex, string message = null, string module = "General")
        {
            var fullMessage = message != null
                ? $"{message}\nException: {ex}\nStackTrace: {ex.StackTrace}"
                : $"Exception: {ex}\nStackTrace: {ex.StackTrace}";

            Log(LogLevel.Error, fullMessage, module);
        }

        private string FormatLogMessage(LogLevel level, string message, string module)
        {
            return $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpper()}] [{module}] {message}";
        }

        private void WriteToLogFile(string message, string module)
        {
            try
            {
                _lock.EnterWriteLock();

                var logsDirectory = GetLogsDirectory();
                var today = DateTime.Today;
                var logFileName = $"RimSharp_{module}_{today:yyyyMMdd}.log";
                var logFilePath = Path.Combine(logsDirectory, logFileName);

                // Check if we need to clear previous day's logs
                if (_currentLogDate != today)
                {
                    _currentLogDate = today;
                    ClearOldLogs(logsDirectory, module);
                }

                // Ensure directory exists
                Directory.CreateDirectory(logsDirectory);

                // Append to log file
                File.AppendAllText(logFilePath, message + Environment.NewLine);
            }
            catch (Exception ex)
            {
                // Fallback to console if file logging fails
                Console.WriteLine($"Failed to write to log file: {ex.Message}");
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private string GetLogsDirectory()
        {
            // Get from config or use default
            var customLogPath = _configService.GetConfigValue("logs_directory");
            if (!string.IsNullOrEmpty(customLogPath))
            {
                return customLogPath;
            }

            // Default to application base directory + Logs
            var appBaseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(appBaseDir, "Logs");
        }

        private void ClearOldLogs(string logsDirectory, string module)
        {
            try
            {
                var today = DateTime.Today;
                var todayFileName = $"RimSharp_{module}_{today:yyyyMMdd}.log";

                // Get all log files for this module
                var moduleLogFiles = Directory.GetFiles(logsDirectory, $"RimSharp_{module}_*.log");

                foreach (var file in moduleLogFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName != todayFileName)
                    {
                        try { File.Delete(file); }
                        catch { /* Ignore deletion errors */ }
                    }
                }
            }
            catch { /* Ignore directory access errors */ }
        }
    }
}