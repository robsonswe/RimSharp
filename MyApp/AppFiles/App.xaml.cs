// App.xaml.cs
using System;
using System.Net.Http; // <<< Keep this
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RimSharp.Features.ModManager.Services.Commands;
using RimSharp.Features.ModManager.Services.Data;
using RimSharp.Features.ModManager.Services.Filtering;
using RimSharp.Features.ModManager.Services.Management;
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
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;
using RimSharp.Infrastructure.Workshop;
using RimSharp.Infrastructure.Logging;
using RimSharp.Features.GitModManager.ViewModels;

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
            services.AddSingleton<ILoggerService, LoggerService>();
            services.AddSingleton<IConfigService, ConfigService>();
            services.AddSingleton<IDialogService, DialogService>();

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

            services.AddSingleton(provider =>
            {
                // Get the base path of the application
                var basePath = AppDomain.CurrentDomain.BaseDirectory;
                provider.GetRequiredService<ILoggerService>()?.LogDebug($"Application base path: {basePath}", "App.ConfigureServices");
                return basePath;
            });
            // --- Mod Rules ---
            services.AddSingleton<IModRulesRepository, JsonModRulesRepository>();
            services.AddSingleton<IModRulesService, ModRulesService>();
            services.AddSingleton<IModCustomService, ModCustomService>(provider =>
                new ModCustomService(provider.GetRequiredService<string>()));

            // --- Core Mod Services ---
            services.AddSingleton<IModService>(provider =>
                new ModService(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IModRulesService>(),
                    provider.GetRequiredService<IModCustomService>()
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
            // Register ViewModels - Use AddTransient unless you specifically need a singleton lifecycle
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

            // ************************************************
            // **************** FIX: REGISTER GitModsViewModel ****************
            // ************************************************
            services.AddTransient<GitModsViewModel>(provider =>
                new GitModsViewModel(
                    provider.GetRequiredService<IModService>(),
                    provider.GetRequiredService<IModListManager>()
                // Add any other dependencies GitModsViewModel might need here
                ));
            // ************************************************
            // ************************************************

            // MainViewModel should likely be Singleton as it holds the application's top-level state
            services.AddSingleton<MainViewModel>(provider =>
                new MainViewModel(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IConfigService>(),
                    provider.GetRequiredService<IDialogService>(),
                    provider.GetRequiredService<ModsViewModel>(),     // Resolve transient instance
                    provider.GetRequiredService<DownloaderViewModel>(), // Resolve transient instance
                    provider.GetRequiredService<GitModsViewModel>()   // <<< Resolve the now-registered GitModsViewModel
                ));

            // --- Application Shell ---
            // MainWindow is the root visual, usually a Singleton.
            services.AddSingleton<MainWindow>();

            // Log completion of service configuration
            // Cannot directly log here as the provider isn't built yet.
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            _logger.LogInfo("Application starting up (OnStartup entered).", "App.OnStartup");

            try
            {
                _logger.LogDebug("Attempting to retrieve MainWindow instance.", "App.OnStartup");
                // Resolve MainWindow AFTER services are configured and provider is built
                var mainWindow = ServiceProvider.GetRequiredService<MainWindow>(); // Use GetRequiredService

                if (mainWindow != null)
                {
                    _logger.LogDebug("MainWindow instance retrieved successfully. Assigning DataContext.", "App.OnStartup");

                    // ***************************************************************************
                    // *** Crucial: Set the DataContext for MainWindow AFTER resolving it ***
                    // ***************************************************************************
                    // The MainWindow needs its ViewModel (MainViewModel) to function correctly.
                    // Resolve MainViewModel and set it as the DataContext.
                    var mainViewModel = ServiceProvider.GetRequiredService<MainViewModel>();
                    mainWindow.DataContext = mainViewModel;
                    _logger.LogDebug("DataContext (MainViewModel) assigned to MainWindow.", "App.OnStartup");
                    // ***************************************************************************

                    _logger.LogDebug("Showing MainWindow.", "App.OnStartup");
                    mainWindow.Show();
                    _logger.LogInfo("MainWindow shown.", "App.OnStartup");

                    // Consider triggering initial data load *here* if needed, *after* Show()
                    // Example: await mainViewModel.InitializeApplicationAsync(); // If you add such a method
                }
                else
                {
                    // This case should ideally not happen if registration is correct, but keep logging.
                    _logger.LogCritical("Failed to retrieve MainWindow instance from Service Provider. Application cannot start.", "App.OnStartup");
                    MessageBox.Show("Critical error: Could not create the main application window. Check logs for details.", "Application Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown(-1);
                    return;
                }

                base.OnStartup(e);
                _logger.LogInfo("Application startup complete (OnStartup finished).", "App.OnStartup");
            }
            catch (Exception ex)
            {
                // Log the exception that occurred during startup (could be during DI resolution or Show())
                _logger.LogException(ex, "Unhandled exception during application startup.", "App.OnStartup");
                MessageBox.Show($"An critical error occurred during startup: {ex.Message}\n\nPlease check the application logs for more details.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-2);
            }
        }

        // --- Global Exception Handlers --- (Keep these as they are good practice)

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            _logger?.LogException(e.Exception, "Unhandled UI exception occurred.", "App.DispatcherUnhandled");
            // Check if MainWindow exists before trying to use DialogService potentially tied to it
            var dialogService = ServiceProvider?.GetService<IDialogService>(); // Use GetService for safety
            if (dialogService != null && Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                dialogService.ShowError("Unhandled Error", $"An unexpected error occurred: {e.Exception.Message}\n\nThe application may become unstable. Please check logs.");
            }
            else
            {
                MessageBox.Show($"An unexpected error occurred: {e.Exception.Message}\n\nThe application may become unstable. Please check logs.", "Unhandled Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            e.Handled = true; // Prevent WPF default crash dialog
            // Consider Shutdown(-3) depending on severity or if recovery isn't possible.
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                _logger?.LogException(ex, $"Unhandled non-UI exception occurred. IsTerminating: {e.IsTerminating}", "App.CurrentDomainUnhandled");
            }
            else
            {
                _logger?.LogCritical($"Unhandled non-UI exception occurred with non-exception object: {e.ExceptionObject}", "App.CurrentDomainUnhandled");
            }
            // Can't reliably show UI here. Logging is key.
            // If e.IsTerminating is true, the app is going down anyway.
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _logger?.LogInfo($"Application exiting with code {e.ApplicationExitCode}.", "App.OnExit");
            // Dispose IDisposable services if needed (though DI container might handle singletons)
            (ServiceProvider as IDisposable)?.Dispose();
            base.OnExit(e);
        }
    }
}