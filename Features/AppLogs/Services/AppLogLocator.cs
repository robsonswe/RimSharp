#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.AppLogs.Services
{
    /// <summary>
    /// Finds the app's own log files the same way <see cref="LoggerService"/>
    /// writes them: <c>RimSharp_{module}_{yyyyMMdd}.log</c> under the
    /// configured logs directory, defaulting to the app data Logs folder.
    /// One file per service per day; the writer keeps only today's files.
    /// </summary>
    public static class AppLogLocator
    {
        public const string FilePrefix = "RimSharp_";
        public const string LogsFolderName = "Logs";

        /// <summary>
        /// The directory the logger writes to: the <c>logs_directory</c>
        /// setting when present, the app data Logs folder otherwise.
        /// </summary>
        public static string ResolveLogsDirectory(IConfigService? configService, string? appDataDirectory = null)
        {
            var custom = configService?.GetConfigValue("logs_directory");
            if (!string.IsNullOrWhiteSpace(custom))
            {
                return custom;
            }

            return Path.Combine(appDataDirectory ?? AppDataPaths.DataDirectory, LogsFolderName);
        }

        /// <summary>
        /// Every log file in the directory, newest day first and, within a
        /// day, by module name. Missing directory yields an empty list.
        /// </summary>
        public static List<AppLogFile> Enumerate(string logsDirectory)
        {
            if (string.IsNullOrWhiteSpace(logsDirectory) || !Directory.Exists(logsDirectory))
            {
                return new List<AppLogFile>();
            }

            var files = new List<AppLogFile>();
            foreach (var path in Directory.EnumerateFiles(logsDirectory, FilePrefix + "*.log"))
            {
                var parsed = TryParseFileName(Path.GetFileName(path), path);
                if (parsed != null)
                {
                    files.Add(parsed);
                }
            }

            return files
                .OrderByDescending(f => f.Date)
                .ThenBy(f => f.Module, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Parses <c>RimSharp_{module}_{yyyyMMdd}.log</c>. The date is the
        /// trailing eight digits, so modules whose names contain underscores
        /// still resolve. Returns null for anything else in the folder.
        /// </summary>
        public static AppLogFile? TryParseFileName(string fileName, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            var name = fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                ? fileName[..^4]
                : fileName;

            if (!name.StartsWith(FilePrefix, StringComparison.Ordinal))
            {
                return null;
            }

            var lastSeparator = name.LastIndexOf('_');
            if (lastSeparator < FilePrefix.Length)
            {
                return null;
            }

            var dateText = name[(lastSeparator + 1)..];
            if (dateText.Length != 8 ||
                !DateTime.TryParseExact(dateText, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                return null;
            }

            var module = name[FilePrefix.Length..lastSeparator];
            if (module.Length == 0)
            {
                return null;
            }

            long size = 0;
            var lastWrite = date;
            try
            {
                var info = new FileInfo(fullPath);
                size = info.Length;
                lastWrite = info.LastWriteTime;
            }
            catch (IOException)
            {
                // A file that vanished mid-scan still belongs in the list.
            }
            catch (UnauthorizedAccessException)
            {
            }

            return new AppLogFile(module, date, fullPath, size, lastWrite);
        }
    }
}
