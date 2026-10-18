#nullable enable
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
        private readonly string _appBasePath;
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private DateTime _currentLogDate = DateTime.MinValue;

        public LoggerService(IConfigService configService, string? appBasePath = null)
        {
            _configService = configService;
            _appBasePath = appBasePath ?? AppDataPaths.DataDirectory;
        }

        public void Log(LogLevel level, string message, string module = "General")
        {
            var logMessage = FormatLogMessage(level, message, module);

            Console.WriteLine(logMessage);
            Debug.WriteLine(logMessage);

            WriteToLogFile(logMessage, module);
        }

        public void LogDebug(string message, string module = "General") => Log(LogLevel.Debug, message, module);
        public void LogInfo(string message, string module = "General") => Log(LogLevel.Info, message, module);
        public void LogWarning(string message, string module = "General") => Log(LogLevel.Warning, message, module);
        public void LogError(string message, string module = "General") => Log(LogLevel.Error, message, module);
        public void LogCritical(string message, string module = "General") => Log(LogLevel.Critical, message, module);

        public void LogException(Exception ex, string? message = null, string module = "General")
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
                if (_currentLogDate != today)
                {
                    _currentLogDate = today;
                    ClearOldLogs(logsDirectory);
                }
                Directory.CreateDirectory(logsDirectory);

                // Append to log file
                File.AppendAllText(logFilePath, message + Environment.NewLine);
            }
            catch (Exception ex)
            {

                Console.WriteLine($"Failed to write to log file: {ex.Message}");
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private string GetLogsDirectory()
        {

            var customLogPath = _configService.GetConfigValue("logs_directory");
            if (!string.IsNullOrEmpty(customLogPath))
            {
                return customLogPath;
            }

            return Path.Combine(_appBasePath, "Logs");
        }

        private void ClearOldLogs(string logsDirectory)
        {
            try
            {
                var today = DateTime.Today;
                var todayDateString = today.ToString("yyyyMMdd");

                // Get ALL log files in the directory
                var allLogFiles = Directory.GetFiles(logsDirectory, "RimSharp_*.log");

                foreach (var file in allLogFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    
                    // Check if the file name contains today's date
                    if (!fileName.EndsWith(todayDateString))
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

