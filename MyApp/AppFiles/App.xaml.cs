using System;
using System.Net.Http; // <<< ADDED for HttpClient
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RimSharp.Features.ModManager.Services.Commands;
using RimSharp.Features.ModManager.Services.Data;
using RimSharp.Features.ModManager.Services.Filtering;
using RimSharp.Features.ModManager.Services.Mangement;
using RimSharp.Features.ModManager.ViewModels;
using RimSharp.Features.WorkshopDownloader.Services; // <<< ADDED for Workshop services
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

namespace RimSharp.MyApp.AppFiles
{
    public partial class App : Application
    {
        public IServiceProvider ServiceProvider { get; private set; }

        public App()
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // --- Base Infrastructure & Configuration ---
            services.AddSingleton<IConfigService, ConfigService>();
            services.AddSingleton<IDialogService, DialogService>(); // Dialogs (used by many)

            // PathService depends on ConfigService
            services.AddSingleton<IPathService, PathService>(provider =>
                new PathService(provider.GetRequiredService<IConfigService>()));

            // PathSettings (not directly injected often, but PathService uses it internally)
            services.AddSingleton(provider =>
            {
                var configService = provider.GetRequiredService<IConfigService>();
                return new PathSettings
                {
                    GamePath = configService.GetConfigValue("game_folder"),
                    ModsPath = configService.GetConfigValue("mods_folder"),
                    ConfigPath = configService.GetConfigValue("config_folder"),
                    GameVersion = "1.5" // Consider making this dynamic later
                };
            });

            // --- Mod Rules ---
            services.AddSingleton<IModRulesRepository, JsonModRulesRepository>(); // Or use Factory if needed
            services.AddSingleton<IModRulesService, ModRulesService>();

            // --- Core Mod Services ---
            // ModService depends on PathService and RulesService
            services.AddSingleton<IModService>(provider =>
                new ModService(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IModRulesService>()
                ));

            // ModListManager (and its internal helpers)
            services.AddSingleton<IModListManager, ModListManager>(); // Manages active/inactive lists
            services.AddSingleton<ModLookupService>(); // Helper for ModListManager (if needed elsewhere)

            // --- Mod Manager Feature Services ---
            // ModDataService depends on ModService, PathService, DialogService
            services.AddSingleton<IModDataService>(provider =>
                new ModDataService(
                    provider.GetRequiredService<IModService>(),
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IDialogService>() // Added DialogService dependency
                ));
            services.AddSingleton<IModFilterService, ModFilterService>();
            services.AddSingleton<IModCommandService, ModCommandService>(); // Handles mod actions (activate, etc.)
            services.AddSingleton<IModListIOService, ModListIOService>(); // Handles saving/loading mod lists
            services.AddSingleton<IModIncompatibilityService, ModIncompatibilityService>();

            // --- Workshop Downloader Feature Services ---
            services.AddSingleton<IWebNavigationService, WebNavigationService>();
            services.AddSingleton<IDownloadQueueService, DownloadQueueService>();

            // Register HttpClient and API Client (for Check Updates)
            services.AddHttpClient(); // Registers IHttpClientFactory and related services
            services.AddSingleton<ISteamApiClient, SteamApiClient>(); // Uses HttpClient/Factory

            // Register Update Checker Service (for Check Updates)
            services.AddSingleton<IWorkshopUpdateCheckerService, WorkshopUpdateCheckerService>();


            // --- ViewModels ---

            // ModsViewModel (Mod Manager Tab)
            services.AddTransient<ModsViewModel>(provider =>
                new ModsViewModel(
                    provider.GetRequiredService<IModDataService>(),
                    provider.GetRequiredService<IModFilterService>(),
                    provider.GetRequiredService<IModCommandService>(),
                    provider.GetRequiredService<IModListIOService>(),
                    provider.GetRequiredService<IModListManager>(),
                    provider.GetRequiredService<IModIncompatibilityService>(),
                    provider.GetRequiredService<IDialogService>(),
                    provider.GetRequiredService<IModService>()
                ));

            // DownloaderViewModel (Workshop Downloader Tab) - <<< UPDATED
            services.AddTransient<DownloaderViewModel>(provider =>
                new DownloaderViewModel(
                    provider.GetRequiredService<IWebNavigationService>(),
                    provider.GetRequiredService<IDownloadQueueService>(),
                    provider.GetRequiredService<IModService>(),          // Added
                    provider.GetRequiredService<IDialogService>(),         // Added
                    provider.GetRequiredService<IWorkshopUpdateCheckerService>() // Added
                ));

            // MainViewModel (Shell) - Ensure it gets the required ViewModels/Services
                     services.AddSingleton<MainViewModel>(provider =>
            new MainViewModel(
                // Inject services MainViewModel DIRECTLY depends on
                provider.GetRequiredService<IPathService>(),
                provider.GetRequiredService<IConfigService>(),
                provider.GetRequiredService<IDialogService>(),

                // Inject the already-configured sub-viewmodels
                provider.GetRequiredService<ModsViewModel>(),
                provider.GetRequiredService<DownloaderViewModel>()
            ));

             // --- NOTE: MainViewModel constructor signature might need adjustment based on how it uses ModsViewModel/DownloaderViewModel ---
             // If MainViewModel just holds references to them, the above works.
             // If it needs OTHER services that those ViewModels ALSO need, inject them directly into MainViewModel too.
             // The previous MainViewModel registration looked overly complex, injecting almost everything. Simplify if possible.


            // --- Application Shell ---
            services.AddSingleton<MainWindow>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            var mainWindow = ServiceProvider.GetService<MainWindow>();
            // Optionally resolve and set the DataContext here if not done in MainWindow constructor/XAML
             // mainWindow.DataContext = ServiceProvider.GetService<MainViewModel>();
            mainWindow?.Show();
            base.OnStartup(e);
        }
    }
}