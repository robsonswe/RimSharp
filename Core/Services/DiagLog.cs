#nullable enable
using System;
using System.IO;

namespace RimSharp.Core.Services
{
    /// <summary>
    /// Tiny append-only diagnostic log used while debugging AI provider
    /// behavior invisibly (the app usually runs elevated/without console).
    /// Cross-OS: writes under LocalApplicationData (XDG-compliant on Linux,
    /// %LOCALAPPDATA% on Windows) — never a hard-coded machine path.
    /// </summary>
    public static class DiagLog
    {
        private static readonly string? FilePath;

        static DiagLog()
        {
            try
            {
                var root = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData,
                    Environment.SpecialFolderOption.DoNotVerify);
                var directory = Path.Combine(root, "RimSharp", "logs");
                Directory.CreateDirectory(directory);
                FilePath = Path.Combine(directory, "ai_retry_diag.txt");
            }
            catch
            {
                FilePath = null; // diagnostics must never crash the app
            }
        }

        public static void WriteLine(string message)
        {
            if (FilePath == null) return;
            try
            {
                File.AppendAllText(FilePath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Best effort only.
            }
        }
    }
}
