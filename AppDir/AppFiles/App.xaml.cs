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
using RimSharp.Infrastructure.Mods.Rules; // Needed for JsonModRulesRepository
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.AppDir.MainPage;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;
using RimSharp.Infrastructure.Workshop;
using RimSharp.Infrastructure.Logging;
using RimSharp.Features.GitModManager.ViewModels;

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

            // Register App Base Path as a singleton string
            services.AddSingleton<string>(provider =>
            {
                // Get the base path of the application
                var basePath = AppDomain.CurrentDomain.BaseDirectory;
                provider.GetRequiredService<ILoggerService>()?.LogDebug($"Application base path registered: {basePath}", "App.ConfigureServices");
                return basePath;
            });

            // --- Mod Rules ---
            // Updated: Inject appBasePath into JsonModRulesRepository constructor
            services.AddSingleton<IModRulesRepository>(provider =>
                new JsonModRulesRepository(
                    provider.GetRequiredService<string>() // Provide appBasePath
                ));
            services.AddSingleton<IModRulesService, ModRulesService>(); // Depends on IModRulesRepository

            // Updated: Inject appBasePath and Logger into ModCustomService constructor
            services.AddSingleton<IModCustomService>(provider =>
                new ModCustomService(
                    provider.GetRequiredService<string>(), // Provide appBasePath
                    provider.GetRequiredService<ILoggerService>() // Provide logger
                 ));

            services.AddSingleton<IMlieVersionService, MlieVersionService>();

            // ModReplacementService already correctly takes appBasePath and Logger
            services.AddSingleton<IModReplacementService, ModReplacementService>(provider =>
                new ModReplacementService(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<string>(), // Provide appBasePath
                    provider.GetRequiredService<ILoggerService>() // Provide logger
                ));

            // --- Core Mod Services ---
            // ModService dependencies are fine
            services.AddSingleton<IModService>(provider =>
                new ModService(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IModRulesService>(),
                    provider.GetRequiredService<IModCustomService>(),
                    provider.GetRequiredService<IMlieVersionService>(),
                    provider.GetRequiredService<ILoggerService>()
                ));
            services.AddSingleton<IModListManager, ModListManager>();
            services.AddSingleton<ModLookupService>();

            // --- Mod Manager Feature Services ---
            // Dependencies seem fine
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
            // Dependencies seem fine
            services.AddSingleton<IWebNavigationService, WebNavigationService>();
            services.AddSingleton<IDownloadQueueService, DownloadQueueService>();
            services.AddHttpClient(); // Registers IHttpClientFactory
            services.AddSingleton<ISteamApiClient, SteamApiClient>();
            services.AddSingleton<IWorkshopUpdateCheckerService, WorkshopUpdateCheckerService>();

            // --- SteamCMD Infrastructure ---
            // Dependencies seem fine
            services.AddSingleton<SteamCmdPlatformInfo>();
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
            // Register ViewModels - Use AddTransient unless specifically needed
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
                    provider.GetRequiredService<ISteamCmdService>(),
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
            _logger.LogInfo("Application starting up (OnStartup entered).", "App.OnStartup");

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

                base.OnStartup(e);
                _logger.LogInfo("Application startup complete (OnStartup finished).", "App.OnStartup");
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Unhandled exception during application startup.", "App.OnStartup");
                MessageBox.Show($"An critical error occurred during startup: {ex.Message}\n\nPlease check the application logs for more details.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-2);
            }
        }

        // --- Global Exception Handlers --- (Remain unchanged)

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            _logger?.LogException(e.Exception, "Unhandled UI exception occurred.", "App.DispatcherUnhandled");
            var dialogService = ServiceProvider?.GetService<IDialogService>();
            if (dialogService != null && Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                dialogService.ShowError("Unhandled Error", $"An unexpected error occurred: {e.Exception.Message}\n\nThe application may become unstable. Please check logs.");
            }
            else
            {
                MessageBox.Show($"An unexpected error occurred: {e.Exception.Message}\n\nThe application may become unstable. Please check logs.", "Unhandled Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            string message = "Unhandled non-UI exception occurred.";
            if (e.ExceptionObject is Exception ex)
            {
                 message = $"Unhandled non-UI exception occurred. IsTerminating: {e.IsTerminating}";
                _logger?.LogException(ex, message, "App.CurrentDomainUnhandled");
            }
            else
            {
                 message = $"Unhandled non-UI exception occurred with non-exception object: {e.ExceptionObject}";
                _logger?.LogCritical(message, "App.CurrentDomainUnhandled");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _logger?.LogInfo($"Application exiting with code {e.ApplicationExitCode}.", "App.OnExit");
            (ServiceProvider as IDisposable)?.Dispose();
            base.OnExit(e);
        }
    }
}
