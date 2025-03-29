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
        private readonly ServiceProvider _serviceProvider;
        
        public App()
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
        }
        
        private void ConfigureServices(IServiceCollection services)
        {
            // Register PathSettings as singleton with default values
            services.AddSingleton(new PathSettings
            {
                GamePath = @"C:\Games\RimWorld\",
                ModsPath = @"C:\Games\RimWorld\Mods\",
                ConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RimWorld"),
                GameVersion = "1.4"
            });

            services.AddSingleton<IModService, ModService>();
            services.AddSingleton<IPathService, PathService>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
        }
        
        protected override void OnStartup(StartupEventArgs e)
        {
            var mainWindow = _serviceProvider.GetService<MainWindow>();
            mainWindow?.Show();
            base.OnStartup(e);
        }
    }
}