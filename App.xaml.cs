using System.Windows;
using RimSharp.Views;
using RimSharp.Services;
using RimSharp.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using RimSharp.Models;

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
            
            // Register PathSettings with values from config
            services.AddSingleton(provider => 
            {
                var configService = provider.GetRequiredService<IConfigService>();
                return new PathSettings
                {
                    GamePath = configService.GetConfigValue("game_folder"),
                    ModsPath = configService.GetConfigValue("mods_folder"),
                    ConfigPath = configService.GetConfigValue("config_folder"),
                    GameVersion = "1.4" // This will be updated later
                };
            });

            services.AddSingleton<IModService, ModService>();
            services.AddSingleton<IPathService, PathService>();
            services.AddSingleton<MainViewModel>();
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