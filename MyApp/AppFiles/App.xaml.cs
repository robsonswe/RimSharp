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
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;
using RimSharp.Infrastructure.Workshop;

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
            services.AddSingleton<IDialogService, DialogService>();

            services.AddSingleton<IPathService, PathService>(provider =>
                new PathService(provider.GetRequiredService<IConfigService>()));

            services.AddSingleton(provider =>
            {
                var configService = provider.GetRequiredService<IConfigService>();
                return new PathSettings
                {
                    GamePath = configService.GetConfigValue("game_folder"),
                    ModsPath = configService.GetConfigValue("mods_folder"),
                    ConfigPath = configService.GetConfigValue("config_folder"),
                    GameVersion = "1.5"
                };
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

            // --- SteamCMD Infrastructure --- <<<< ADD THIS SECTION >>>>
            services.AddSingleton<SteamCmdPlatformInfo>(); // Platform helper

            // Register ISteamCmdPathService with its factory because it needs platform info
            services.AddSingleton<ISteamCmdPathService>(provider =>
            {
                var configService = provider.GetRequiredService<IConfigService>();
                var platformInfo = provider.GetRequiredService<SteamCmdPlatformInfo>();
                return new SteamCmdPathService(configService, platformInfo.SteamCmdExeName);
            });

            // Register the other SteamCMD components (assuming they only need registered services)
            services.AddSingleton<ISteamCmdFileSystem, SteamCmdFileSystem>();
            services.AddSingleton<ISteamCmdInstaller, SteamCmdInstaller>();
            services.AddSingleton<ISteamCmdDownloader, SteamCmdDownloader>();

            // Register the Facade Service (now its dependencies can be resolved)
            services.AddSingleton<ISteamCmdService, SteamCmdService>();
            // --- End SteamCMD Infrastructure Section ---

            // --- ViewModels ---
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

            services.AddTransient<DownloaderViewModel>(provider =>
                new DownloaderViewModel(
                    provider.GetRequiredService<IWebNavigationService>(),
                    provider.GetRequiredService<IDownloadQueueService>(),
                    provider.GetRequiredService<IModService>(),
                    provider.GetRequiredService<IDialogService>(),
                    provider.GetRequiredService<IWorkshopUpdateCheckerService>(),
                    provider.GetRequiredService<ISteamCmdService>() // This should now resolve correctly
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
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Ensure the MainViewModel is created early if needed, otherwise MainWindow creation will trigger it.
            // var mainViewModel = ServiceProvider.GetRequiredService<MainViewModel>();

            var mainWindow = ServiceProvider.GetService<MainWindow>();
            mainWindow?.Show();
            base.OnStartup(e);

            // Initialize paths AFTER config is potentially loaded/available
            // Optional: Trigger path initialization here if needed before UI fully loads
            // var pathService = ServiceProvider.GetRequiredService<ISteamCmdPathService>();
            // pathService.InitializePaths();
        }
    }
}