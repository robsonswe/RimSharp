// App.axaml.cs
#nullable enable
using System;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

// --- Framework & App Structure ---
using RimSharp.AppDir.MainPage;
using RimSharp.AppDir.AppFiles;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Dialog;
using RimSharp.Infrastructure.Logging;
using RimSharp.Infrastructure.Data;
using RimSharp.Core.Extensions; // For ThreadHelper

// --- Shared Contracts & Models ---
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;

// --- Mod Manager Feature ---
using RimSharp.Features.ModManager.Services.Commands;
using RimSharp.Features.ModManager.Services.Data;
using RimSharp.Features.ModManager.Services.Filtering;
using RimSharp.Features.ModManager.Services.Management;
using RimSharp.Features.ModManager.ViewModels;
using RimSharp.Infrastructure.Mods.IO;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Infrastructure.Mods.Validation.Duplicates;

// --- Git Mod Manager Feature ---
using RimSharp.Features.GitModManager.ViewModels;

// --- VRAM Usage Feature ---
using RimSharp.Features.VramAnalysis.ViewModels;

// --- Workshop Downloader Feature ---
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Features.WorkshopDownloader.ViewModels;
using RimSharp.Features.WorkshopDownloader.Components.DownloadQueue;

// --- Workshop Infrastructure ---
using RimSharp.Infrastructure.Workshop;
using RimSharp.Infrastructure.Workshop.Core;
using RimSharp.Infrastructure.Workshop.Download;
using RimSharp.Infrastructure.Workshop.Download.Execution;
using RimSharp.Infrastructure.Workshop.Download.Parsing;
using RimSharp.Infrastructure.Workshop.Download.Processing;
using RimSharp.Infrastructure.Workshop.Download.Models;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.AppDir.Dialogs;
using System.Diagnostics;
using System.Linq;
using PInvoke;

// --- Core Services ---
using RimSharp.Core.Services;
using Avalonia.Threading;

namespace RimSharp.AppDir.AppFiles
{
    public partial class App : Application
    {
        private const string AppUniqueId = "RimSharp_SingleInstance_Mutex_9921_1234";
        private static Mutex? _instanceMutex;
        private static bool _ownsMutex = false;

        public IServiceProvider? ServiceProvider { get; private set; }
        private ILoggerService? _logger;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // --- Single Instance Check ---
                bool isNewInstance;
                _instanceMutex = new Mutex(true, AppUniqueId, out isNewInstance);
                _ownsMutex = isNewInstance;

                if (!isNewInstance)
                {
                    Debug.WriteLine("App is already running. Activating existing instance.");
                    // App is already running. 
                    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                    {
                        Process current = Process.GetCurrentProcess();
                        foreach (Process process in Process.GetProcessesByName(current.ProcessName))
                        {
                            if (process.Id != current.Id && process.MainWindowHandle != IntPtr.Zero)
                            {
                                User32.ShowWindow(process.MainWindowHandle, User32.WindowShowStyle.SW_RESTORE);
                                User32.SetForegroundWindow(process.MainWindowHandle);
                                break;
                            }
                        }
                    }
                    desktop.Shutdown();
                    return;
                }

                // Initialize services
                var services = new ServiceCollection();
                ConfigureServices(services);
                ServiceProvider = services.BuildServiceProvider();
                _logger = ServiceProvider.GetRequiredService<ILoggerService>();

                ThreadHelper.Initialize();
                _logger.LogInfo("Application starting up - Initializing UI Components", "App.OnFrameworkInitializationCompleted");

                var mainViewModel = ServiceProvider.GetRequiredService<MainViewModel>();
                var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                mainWindow.DataContext = mainViewModel;

                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                desktop.Exit += OnExit;

                // Trigger data load on UI thread to ensure we see any errors
                _ = Dispatcher.UIThread.InvokeAsync(async () => 
                {
                    try 
                    {
                        await mainViewModel.OnMainWindowLoadedAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"Error during initial data load: {ex.Message}", "App.Startup");
                        var dialogService = ServiceProvider?.GetService<IDialogService>();
                        dialogService?.ShowError("Startup Error", $"An error occurred during startup: {ex.Message}");
                    }
                });

                // Background updates
                var dataUpdater = ServiceProvider.GetRequiredService<IDataUpdateService>();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await dataUpdater.CheckForAndApplyUpdatesAsync(new Progress<DataUpdateProgress>(), CancellationToken.None);
                    }
                    catch (Exception updateEx)
                    {
                        _logger.LogWarning($"Failed to update data files on startup: {updateEx.Message}", "App.OnFrameworkInitializationCompleted");
                    }
                });
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // --- Base Infrastructure & Configuration ---
            services.AddSingleton<ILoggerService, LoggerService>();
            services.AddSingleton<IConfigService, ConfigService>(provider => new ConfigService());
            services.AddSingleton<IFileDialogService, AvaloniaFileDialogService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IApplicationNavigationService, ApplicationNavigationService>();
            services.AddSingleton<ISystemInfoService>(provider => SystemInfoServiceFactory.Create());
            services.AddSingleton<IGitService, GitService>();

            services.AddSingleton<string>(provider => AppDomain.CurrentDomain.BaseDirectory);

            services.AddHttpClient(); 

            services.AddSingleton<IDataUpdateService>(provider =>
                   new DataUpdateService(
                       provider.GetRequiredService<ILoggerService>(),
                       provider.GetRequiredService<string>(), 
                       provider.GetRequiredService<IHttpClientFactory>().CreateClient()
                   )
               );

            services.AddSingleton<IPathService, PathService>(provider =>
                new PathService(provider.GetRequiredService<IConfigService>()));

            services.AddSingleton(provider =>
            {
                var configService = provider.GetRequiredService<IConfigService>();
                var pathSettings = new PathSettings
                {
                    GamePath = configService.GetConfigValue("game_folder"),
                    ModsPath = configService.GetConfigValue("mods_folder"),
                    ConfigPath = configService.GetConfigValue("config_folder"),
                    GameVersion = "1.5"
                };
                return pathSettings;
            });

            // --- Mod Rules ---
            services.AddSingleton<IModRulesRepository>(provider =>
                new JsonModRulesRepository(provider.GetRequiredService<IDataUpdateService>()));

            services.AddSingleton<IModRulesService, ModRulesService>();
            services.AddSingleton<IModCustomService>(provider =>
                new ModCustomService(
                    provider.GetRequiredService<string>(), 
                    provider.GetRequiredService<ILoggerService>()
                 ));
            services.AddSingleton<IMlieVersionService, MlieVersionService>();
            services.AddSingleton<IModReplacementService>(provider =>
                new ModReplacementService(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IDataUpdateService>(),
                    provider.GetRequiredService<ILoggerService>()
                ));

            services.AddSingleton<IModDictionaryService>(provider =>
                new ModDictionaryService(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IDataUpdateService>(),
                    provider.GetRequiredService<ILoggerService>()
                ));

            // --- Core Mod Services ---
            services.AddSingleton<IModService>(provider =>
                new ModService(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IModRulesService>(),
                    provider.GetRequiredService<IModCustomService>(),
                    provider.GetRequiredService<IMlieVersionService>(),
                    provider.GetRequiredService<ILoggerService>()
                ));
            services.AddSingleton<IModListManager>(provider => 
            {
                return new ModListManager(
                    provider.GetRequiredService<IModDictionaryService>()
                );
            });
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

            // --- Workshop Downloader Feature Services ---
            services.AddSingleton<IWebNavigationService, WebNavigationService>();
            services.AddSingleton<IDownloadQueueService, DownloadQueueService>();
            services.AddSingleton<IUpdaterService, UpdaterService>(); 
            services.AddSingleton<IAppUpdaterService, AppUpdaterService>();
            services.AddSingleton<ISteamApiClient, SteamApiClient>();
            services.AddSingleton<IWorkshopUpdateCheckerService, WorkshopUpdateCheckerService>();
            services.AddSingleton<ISteamWorkshopQueueProcessor, SteamWorkshopQueueProcessor>(); 

            services.AddSingleton<IModListIOService>(provider =>
            {
                return new ModListIOService(
                   provider.GetRequiredService<IPathService>(),
                   provider.GetRequiredService<IModListManager>(),
                   provider.GetRequiredService<IDialogService>(),
                   provider.GetRequiredService<IModListFileParser>(),          
                   provider.GetRequiredService<IModDictionaryService>(),       
                   provider.GetRequiredService<ISteamApiClient>(),           
                   provider.GetRequiredService<IDownloadQueueService>(),     
                   provider.GetRequiredService<IApplicationNavigationService>(),
                   provider.GetRequiredService<ILoggerService>(),             
                   provider.GetRequiredService<ISteamWorkshopQueueProcessor>()
               );
            });

            services.AddSingleton<IModIncompatibilityService, ModIncompatibilityService>();
            services.AddSingleton<IModDuplicateService, ModDuplicateService>();
            services.AddSingleton<IModListFileParser, ModListFileParser>();
            services.AddSingleton<IModDeletionService, ModDeletionService>();

            // --- SteamCMD Infrastructure ---
            services.AddSingleton<SteamCmdPlatformInfo>();
            services.AddSingleton<ISteamCmdPathService>(provider =>
            {
                var configService = provider.GetRequiredService<IConfigService>();
                var platformInfo = provider.GetRequiredService<SteamCmdPlatformInfo>();
                return new SteamCmdPathService(configService, platformInfo.SteamCmdExeName);
            });
            services.AddSingleton<ISteamCmdFileSystem, SteamCmdFileSystem>();
            services.AddSingleton<ISteamCmdInstaller, SteamCmdInstaller>();
            services.AddSingleton<ISteamCmdScriptGenerator, SteamCmdScriptGenerator>();
            services.AddSingleton<ISteamCmdProcessRunner, SteamCmdProcessRunner>();
            services.AddSingleton<ISteamCmdLogParser, SteamCmdLogParser>();
            services.AddSingleton<IDownloadedItemProcessor, DownloadedItemProcessor>();

            services.AddSingleton<ISteamCmdDownloader>(provider =>
                new SteamCmdDownloader(
                    provider.GetRequiredService<ISteamCmdPathService>(),
                    provider.GetRequiredService<ISteamCmdInstaller>(),
                    provider.GetRequiredService<IDialogService>(),
                    provider.GetRequiredService<ILoggerService>(),
                    provider.GetRequiredService<IPathService>(), 
                    provider.GetRequiredService<ISteamCmdScriptGenerator>(),
                    provider.GetRequiredService<ISteamCmdProcessRunner>(),
                    provider.GetRequiredService<ISteamCmdLogParser>(),
                    provider.GetRequiredService<IDownloadedItemProcessor>()
                ));

            services.AddSingleton<ISteamCmdService, SteamCmdService>();

            // --- ViewModels ---
            services.AddSingleton<ModInfoEnricher>(); 

            services.AddTransient<ModsViewModel>(provider =>
               new ModsViewModel(
                   provider.GetRequiredService<IModDataService>(),
                   provider.GetRequiredService<IModFilterService>(),
                   provider.GetRequiredService<IModCommandService>(),
                   provider.GetRequiredService<IModListIOService>(),
                   provider.GetRequiredService<IModListManager>(),
                   provider.GetRequiredService<IModIncompatibilityService>(),
                   provider.GetRequiredService<IModDuplicateService>(),
                   provider.GetRequiredService<IModDeletionService>(),
                   provider.GetRequiredService<IDialogService>(),
                   provider.GetRequiredService<IModService>(),
                   provider.GetRequiredService<IPathService>(),
                   provider.GetRequiredService<IModRulesService>(),
                   provider.GetRequiredService<IModReplacementService>(),
                   provider.GetRequiredService<IDownloadQueueService>(),
                   provider.GetRequiredService<ISteamApiClient>(),
                   provider.GetRequiredService<IApplicationNavigationService>(),
                   provider.GetRequiredService<ISteamWorkshopQueueProcessor>(),
                   provider.GetRequiredService<IGitService>()
               ));

            services.AddTransient<DownloaderViewModel>(provider =>
                new DownloaderViewModel(
                    provider.GetRequiredService<IWebNavigationService>(),
                    provider.GetRequiredService<IDownloadQueueService>(),
                    provider.GetRequiredService<IModService>(),
                    provider.GetRequiredService<IDialogService>(),
                    provider.GetRequiredService<IWorkshopUpdateCheckerService>(),
                    provider.GetRequiredService<ISteamCmdService>(),
                    provider.GetRequiredService<IModListManager>(),
                    provider.GetRequiredService<ModInfoEnricher>(),             
                    provider.GetRequiredService<ISteamWorkshopQueueProcessor>(),
                    provider.GetRequiredService<ILoggerService>(),              
                    provider.GetRequiredService<ISteamApiClient>()
                ));

            services.AddTransient<GitModsViewModel>(provider =>
                new GitModsViewModel(
                    provider.GetRequiredService<IModService>(),
                    provider.GetRequiredService<IModListManager>(),
                    provider.GetRequiredService<IDialogService>(),
                    provider.GetRequiredService<IGitService>()
                ));

            services.AddTransient<VramAnalysisViewModel>(provider =>
                         new VramAnalysisViewModel(
                             provider.GetRequiredService<IModListManager>(),
                             provider.GetRequiredService<IDialogService>(),
                             provider.GetRequiredService<ILoggerService>(),
                             provider.GetRequiredService<IPathService>(),
                             provider.GetRequiredService<ISystemInfoService>()
                         ));

            services.AddSingleton<MainViewModel>(provider =>
                new MainViewModel(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IConfigService>(),
                    provider.GetRequiredService<IDialogService>(),
                    provider.GetRequiredService<IApplicationNavigationService>(),
                    provider.GetRequiredService<IUpdaterService>(),       
                    provider.GetRequiredService<ModsViewModel>(),       
                    provider.GetRequiredService<DownloaderViewModel>(), 
                    provider.GetRequiredService<GitModsViewModel>(),     
                    provider.GetRequiredService<VramAnalysisViewModel>()
                ));

            services.AddSingleton<MainWindow>();
        }

        private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
        {
            _logger?.LogInfo("Application exiting.", "App.OnExit");
            (ServiceProvider as IDisposable)?.Dispose();

            if (_ownsMutex)
            {
                _instanceMutex?.ReleaseMutex();
            }
            _instanceMutex?.Dispose();
            _instanceMutex = null;
        }
    }
}
