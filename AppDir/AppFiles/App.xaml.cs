// App.xaml.cs
#nullable enable
using System;
using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

// --- Framework & App Structure ---
using RimSharp.AppDir.MainPage;
using RimSharp.AppDir.AppFiles;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Dialog;
using RimSharp.Infrastructure.Logging;
using RimSharp.Core.Extensions; // For ThreadHelper

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
using RimSharp.Features.WorkshopDownloader.Components.DownloadQueue; // Added for ModInfoEnricher (if needed here)

// --- Workshop Infrastructure (Refactored Namespaces) ---
using RimSharp.Infrastructure.Workshop;
using RimSharp.Infrastructure.Workshop.Core;
using RimSharp.Infrastructure.Workshop.Download;
using RimSharp.Infrastructure.Workshop.Download.Execution;
using RimSharp.Infrastructure.Workshop.Download.Parsing;
using RimSharp.Infrastructure.Workshop.Download.Processing;
using RimSharp.Infrastructure.Workshop.Download.Models;


namespace RimSharp.AppDir.AppFiles
{
    public partial class App : Application
    {
        public IServiceProvider ServiceProvider { get; private set; }
        private ILoggerService _logger;

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
            _logger?.LogInfo("Starting service configuration.", "App.ConfigureServices"); // Added log

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

            // --- Mod Dictionary Service --- // <<< ALREADY REGISTERED, GOOD
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
            services.AddSingleton<IModListManager>(provider => // Ensure Singleton if needed
            {
                _logger?.LogDebug("Creating IModListManager instance.", "App.ConfigureServices");
                return new ModListManager(
                    // Pass the required service instance
                    provider.GetRequiredService<IModDictionaryService>()
                );
            });
            services.AddSingleton<ModLookupService>();

            // --- Mod Manager Feature Services ---
            services.AddSingleton<IModDataService>(provider =>
                new ModDataService(
                    provider.GetRequiredService<IModService>(),
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IDialogService>()
                ));
            services.AddSingleton<IModFilterService, ModFilterService>();
            services.AddSingleton<IModCommandService, ModCommandService>(); // Make sure this is concrete

            // --- Workshop Downloader Feature Services ---
            services.AddSingleton<IWebNavigationService, WebNavigationService>();
            services.AddSingleton<IDownloadQueueService, DownloadQueueService>();
            services.AddHttpClient(); // Registers IHttpClientFactory
            services.AddSingleton<ISteamApiClient, SteamApiClient>();
            services.AddSingleton<IWorkshopUpdateCheckerService, WorkshopUpdateCheckerService>();
            services.AddSingleton<ISteamWorkshopQueueProcessor, SteamWorkshopQueueProcessor>(); // <<< ENSURE this uses correct dependencies if needed (ILoggerService, ISteamApiClient, IDownloadQueueService) - Constructor injection handles this if they are registered

            // --- ModListIOService Registration UPDATED ---
            services.AddSingleton<IModListIOService>(provider =>
            {
                _logger?.LogDebug("Creating IModListIOService instance.", "App.ConfigureServices");
                return new ModListIOService(
                   provider.GetRequiredService<IPathService>(),
                   provider.GetRequiredService<IModListManager>(),
                   provider.GetRequiredService<IDialogService>(),
                   // --- Inject the new dependencies ---
                   provider.GetRequiredService<IModDictionaryService>(),       // <<< INJECTED
                   provider.GetRequiredService<ISteamApiClient>(),           // <<< INJECTED
                   provider.GetRequiredService<IDownloadQueueService>(),     // <<< INJECTED
                   provider.GetRequiredService<IApplicationNavigationService>(),// <<< INJECTED
                   provider.GetRequiredService<ILoggerService>(),             // <<< INJECTED
                   provider.GetRequiredService<ISteamWorkshopQueueProcessor>()// <<< INJECTED
               );
            });
            // --- End ModListIOService Update ---

            services.AddSingleton<IModIncompatibilityService, ModIncompatibilityService>();


            // --- SteamCMD Infrastructure (Refactored) ---
            // Core Setup Components
            services.AddSingleton<SteamCmdPlatformInfo>();
            services.AddSingleton<ISteamCmdPathService>(provider =>
            {
                var configService = provider.GetRequiredService<IConfigService>();
                var platformInfo = provider.GetRequiredService<SteamCmdPlatformInfo>();
                return new SteamCmdPathService(configService, platformInfo.SteamCmdExeName);
            });
            services.AddSingleton<ISteamCmdFileSystem, SteamCmdFileSystem>();
            services.AddSingleton<ISteamCmdInstaller, SteamCmdInstaller>();

            // Download Components
            services.AddSingleton<ISteamCmdScriptGenerator, SteamCmdScriptGenerator>();
            services.AddSingleton<ISteamCmdProcessRunner, SteamCmdProcessRunner>();
            services.AddSingleton<ISteamCmdLogParser, SteamCmdLogParser>();
            services.AddSingleton<IDownloadedItemProcessor, DownloadedItemProcessor>();

            // SteamCmdDownloader (depends on Download components)
            services.AddSingleton<ISteamCmdDownloader>(provider =>
                new SteamCmdDownloader(
                    provider.GetRequiredService<ISteamCmdPathService>(),
                    provider.GetRequiredService<ISteamCmdInstaller>(),
                    provider.GetRequiredService<IDialogService>(),
                    provider.GetRequiredService<ILoggerService>(),
                    provider.GetRequiredService<IPathService>(), // Game path service
                    provider.GetRequiredService<ISteamCmdScriptGenerator>(),
                    provider.GetRequiredService<ISteamCmdProcessRunner>(),
                    provider.GetRequiredService<ISteamCmdLogParser>(),
                    provider.GetRequiredService<IDownloadedItemProcessor>()
                ));

            // SteamCmdService Facade (depends on Core and Downloader)
            services.AddSingleton<ISteamCmdService, SteamCmdService>();

            // --- ViewModels ---
            _logger?.LogDebug("Registering ViewModels.", "App.ConfigureServices");

            // Register ModInfoEnricher (assuming it's stateless enough for Singleton or Scoped, or use Transient)
            services.AddSingleton<ModInfoEnricher>(); // <<< ADDED

            services.AddTransient<ModsViewModel>(provider =>
               new ModsViewModel(
                   provider.GetRequiredService<IModDataService>(),
                   provider.GetRequiredService<IModFilterService>(),
                   provider.GetRequiredService<IModCommandService>(),
                   provider.GetRequiredService<IModListIOService>(), // <<< This now gets the updated one
                   provider.GetRequiredService<IModListManager>(),
                   provider.GetRequiredService<IModIncompatibilityService>(),
                   provider.GetRequiredService<IDialogService>(),
                   provider.GetRequiredService<IModService>(),
                   provider.GetRequiredService<IPathService>(),
                   provider.GetRequiredService<IModReplacementService>(),
                   provider.GetRequiredService<IDownloadQueueService>(),
                   provider.GetRequiredService<ISteamApiClient>(),
                   provider.GetRequiredService<IApplicationNavigationService>(),
                   provider.GetRequiredService<ISteamWorkshopQueueProcessor>()
               ));

            services.AddTransient<DownloaderViewModel>(provider =>
                new DownloaderViewModel(
                    provider.GetRequiredService<IWebNavigationService>(),
                    provider.GetRequiredService<IDownloadQueueService>(),
                    provider.GetRequiredService<IModService>(),
                    provider.GetRequiredService<IDialogService>(),
                    provider.GetRequiredService<IWorkshopUpdateCheckerService>(),
                    provider.GetRequiredService<ISteamCmdService>(),
                    provider.GetRequiredService<IModListManager>(),
                    provider.GetRequiredService<ModInfoEnricher>(),             // <<< INJECTED
                    provider.GetRequiredService<ISteamWorkshopQueueProcessor>(),// <<< INJECTED
                    provider.GetRequiredService<ILoggerService>(),              // <<< INJECTED
                    provider.GetRequiredService<ISteamApiClient>()
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
                    provider.GetRequiredService<ModsViewModel>(),       // <<< Resolves Transient
                    provider.GetRequiredService<DownloaderViewModel>(), // <<< Resolves Transient
                    provider.GetRequiredService<GitModsViewModel>()     // <<< Resolves Transient
                ));

            // --- Application Shell ---
            services.AddSingleton<MainWindow>();
            _logger?.LogInfo("Service configuration finished.", "App.ConfigureServices");
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e); // Call base startup first

            // --- Initialize ThreadHelper ---
            try
            {
                ThreadHelper.Initialize();
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

            _logger!.LogInfo("Application starting up (OnStartup entered).", "App.OnStartup");

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

                    // Set the static MainWindow reference if needed elsewhere (use cautiously)
                    // Application.Current.MainWindow = mainWindow;
                    // _logger?.LogDebug("Set Application.Current.MainWindow.", "App.OnStartup");

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

                _logger.LogInfo("Application startup complete (OnStartup finished).", "App.OnStartup");
            }
            catch (Exception ex)
            {
                _logger?.LogException(ex, "Unhandled exception during application startup.", "App.OnStartup");
                MessageBox.Show($"An critical error occurred during startup: {ex.Message}\n\nPlease check the application logs for more details.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-2);
            }
        }

        // --- Global Exception Handlers (Unchanged) ---

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            _logger?.LogException(e.Exception, "Unhandled UI exception occurred.", "App.DispatcherUnhandled");
            var dialogService = ServiceProvider?.GetService<IDialogService>();
            if (dialogService != null && Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                try { dialogService.ShowError("Unhandled Error", $"An unexpected error occurred: {e.Exception.Message}\n\nThe application may become unstable. Please check logs."); }
                catch { MessageBox.Show($"An unexpected error occurred: {e.Exception.Message}\n\nError displaying detailed message. Check logs.", "Unhandled Error", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
            else { MessageBox.Show($"An unexpected error occurred: {e.Exception.Message}\n\nThe application may become unstable. Please check logs.", "Unhandled Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            string message;
            Exception? ex = e.ExceptionObject as Exception;
            if (ex != null) { message = $"Unhandled non-UI exception occurred. IsTerminating: {e.IsTerminating}"; _logger?.LogException(ex, message, "App.CurrentDomainUnhandled"); }
            else { message = $"Unhandled non-UI exception occurred with non-exception object: {e.ExceptionObject}. IsTerminating: {e.IsTerminating}"; _logger?.LogCritical(message, "App.CurrentDomainUnhandled"); }
            MessageBox.Show($"A critical non-UI error occurred: {(ex?.Message ?? "Unknown error")}\n\nApplication will likely terminate. Please check logs.", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _logger?.LogInfo($"Application exiting with code {e.ApplicationExitCode}.", "App.OnExit");
            (ServiceProvider as IDisposable)?.Dispose();
            base.OnExit(e);
        }
    }
}
