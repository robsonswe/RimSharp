// App.xaml.cs
using System;
using System.Net.Http; // Keep this
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

// --- Framework & App Structure ---
using RimSharp.AppDir.MainPage;
using RimSharp.AppDir.AppFiles; // Correct namespace for App itself
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Dialog;
using RimSharp.Infrastructure.Logging;
using RimSharp.Core.Extensions; // <<< ADDED for ThreadHelper

// --- Shared Contracts & Models ---
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;

// --- Mod Manager Feature ---
using RimSharp.Features.ModManager.Services.Commands;
using RimSharp.Features.ModManager.Services.Data;
using RimSharp.Features.ModManager.Services.Filtering;
using RimSharp.Features.ModManager.Services.Management;
using RimSharp.Features.ModManager.ViewModels;
using RimSharp.Infrastructure.Mods.IO;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;

// --- Git Mod Manager Feature ---
using RimSharp.Features.GitModManager.ViewModels;

// --- Workshop Downloader Feature ---
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Features.WorkshopDownloader.ViewModels;

// --- Workshop Infrastructure (Refactored Namespaces) ---
using RimSharp.Infrastructure.Workshop;              // For SteamCmdService itself
using RimSharp.Infrastructure.Workshop.Core;        // For PlatformInfo, PathService, FileSystem, Installer interfaces/classes
using RimSharp.Infrastructure.Workshop.Download;    // For Downloader interface/class
using RimSharp.Infrastructure.Workshop.Download.Execution; // For Runner, ScriptGenerator interfaces/classes
using RimSharp.Infrastructure.Workshop.Download.Parsing;   // For LogParser interface/class
using RimSharp.Infrastructure.Workshop.Download.Processing;// For ItemProcessor interface/class
// --- IMPORTANT: Keep this if SteamCmdDownloadResult stays here ---
using RimSharp.Infrastructure.Workshop.Download.Models;  // For SteamCmdDownloadResult DTO

namespace RimSharp.AppDir.AppFiles
{
    public partial class App : Application
    {
        public IServiceProvider ServiceProvider { get; private set; }
        private ILoggerService _logger; // Store logger instance

        public App()
        {
            // Initialize services FIRST, including the logger
            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();

            // Get the logger service AFTER the provider is built
            _logger = ServiceProvider.GetRequiredService<ILoggerService>();
            _logger.LogInfo("Application object created. Service Provider built.", "App");

            // Optional: Setup global exception handler
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // --- Base Infrastructure & Configuration ---
            services.AddSingleton<ILoggerService, LoggerService>();
            services.AddSingleton<IConfigService, ConfigService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IApplicationNavigationService, ApplicationNavigationService>();

            services.AddSingleton<IPathService, PathService>(provider =>
                new PathService(provider.GetRequiredService<IConfigService>()));

            services.AddSingleton(provider =>
            {
                var configService = provider.GetRequiredService<IConfigService>();
                var logger = provider.GetService<ILoggerService>();
                logger?.LogDebug("Retrieving path settings from config.", "App.ConfigureServices");
                var pathSettings = new PathSettings
                {
                    GamePath = configService.GetConfigValue("game_folder"),
                    ModsPath = configService.GetConfigValue("mods_folder"),
                    ConfigPath = configService.GetConfigValue("config_folder"),
                    GameVersion = "1.5" // Consider making this configurable too
                };
                logger?.LogDebug($"Path Settings retrieved: Game='{pathSettings.GamePath}', Mods='{pathSettings.ModsPath}', Config='{pathSettings.ConfigPath}'", "App.ConfigureServices");
                return pathSettings;
            });

            services.AddSingleton<string>(provider =>
            {
                var basePath = AppDomain.CurrentDomain.BaseDirectory;
                provider.GetRequiredService<ILoggerService>()?.LogDebug($"Application base path registered: {basePath}", "App.ConfigureServices");
                return basePath;
            });

            // --- Mod Rules ---
            services.AddSingleton<IModRulesRepository>(provider =>
                new JsonModRulesRepository(provider.GetRequiredService<string>())); // appBasePath
            services.AddSingleton<IModRulesService, ModRulesService>();
            services.AddSingleton<IModCustomService>(provider =>
                new ModCustomService(
                    provider.GetRequiredService<string>(), // appBasePath
                    provider.GetRequiredService<ILoggerService>()
                 ));
            services.AddSingleton<IMlieVersionService, MlieVersionService>();
            services.AddSingleton<IModReplacementService>(provider =>
                new ModReplacementService(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<string>(), // appBasePath
                    provider.GetRequiredService<ILoggerService>()
                ));

            // --- Mod Dictionary Service --- // <<< ADDED Registration
            services.AddSingleton<IModDictionaryService>(provider =>
                new ModDictionaryService(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<string>(), // appBasePath
                    provider.GetRequiredService<ILoggerService>()
                ));


            // --- Core Mod Services ---
            services.AddSingleton<IModService>(provider =>
                new ModService(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IModRulesService>(),
                    provider.GetRequiredService<IModCustomService>(),
                    provider.GetRequiredService<IMlieVersionService>(),
                    provider.GetRequiredService<ILoggerService>()
                ));
            services.AddSingleton<IModListManager, ModListManager>();
            services.AddSingleton<ModLookupService>(); // Assuming concrete type needed directly

            // --- Mod Manager Feature Services ---
            services.AddSingleton<IModDataService>(provider =>
                new ModDataService(
                    provider.GetRequiredService<IModService>(),
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IDialogService>()
                ));
            services.AddSingleton<IModFilterService, ModFilterService>();
            services.AddSingleton<IModCommandService, ModCommandService>();

            // --- ModListIOService Registration UPDATED ---
            services.AddSingleton<IModListIOService>(provider =>
                new ModListIOService(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IModListManager>(),
                    provider.GetRequiredService<IDialogService>(),
                    // --- Add the new dependencies ---
                    provider.GetRequiredService<IModDictionaryService>(), // <<< ADDED
                    provider.GetRequiredService<ISteamApiClient>(),       // <<< ADDED
                    provider.GetRequiredService<IDownloadQueueService>(), // <<< ADDED
                    provider.GetRequiredService<IApplicationNavigationService>(), // <<< ADDED
                    provider.GetRequiredService<ILoggerService>()         // <<< ADDED
                ));
            // --- End ModListIOService Update ---

            services.AddSingleton<IModIncompatibilityService, ModIncompatibilityService>();


            // --- Workshop Downloader Feature Services ---
            services.AddSingleton<IWebNavigationService, WebNavigationService>();
            services.AddSingleton<IDownloadQueueService, DownloadQueueService>();
            services.AddHttpClient(); // Registers IHttpClientFactory
            services.AddSingleton<ISteamApiClient, SteamApiClient>();
            services.AddSingleton<IWorkshopUpdateCheckerService, WorkshopUpdateCheckerService>();

            // --- SteamCMD Infrastructure (Refactored) ---
            // Core Setup Components
            services.AddSingleton<SteamCmdPlatformInfo>(); // Lives in Core
            services.AddSingleton<ISteamCmdPathService>(provider => // Lives in Core
            {
                var configService = provider.GetRequiredService<IConfigService>();
                var platformInfo = provider.GetRequiredService<SteamCmdPlatformInfo>();
                // SteamCmdPathService constructor takes IConfigService, exeName
                return new SteamCmdPathService(configService, platformInfo.SteamCmdExeName);
            });
            services.AddSingleton<ISteamCmdFileSystem, SteamCmdFileSystem>(); // Lives in Core
            services.AddSingleton<ISteamCmdInstaller, SteamCmdInstaller>(); // Lives in Core

            // NEW: Register components from Download sub-namespaces
            services.AddSingleton<ISteamCmdScriptGenerator, SteamCmdScriptGenerator>(); // Lives in Download.Execution
            services.AddSingleton<ISteamCmdProcessRunner, SteamCmdProcessRunner>();     // Lives in Download.Execution
            services.AddSingleton<ISteamCmdLogParser, SteamCmdLogParser>();             // Lives in Download.Parsing
            services.AddSingleton<IDownloadedItemProcessor, DownloadedItemProcessor>(); // Lives in Download.Processing

            // UPDATED: SteamCmdDownloader registration (lives in Download)
            // Needs explicit factory due to many dependencies, including the new ones above
            services.AddSingleton<ISteamCmdDownloader>(provider =>
                new SteamCmdDownloader(
                    provider.GetRequiredService<ISteamCmdPathService>(),
                    provider.GetRequiredService<ISteamCmdInstaller>(),
                    provider.GetRequiredService<IDialogService>(),
                    provider.GetRequiredService<ILoggerService>(),
                    provider.GetRequiredService<IPathService>(), // Game path service
                    provider.GetRequiredService<ISteamCmdScriptGenerator>(), // New dependency
                    provider.GetRequiredService<ISteamCmdProcessRunner>(),     // New dependency
                    provider.GetRequiredService<ISteamCmdLogParser>(),         // New dependency
                    provider.GetRequiredService<IDownloadedItemProcessor>()    // New dependency
                ));

            // SteamCmdService Facade (lives at Workshop root)
            // Constructor takes Path, Installer, Downloader, FileSystem interfaces
            // Simple registration should work now that its dependencies are registered
            services.AddSingleton<ISteamCmdService, SteamCmdService>();

            // --- ViewModels ---
            // Register ViewModels - Use AddTransient unless specifically needed as singleton
             services.AddTransient<ModsViewModel>(provider =>
               new ModsViewModel(
                   provider.GetRequiredService<IModDataService>(),
                   provider.GetRequiredService<IModFilterService>(),
                   provider.GetRequiredService<IModCommandService>(),
                   provider.GetRequiredService<IModListIOService>(),
                   provider.GetRequiredService<IModListManager>(),
                   provider.GetRequiredService<IModIncompatibilityService>(),
                   provider.GetRequiredService<IDialogService>(),
                   provider.GetRequiredService<IModService>(),
                   provider.GetRequiredService<IPathService>(),
                   provider.GetRequiredService<IModReplacementService>(),
                   provider.GetRequiredService<IDownloadQueueService>(),
                   provider.GetRequiredService<ISteamApiClient>(),
                   provider.GetRequiredService<IApplicationNavigationService>()
               ));

            services.AddTransient<DownloaderViewModel>(provider =>
                new DownloaderViewModel(
                    provider.GetRequiredService<IWebNavigationService>(),
                    provider.GetRequiredService<IDownloadQueueService>(),
                    provider.GetRequiredService<IModService>(),
                    provider.GetRequiredService<IDialogService>(),
                    provider.GetRequiredService<IWorkshopUpdateCheckerService>(),
                    provider.GetRequiredService<ISteamCmdService>(), // Depends on the facade
                    provider.GetRequiredService<IModListManager>()
                ));

            services.AddTransient<GitModsViewModel>(provider =>
                new GitModsViewModel(
                    provider.GetRequiredService<IModService>(),
                    provider.GetRequiredService<IModListManager>(),
                    provider.GetRequiredService<IDialogService>()
                ));

            // MainViewModel holds top-level state - Singleton
            services.AddSingleton<MainViewModel>(provider =>
                new MainViewModel(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IConfigService>(),
                    provider.GetRequiredService<IDialogService>(),
                    provider.GetRequiredService<IApplicationNavigationService>(),
                    provider.GetRequiredService<ModsViewModel>(),
                    provider.GetRequiredService<DownloaderViewModel>(),
                    provider.GetRequiredService<GitModsViewModel>()
                ));

            // --- Application Shell ---
            services.AddSingleton<MainWindow>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e); // Call base startup first

            // --- Initialize ThreadHelper ---
            try
            {
                ThreadHelper.Initialize(); // <<< Initialized here
                _logger?.LogInfo("ThreadHelper initialized.", "App.OnStartup");
            }
            catch (Exception initEx)
            {
                _logger?.LogException(initEx, "Failed to initialize ThreadHelper.", "App.OnStartup");
                MessageBox.Show($"Critical error initializing threading: {initEx.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-3);
                return;
            }
            // --- End ThreadHelper Initialization ---

            _logger.LogInfo("Application starting up (OnStartup entered).", "App.OnStartup"); // Keep existing log

            try
            {
                _logger.LogDebug("Attempting to retrieve MainWindow instance.", "App.OnStartup");
                var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();

                if (mainWindow != null)
                {
                    _logger.LogDebug("MainWindow instance retrieved successfully. Assigning DataContext.", "App.OnStartup");
                    var mainViewModel = ServiceProvider.GetRequiredService<MainViewModel>();
                    mainWindow.DataContext = mainViewModel;
                    _logger.LogDebug("DataContext (MainViewModel) assigned to MainWindow.", "App.OnStartup");

                    _logger.LogDebug("Showing MainWindow.", "App.OnStartup");
                    mainWindow.Show();
                    _logger.LogInfo("MainWindow shown.", "App.OnStartup");
                }
                else
                {
                    _logger.LogCritical("Failed to retrieve MainWindow instance from Service Provider. Application cannot start.", "App.OnStartup");
                    MessageBox.Show("Critical error: Could not create the main application window. Check logs for details.", "Application Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown(-1);
                    return;
                }

                // base.OnStartup(e); // <<< MOVED TO THE TOP
                _logger.LogInfo("Application startup complete (OnStartup finished).", "App.OnStartup");
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Unhandled exception during application startup.", "App.OnStartup");
                MessageBox.Show($"An critical error occurred during startup: {ex.Message}\n\nPlease check the application logs for more details.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-2);
            }
        }

        // --- Global Exception Handlers ---

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            _logger?.LogException(e.Exception, "Unhandled UI exception occurred.", "App.DispatcherUnhandled");
            var dialogService = ServiceProvider?.GetService<IDialogService>();
            // Avoid showing dialog if MainWindow isn't visible or available (e.g., during early startup crash)
            if (dialogService != null && Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                 try
                 {
                    dialogService.ShowError("Unhandled Error", $"An unexpected error occurred: {e.Exception.Message}\n\nThe application may become unstable. Please check logs.");
                 }
                 catch(Exception /*dialogEx*/)
                 {
                     // Fallback if even the dialog service fails
                    MessageBox.Show($"An unexpected error occurred: {e.Exception.Message}\n\nError displaying detailed message. Check logs.", "Unhandled Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                 }
            }
            else // Fallback message box
            {
                MessageBox.Show($"An unexpected error occurred: {e.Exception.Message}\n\nThe application may become unstable. Please check logs.", "Unhandled Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            // Decide whether to terminate. For many UI errors, allowing continuation might be possible.
            // For critical errors, consider Shutdown(). For now, we just handle it.
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            string message;
            Exception? ex = e.ExceptionObject as Exception;

            if (ex != null)
            {
                 message = $"Unhandled non-UI exception occurred. IsTerminating: {e.IsTerminating}";
                _logger?.LogException(ex, message, "App.CurrentDomainUnhandled");
            }
            else
            {
                 message = $"Unhandled non-UI exception occurred with non-exception object: {e.ExceptionObject}. IsTerminating: {e.IsTerminating}";
                _logger?.LogCritical(message, "App.CurrentDomainUnhandled");
            }

            // Optionally show a message box, but be careful as this is often on a background thread
             MessageBox.Show($"A critical non-UI error occurred: {(ex?.Message ?? "Unknown error")}\n\nApplication will likely terminate. Please check logs.", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);

            // If e.IsTerminating is true, the CLR is already shutting down.
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _logger?.LogInfo($"Application exiting with code {e.ApplicationExitCode}.", "App.OnExit");
            // Dispose the service provider if it implements IDisposable (standard practice)
            (ServiceProvider as IDisposable)?.Dispose();
            base.OnExit(e);
        }
    }
}