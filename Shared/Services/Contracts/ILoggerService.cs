using System;

namespace RimSharp.Shared.Services.Contracts
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }

    public interface ILoggerService
    {
        void Log(LogLevel level, string message, string module = "General");
        void LogDebug(string message, string module = "General");
        void LogInfo(string message, string module = "General");
        void LogWarning(string message, string module = "General");
        void LogError(string message, string module = "General");
        void LogCritical(string message, string module = "General");
        void LogException(Exception ex, string message = null, string module = "General");
    }
}