using System;
using Avalonia;
using ReactiveUI.Avalonia;
using Optris.Icons.Avalonia;
using Optris.Icons.Avalonia.FontAwesome;
using Optris.Icons.Avalonia.MaterialDesign;
using RimSharp.AppDir.AppFiles;
using RimSharp.Infrastructure.Configuration;

namespace RimSharp
{
    class Program
    {

[STAThread]
        public static void Main(string[] args)
        {
            try
            {
                BuildAvaloniaApp()
                    .StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                // Last resort logging
                Console.WriteLine($"FATAL ERROR: {ex}");
                TryWriteFatalCrashDump(ex);
            }
        }

        /// <summary>
        /// Best-effort crash dump. Windows keeps the historical CWD write so
        /// behavior stays byte-identical; Linux prefers the XDG data
        /// directory and falls back to the CWD if that cannot be created.
        /// </summary>
        private static void TryWriteFatalCrashDump(Exception ex)
        {
            if (OperatingSystem.IsWindows())
            {
                try { System.IO.File.WriteAllText("fatal_crash.txt", ex.ToString()); } catch { }
                return;
            }

            try
            {
                var dataDirectory = AppDataPaths.EnsureDataDirectory();
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(dataDirectory, "fatal_crash.txt"),
                    ex.ToString());
            }
            catch
            {
                try { System.IO.File.WriteAllText("fatal_crash.txt", ex.ToString()); }
                catch { /* Nothing more we can do. */ }
            }
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            IconProvider.Current
                .Register<FontAwesomeIconProvider>()
                .Register<MaterialDesignIconProvider>();

            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI(_ => { });
        }
    }
}

