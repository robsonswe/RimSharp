using System;
using Avalonia;
using Avalonia.ReactiveUI;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.FontAwesome;
using Projektanker.Icons.Avalonia.MaterialDesign;
using RimSharp.AppDir.AppFiles;

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
                System.IO.File.WriteAllText("fatal_crash.txt", ex.ToString());
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
                .UseReactiveUI();
        }
    }
}

