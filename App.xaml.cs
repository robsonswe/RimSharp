using System.Windows;
using RimSharp.Views;
using RimSharp.Services;
using RimSharp.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using RimSharp.Models;
using RimSharp.ViewModels.Modules.Mods.Management;
using RimSharp.ViewModels.Modules.Mods.Data;
using RimSharp.ViewModels.Modules.Mods.Filtering;
using RimSharp.ViewModels.Modules.Mods.Commands;
using RimSharp.ViewModels.Modules.Mods.IO;
using RimSharp.ViewModels.Modules.Downloader;

namespace RimSharp
{
    public partial class App : Application
    {
        public IServiceProvider ServiceProvider { get; private set; }

        public App()
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();  // Store the provider
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Register ConfigService first as it's needed by PathSettings
            services.AddSingleton<IConfigService, ConfigService>();

            // Register PathService with dependency on ConfigService
            services.AddSingleton<IPathService, PathService>(provider =>
                new PathService(provider.GetRequiredService<IConfigService>()));

            // Register PathSettings with values from config
            services.AddSingleton(provider =>
            {
                var configService = provider.GetRequiredService<IConfigService>();
                return new PathSettings
                {
                    GamePath = configService.GetConfigValue("game_folder"),
                    ModsPath = configService.GetConfigValue("mods_folder"),
                    ConfigPath = configService.GetConfigValue("config_folder"),
                    GameVersion = "1.5" // Default version, can be updated later
                };
            });

            // Register core services
            services.AddSingleton<IModService, ModService>();
            services.AddSingleton<IModListManager, ViewModels.Modules.Mods.Management.ModListManager>();

            // Register new modular services
            services.AddSingleton<IModDataService, ViewModels.Modules.Mods.Data.ModDataService>();
            services.AddSingleton<IModFilterService, ViewModels.Modules.Mods.Filtering.ModFilterService>();
            services.AddSingleton<IModCommandService, ViewModels.Modules.Mods.Commands.ModCommandService>();
            services.AddSingleton<IModListIOService, ViewModels.Modules.Mods.IO.ModListIOService>();

            // Register ViewModels with their dependencies
            services.AddTransient<ViewModels.Modules.Mods.ModsViewModel>(provider =>
                new ViewModels.Modules.Mods.ModsViewModel(
                    provider.GetRequiredService<IModDataService>(),
                    provider.GetRequiredService<IModFilterService>(),
                    provider.GetRequiredService<IModCommandService>(),
                    provider.GetRequiredService<IModListIOService>(),
                    provider.GetRequiredService<IModListManager>()
                ));

            services.AddSingleton<DownloaderViewModel>();

            services.AddSingleton<MainViewModel>(provider =>
            new MainViewModel(
                provider.GetRequiredService<IModService>(),
                provider.GetRequiredService<IPathService>(),
                provider.GetRequiredService<IConfigService>(),
                provider.GetRequiredService<IModListManager>(),
                provider.GetRequiredService<IModDataService>(),
                provider.GetRequiredService<IModCommandService>(),
                provider.GetRequiredService<IModListIOService>()
            ));


            services.AddSingleton<MainWindow>();
        }


        protected override void OnStartup(StartupEventArgs e)
        {
            var mainWindow = ServiceProvider.GetService<MainWindow>();
            mainWindow?.Show();

            base.OnStartup(e);
        }
    }
}