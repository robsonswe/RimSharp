// App.xaml.cs
using System;
using System.Net.Http; // <<< Keep this
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RimSharp.Features.ModManager.Services.Commands;
using RimSharp.Features.ModManager.Services.Data;
using RimSharp.Features.ModManager.Services.Filtering;
using RimSharp.Features.ModManager.Services.Mangement;
using RimSharp.Features.ModManager.ViewModels;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Features.WorkshopDownloader.ViewModels;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Dialog;
using RimSharp.Infrastructure.Mods.IO;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.MyApp.MainPage;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts; // <<< Add this using
using RimSharp.Shared.Services.Implementations;
using RimSharp.Infrastructure.Workshop;
using RimSharp.Infrastructure.Logging;

namespace RimSharp.MyApp.AppFiles
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
            // Register Logger FIRST so other services potentially could use it during construction (if needed via factory)
            // Although direct use isn't feasible here, it's good practice.
            services.AddSingleton<ILoggerService, LoggerService>();

            // --- Remaining service configurations ---
            services.AddSingleton<IConfigService, ConfigService>();
            // services.AddSingleton<ILoggerService, LoggerService>(); // Moved up
            services.AddSingleton<IDialogService, DialogService>();

            services.AddSingleton<IPathService, PathService>(provider =>
                new PathService(provider.GetRequiredService<IConfigService>()));

            services.AddSingleton(provider =>
            {
                var configService = provider.GetRequiredService<IConfigService>();
                // Log path settings retrieval
                var logger = provider.GetService<ILoggerService>(); // Use GetService for optional logging inside factory
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

            // --- Mod Rules ---
            services.AddSingleton<IModRulesRepository, JsonModRulesRepository>();
            services.AddSingleton<IModRulesService, ModRulesService>();

            // --- Core Mod Services ---
            services.AddSingleton<IModService>(provider =>
                new ModService(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IModRulesService>()
                ));
            services.AddSingleton<IModListManager, ModListManager>();
            services.AddSingleton<ModLookupService>();

            // --- Mod Manager Feature Services ---
            services.AddSingleton<IModDataService>(provider =>
                new ModDataService(
                    provider.GetRequiredService<IModService>(),
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IDialogService>()
                ));
            services.AddSingleton<IModFilterService, ModFilterService>();
            services.AddSingleton<IModCommandService, ModCommandService>();
            services.AddSingleton<IModListIOService, ModListIOService>();
            services.AddSingleton<IModIncompatibilityService, ModIncompatibilityService>();

            // --- Workshop Downloader Feature Services ---
            services.AddSingleton<IWebNavigationService, WebNavigationService>();
            services.AddSingleton<IDownloadQueueService, DownloadQueueService>();
            services.AddHttpClient(); // Registers IHttpClientFactory
            services.AddSingleton<ISteamApiClient, SteamApiClient>();
            services.AddSingleton<IWorkshopUpdateCheckerService, WorkshopUpdateCheckerService>();

            // --- SteamCMD Infrastructure ---
            services.AddSingleton<SteamCmdPlatformInfo>(); // Platform helper

            services.AddSingleton<ISteamCmdPathService>(provider =>
            {
                var configService = provider.GetRequiredService<IConfigService>();
                var platformInfo = provider.GetRequiredService<SteamCmdPlatformInfo>();
                return new SteamCmdPathService(configService, platformInfo.SteamCmdExeName);
            });

            services.AddSingleton<ISteamCmdFileSystem, SteamCmdFileSystem>();
            services.AddSingleton<ISteamCmdInstaller, SteamCmdInstaller>();
            services.AddSingleton<ISteamCmdDownloader, SteamCmdDownloader>();
            services.AddSingleton<ISteamCmdService, SteamCmdService>();

            // --- ViewModels ---
            // (No changes needed here unless ViewModels need logging during construction)
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
                    provider.GetRequiredService<IPathService>()
                ));

            services.AddTransient<DownloaderViewModel>(provider =>
                new DownloaderViewModel(
                    provider.GetRequiredService<IWebNavigationService>(),
                    provider.GetRequiredService<IDownloadQueueService>(),
                    provider.GetRequiredService<IModService>(),
                    provider.GetRequiredService<IDialogService>(),
                    provider.GetRequiredService<IWorkshopUpdateCheckerService>(),
                    provider.GetRequiredService<ISteamCmdService>(),
                    provider.GetRequiredService<IModListManager>()
                ));

            services.AddSingleton<MainViewModel>(provider =>
                new MainViewModel(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IConfigService>(),
                    provider.GetRequiredService<IDialogService>(),
                    provider.GetRequiredService<ModsViewModel>(),
                    provider.GetRequiredService<DownloaderViewModel>()
                ));

            // --- Application Shell ---
            services.AddSingleton<MainWindow>();

            // Log completion of service configuration
            // Cannot directly log here as the provider isn't built yet.
            // Logging is done after the provider is built in the constructor.
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            _logger.LogInfo("Application starting up (OnStartup entered).", "App.OnStartup");

            try
            {
                // Optional: Trigger path initialization explicitly if needed before UI loads
                // var steamCmdPathService = ServiceProvider.GetRequiredService<ISteamCmdPathService>();
                // _logger.LogDebug("Initializing SteamCMD paths.", "App.OnStartup");
                // steamCmdPathService.InitializePaths(); // Assuming InitializePaths exists and is needed here
                // _logger.LogDebug("SteamCMD paths initialized.", "App.OnStartup");

                _logger.LogDebug("Attempting to retrieve MainWindow instance.", "App.OnStartup");
                var mainWindow = ServiceProvider.GetService<MainWindow>();

                if (mainWindow != null)
                {
                    _logger.LogDebug("MainWindow instance retrieved successfully. Showing window.", "App.OnStartup");
                    mainWindow.Show();
                    _logger.LogInfo("MainWindow shown.", "App.OnStartup");
                }
                else
                {
                    _logger.LogCritical("Failed to retrieve MainWindow instance from Service Provider. Application cannot start.", "App.OnStartup");
                    // Optionally show a basic message box if DialogService failed or isn't usable yet
                    MessageBox.Show("Critical error: Could not create the main application window. Check logs for details.", "Application Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    // Shutdown the application gracefully if possible
                    Shutdown(-1); // Use a non-zero exit code for error
                    return; // Prevent base.OnStartup from running if shutdown is initiated
                }

                base.OnStartup(e);
                _logger.LogInfo("Application startup complete (OnStartup finished).", "App.OnStartup");
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Unhandled exception during application startup.", "App.OnStartup");
                // Try to show an error message before crashing
                 MessageBox.Show($"An critical error occurred during startup: {ex.Message}\n\nPlease check the application logs for more details.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // Ensure shutdown
                Shutdown(-2); // Different error code
            }
        }

        // --- Global Exception Handlers ---

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            _logger?.LogException(e.Exception, "Unhandled UI exception occurred.", "App.DispatcherUnhandled");
            // Optionally show user a message box (could use DialogService if it's reliable at this point)
            MessageBox.Show($"An unexpected error occurred: {e.Exception.Message}\n\nThe application may become unstable. Please check logs.", "Unhandled Error", MessageBoxButton.OK, MessageBoxImage.Error);
            // Prevent default WPF crash behavior
            e.Handled = true;
            // Consider if you need to shut down depending on the severity:
            // Shutdown(-3);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
             // This catches exceptions on non-UI threads
            if (e.ExceptionObject is Exception ex)
            {
                 _logger?.LogException(ex, $"Unhandled non-UI exception occurred. IsTerminating: {e.IsTerminating}", "App.CurrentDomainUnhandled");
            }
            else
            {
                _logger?.LogCritical($"Unhandled non-UI exception occurred with non-exception object: {e.ExceptionObject}", "App.CurrentDomainUnhandled");
            }
             // Log it, but can't do much else here as the app is likely terminating.
             // Avoid showing UI elements here as it might be on a background thread.
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _logger?.LogInfo($"Application exiting with code {e.ApplicationExitCode}.", "App.OnExit");
            base.OnExit(e);
            // Dispose resources if necessary (though DI container disposal handles most singletons)
        }
    }
}