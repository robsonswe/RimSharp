using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Dialog;
using RimSharp.Shared.Services.Implementations;
using RimSharp.Features.ModManager.Services.Mangement;
using RimSharp.Features.ModManager.Services.Data;
using RimSharp.Features.ModManager.Services.Filtering;
using RimSharp.Features.ModManager.Services.Commands;
using RimSharp.Infrastructure.Mods.IO;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Features.WorkshopDownloader.ViewModels;
using RimSharp.MyApp.MainPage;
using RimSharp.Features.ModManager.ViewModels;
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Shared.Models;
using System;

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

            // Register Rules Services
            services.AddSingleton<IModRulesRepository, JsonModRulesRepository>();
            services.AddSingleton<IModRulesService, ModRulesService>();

            // Register core services - Update ModService to include IModRulesService
            services.AddSingleton<IModService>(provider => 
                new ModService(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IModRulesService>()
                ));
            
            services.AddSingleton<IModListManager, ModListManager>();

            // Register modular services
            services.AddSingleton<IModDataService, ModDataService>();
            services.AddSingleton<IModFilterService, ModFilterService>();
            services.AddSingleton<IModCommandService, ModCommandService>();
            services.AddSingleton<IModListIOService, ModListIOService>();
            services.AddSingleton<ModLookupService>();
            services.AddSingleton<IModIncompatibilityService, ModIncompatibilityService>();

            // Register ViewModels with their dependencies
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

            services.AddSingleton<DownloaderViewModel>();

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