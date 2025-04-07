using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Dialog;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Implementations;
using RimSharp.Features.ModManager.Services.Mangement;
using RimSharp.Features.ModManager.Services.Data;
using RimSharp.Features.ModManager.Services.Filtering;
using RimShaRimSharp.Features.ModManager.Services.Commands;
using RimSharp.Infrastructure.Mods.IO;
using RimSharp.Features.WorkshopDownloader.ViewModels;
using RimSharp.MyApp.MainPage;
using RimSharp.Features.ModManager.ViewModels;
using RimShaRimSharp.Infrastructure.Mods.Validation.Incompatibilities;

namespace RimSharp.MyApp.AppFiles
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
            services.AddSingleton<IDialogService, DialogService>();

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
            services.AddSingleton<IModListManager, ModListManager>();

            // Register new modular services
            services.AddSingleton<IModDataService, ModDataService>();
            services.AddSingleton<IModFilterService, ModFilterService>(); // Ensure this is registered
            services.AddSingleton<IModCommandService, ModCommandService>();
            services.AddSingleton<IModListIOService, ModListIOService>();
            services.AddSingleton<ModLookupService>();

            // Register IModIncompatibilityService
            services.AddSingleton<IModIncompatibilityService, ModIncompatibilityService>();

            // Register ViewModels with their dependencies
            // Transient for ModsViewModel might be okay, or Singleton if you prefer it keeps state across tab switches
            services.AddTransient<ModsViewModel>(provider =>
                new ModsViewModel(
                    provider.GetRequiredService<IModDataService>(),
                    provider.GetRequiredService<IModFilterService>(), // Use the registered service
                    provider.GetRequiredService<IModCommandService>(),
                    provider.GetRequiredService<IModListIOService>(),
                    provider.GetRequiredService<IModListManager>(),
                    provider.GetRequiredService<IModIncompatibilityService>(),
                    provider.GetRequiredService<IDialogService>(),
                    provider.GetRequiredService<IModService>() 
                ));

            services.AddSingleton<DownloaderViewModel>(); // Assuming it needs no constructor args for now

            // Register MainViewModel as Singleton
            services.AddSingleton<MainViewModel>(provider =>
            new MainViewModel(
                provider.GetRequiredService<IModService>(),
                provider.GetRequiredService<IPathService>(),
                provider.GetRequiredService<IConfigService>(),
                provider.GetRequiredService<IModListManager>(),
                provider.GetRequiredService<IModDataService>(),
                provider.GetRequiredService<IModCommandService>(),
                provider.GetRequiredService<IModListIOService>(),
                provider.GetRequiredService<IModIncompatibilityService>(),
                provider.GetRequiredService<IDialogService>(),
                provider.GetRequiredService<IModFilterService>()
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