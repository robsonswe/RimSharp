// App.axaml.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using RimSharp.AppDir.MainPage;
using RimSharp.AppDir.AppFiles;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Dialog;
using RimSharp.Infrastructure.Logging;
using RimSharp.Infrastructure.Data;
using RimSharp.Core.Extensions;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;
using RimSharp.Features.ModManager.Services.Commands;
using RimSharp.Features.ModManager.Services.Data;
using RimSharp.Features.ModManager.Services.Filtering;
using RimSharp.Features.ModManager.Services.Management;
using RimSharp.Features.ModManager.ViewModels;
using RimSharp.Infrastructure.Mods.IO;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Infrastructure.Mods.Validation.Duplicates;
using RimSharp.Infrastructure.Mods.Validation.AI;
using RimSharp.Infrastructure.AI;
using RimSharp.Features.GitModManager.ViewModels;
using RimSharp.Features.VramAnalysis.ViewModels;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Features.WorkshopDownloader.ViewModels;
using RimSharp.Features.WorkshopDownloader.Components.DownloadQueue;
using RimSharp.Infrastructure.Workshop;
using RimSharp.Infrastructure.Workshop.Core;
using RimSharp.Infrastructure.Workshop.Download;
using RimSharp.Infrastructure.Workshop.Download.Execution;
using RimSharp.Infrastructure.Workshop.Download.Parsing;
using RimSharp.Infrastructure.Workshop.Download.Processing;
using RimSharp.Infrastructure.Workshop.Download.Models;
using RimSharp.Services.Workshop;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.AppDir.Dialogs;
using System.Diagnostics;
using System.Linq;
#if WINDOWS
using PInvoke;
#endif
using RimSharp.Core.Services;
using RimSharp.Features.WorkshopDownloader.Components.Browser;
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
                bool isNewInstance;
                _instanceMutex = new Mutex(true, AppUniqueId, out isNewInstance);
                _ownsMutex = isNewInstance;

                if (!isNewInstance)
                {
                    Debug.WriteLine("App is already running. Activating existing instance.");
#if WINDOWS
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
#endif
                    desktop.Shutdown();
                    System.Environment.Exit(0);
                }
                // One-time migration of legacy exe-side data into XDG
                // directories (no-op on Windows). Must run before services are
                // built so ConfigService reads the migrated config.cfg.
                AppDataPaths.MigrateLegacyLocations();

                var services = new ServiceCollection();
                ConfigureServices(services);
                ServiceProvider = services.BuildServiceProvider();
                _logger = ServiceProvider.GetRequiredService<ILoggerService>();

                ThreadHelper.Initialize();
                ServiceProvider.GetRequiredService<IModCustomService>().Initialize();
                _logger.LogInfo("Application starting up - Initializing UI Components", "App.OnFrameworkInitializationCompleted");

                var mainViewModel = ServiceProvider.GetRequiredService<MainViewModel>();
                var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                mainWindow.DataContext = mainViewModel;

                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                desktop.Exit += OnExit;

                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    // Startup gate: the app MUST NOT load data with unresolved
                    // Game/Config folders. This blocks on the tabbed Settings
                    // dialog until both paths are valid; if the user gives up,
                    // the application shuts down instead of showing empty lists.
                    bool pathsOk;
                    try
                    {
                        pathsOk = await mainViewModel.EnsurePathsConfiguredAsync();
                    }
                    catch (Exception gateEx)
                    {
                        _logger?.LogError($"Path configuration gate failed: {gateEx.Message}", "App.Startup");
                        pathsOk = true; // don't brick the app over gate internals
                    }

                    if (!pathsOk)
                    {
                        _logger?.LogInfo("Startup aborted by the user at the path configuration gate.", "App.Startup");
                        desktop.Shutdown();
                        System.Environment.Exit(0);
                        return;
                    }

                    try
                    {
                        await mainViewModel.OnMainWindowLoadedAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"Error during initial data load: {ex.Message}", "App.Startup");
                        var dialogService = ServiceProvider?.GetService<IDialogService>();
                        dialogService?.ShowError("Startup Error", $"An error occurred during startup: {ex.Message}");
                        return;
                    }

                    // Background data updates start only after the app is up
                    // with valid paths and the initial load done.
                    var dataUpdater = ServiceProvider.GetRequiredService<IDataUpdateService>();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await dataUpdater.CheckForAndApplyUpdatesAsync(new Progress<DataUpdateProgress>(), CancellationToken.None);
                        }
                        catch (Exception updateEx)
                        {
                            _logger?.LogWarning($"Failed to update data files on startup: {updateEx.Message}", "App.OnFrameworkInitializationCompleted");
                        }
                    });
                });
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<ILoggerService, LoggerService>();
            services.AddSingleton<IConfigService, ConfigService>(provider => new ConfigService(AppDataPaths.GetConfigFilePath()));
            services.AddSingleton<GameInstance>(provider =>
            {
                var gameInstance = new GameInstance();
                var pathService = provider.GetRequiredService<IPathService>();
                var configService = provider.GetRequiredService<IConfigService>();
                var steamDetectionService = new SteamDetectionService(pathService);
                
                // Initialize game instance with paths from path service
                gameInstance.GameFolder = pathService.GetGamePath();
                gameInstance.ConfigFolder = pathService.GetConfigPath();
                gameInstance.LocalFolder = pathService.GetModsPath();
                
                // Detect Steam installation
                steamDetectionService.DetectAndConfigureSteam(gameInstance);

                // Override with saved settings if configured
                var savedSteamInstallation = configService.GetConfigValue("steam_installation");
                if (!string.IsNullOrEmpty(savedSteamInstallation) && bool.TryParse(savedSteamInstallation, out var isSteam))
                {
                    gameInstance.IsSteamInstallation = isSteam;
                }

                // Workshop override resolves through the detection service (single writer
                // of GameInstance.WorkshopFolder): empty = keep the detected folder; set =
                // use if ACF-valid, else warn + fall back to detection; never clears config.
                var savedWorkshopFolder = configService.GetConfigValue("steam_workshop_folder");
                steamDetectionService.ApplyWorkshopFolderOverride(gameInstance, savedWorkshopFolder);

                var savedIntegration = configService.GetConfigValue("steam_integration");
                if (!string.IsNullOrEmpty(savedIntegration) && bool.TryParse(savedIntegration, out var integration))
                {
                    gameInstance.SteamClientIntegration = integration;
                }

                var savedProtocol = configService.GetConfigValue("steam_protocol");
                if (!string.IsNullOrEmpty(savedProtocol) && bool.TryParse(savedProtocol, out var protocol))
                {
                    gameInstance.LaunchViaSteamProtocol = protocol;
                }

                var savedSteamCmdPath = configService.GetConfigValue("steamcmd_path");
                if (!string.IsNullOrEmpty(savedSteamCmdPath))
                {
                    gameInstance.SteamCmdPath = savedSteamCmdPath;
                }

                var savedSteamCmdCache = configService.GetConfigValue("steamcmd_cache");
                if (!string.IsNullOrEmpty(savedSteamCmdCache) && bool.TryParse(savedSteamCmdCache, out var clearCache))
                {
                    gameInstance.SteamCmdAutoClearCache = clearCache;
                }

                gameInstance.ValidateAndFix();
                return gameInstance;
            });
            services.AddSingleton<IFileDialogService, AvaloniaFileDialogService>();
            services.AddSingleton<IDialogService>(provider =>
                new DialogService(
                    provider.GetRequiredService<IAppUpdaterService>(),
                    provider.GetRequiredService<IConfigService>(),
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IEnumerable<IAIProviderClient>>(),
                    provider.GetRequiredService<IFileDialogService>(),
                    provider.GetRequiredService<ILoggerService>()
                ));
            services.AddSingleton<IApplicationNavigationService, ApplicationNavigationService>();
            services.AddSingleton<ISystemInfoService>(provider => SystemInfoServiceFactory.Create());
            services.AddSingleton<IGitService, GitService>();

#if WINDOWS
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                services.AddSingleton<IBrowserFactory, WindowsBrowserFactory>();
            }
            else
            {
                services.AddSingleton<IBrowserFactory, GenericBrowserFactory>();
            }
#else
            // Probe inside the provider so the capability check is logged via
            // ILoggerService (the static cache ensures it runs only once).
            services.AddSingleton<IBrowserFactory>(provider =>
            {
                var linuxFactory = new LinuxBrowserFactory(provider.GetRequiredService<ILoggerService>());
                return linuxFactory.IsSupported ? linuxFactory : (IBrowserFactory)new GenericBrowserFactory();
            });
#endif

            // App-managed data root: Windows -> next to the exe (unchanged),
            // Linux -> XDG data dir. Consumed by DataUpdateService,
            // ModCustomService and LoggerService.
            services.AddSingleton<string>(provider => AppDataPaths.DataDirectory);

            services.AddHttpClient();

            // Workshop HTML-description extractor: Steam serves the item page
            // only to browser-like User-Agents.
            services.AddHttpClient("WorkshopHtml", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; RimSharp)");
            });


            services.AddSingleton<IDataUpdateService>(provider =>
                   new DataUpdateService(
                       provider.GetRequiredService<ILoggerService>(),
                       provider.GetRequiredService<string>(), 
                       provider.GetRequiredService<IHttpClientFactory>().CreateClient()
                   )
               );

            services.AddSingleton<IPathService, PathService>(provider =>
                new PathService(provider.GetRequiredService<IConfigService>(), null, provider.GetRequiredService<ILoggerService>()));

            services.AddSingleton(provider =>
            {
                var configService = provider.GetRequiredService<IConfigService>();
                var pathSettings = new PathSettings
                {
                    GamePath = configService.GetConfigValue("game_folder"),
                    ModsPath = configService.GetConfigValue("mods_folder"),
                    ConfigPath = configService.GetConfigValue("config_folder"),
                    GameVersion = "N/A"
                };
                return pathSettings;
            });
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
            services.AddSingleton<IModService>(provider =>
                new ModService(
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IModRulesService>(),
                    provider.GetRequiredService<IModCustomService>(),
                    provider.GetRequiredService<IMlieVersionService>(),
                    provider.GetRequiredService<ILoggerService>(),
                    provider.GetRequiredService<GameInstance>(),
                    provider.GetRequiredService<IWorkshopAcfTimestampService>()
                ));
            services.AddSingleton<IModListManager>(provider => 
            {
                return new ModListManager(
                    provider.GetRequiredService<IModDictionaryService>()
                );
            });
            services.AddSingleton<ModLookupService>();
            services.AddSingleton<IModDataService>(provider =>
                new ModDataService(
                    provider.GetRequiredService<IModService>(),
                    provider.GetRequiredService<IPathService>(),
                    provider.GetRequiredService<IDialogService>()
                ));
            services.AddSingleton<IModFilterService, ModFilterService>();
            services.AddSingleton<IModCommandService, ModCommandService>();
            services.AddSingleton<IWebNavigationService, WebNavigationService>();
            services.AddSingleton<IDownloadQueueService, DownloadQueueService>();
            services.AddSingleton<IUpdaterService, UpdaterService>(); 
            services.AddSingleton<IAppUpdaterService>(provider => new AppUpdaterService(
    provider.GetRequiredService<IHttpClientFactory>(),
    provider.GetRequiredService<ILoggerService>(),
    provider.GetRequiredService<IConfigService>()
));
            services.AddSingleton<ISteamApiClient, SteamApiClient>();
            services.AddSingleton<IWorkshopUpdateCheckerService, WorkshopUpdateCheckerService>();
            services.AddSingleton<ISteamWorkshopQueueProcessor, SteamWorkshopQueueProcessor>();
            
            // Workshop Management Services
            services.AddSingleton<SteamWorkshopApiClient>(provider =>
                new SteamWorkshopApiClient(provider.GetRequiredService<IHttpClientFactory>().CreateClient()));
            services.AddSingleton<IAcfFileParser, AcfFileParser>();
            services.AddSingleton<IWorkshopAcfTimestampService>(
                provider => new WorkshopAcfTimestampService(
                    provider.GetRequiredService<IAcfFileParser>(),
                    provider.GetRequiredService<ILoggerService>()));
            services.AddSingleton<ISteamSubscriptionService, SteamSubscriptionService>();
            services.AddSingleton<IWorkshopUpdateService, WorkshopUpdateService>(); 

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
            services.AddSingleton<IAIProviderClient, OpenRouterProviderClient>();
            services.AddSingleton<AIProviderFactory>();
            services.AddSingleton<IAIncompatibilityAgentService, AIIncompatibilityAgentService>();
            services.AddSingleton<IAIncompatibilityAgentService, AIIncompatibilityAgentService>();
            services.AddSingleton<IModDuplicateService, ModDuplicateService>();
            services.AddSingleton<IModListFileParser, ModListFileParser>();
            services.AddSingleton<IModDeletionService, ModDeletionService>();
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

            // Fetches/caches full Workshop descriptions (RimSharp.json per mod
            // folder). Used by the downloader post-install hook below.
            services.AddSingleton<IWorkshopDescriptionService, WorkshopDescriptionService>();
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
                   provider.GetRequiredService<IGitService>(),
                   provider.GetRequiredService<IAIncompatibilityAgentService>(),
                   provider.GetRequiredService<IModCustomService>(),
                   provider.GetRequiredService<ISteamSubscriptionService>(),
                   provider.GetRequiredService<IWorkshopUpdateService>()
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
                    provider.GetRequiredService<ISteamApiClient>(),
                    provider.GetRequiredService<IWorkshopDescriptionService>()
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
                    provider.GetRequiredService<VramAnalysisViewModel>(),
                    provider.GetRequiredService<ISteamSubscriptionService>(),
                    provider.GetRequiredService<IWorkshopUpdateService>(),
                    provider.GetRequiredService<GameInstance>()
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

