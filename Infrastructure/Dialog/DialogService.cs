#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Infrastructure.Logging;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Services.ModManagement;
using RimSharp.Services.Workshop;
using RimSharp.Features.WorkshopDownloader.Dialogs.UpdateCheck;
using RimSharp.Features.ModManager.Dialogs.CustomizeMod;
using RimSharp.Features.ModManager.Dialogs.ModSelector;
using RimSharp.Features.ModManager.Dialogs.Filter;
using RimSharp.Features.ModManager.Dialogs.Replacements;
using RimSharp.Features.ModManager.Dialogs.Dependencies;
using RimSharp.Features.ModManager.Dialogs.MissingMods;
using RimSharp.Features.WorkshopDownloader.Dialogs.Collection;
using RimSharp.Features.ModManager.Dialogs.Strip;
using RimSharp.Features.ModManager.Dialogs.ActiveIssues;
using RimSharp.Features.LogViewer.ViewModels;
using RimSharp.Features.LogViewer.Views;
using RimSharp.Features.AppLogs.ViewModels;
using RimSharp.Features.AppLogs.Views;
using RimSharp.Features.Saves.ViewModels;
using RimSharp.Features.Saves.Views;
using Avalonia.Platform.Storage;

namespace RimSharp.Infrastructure.Dialog
{
    public class DialogService : IDialogService, INotifyPropertyChanged
    {
        private readonly IAppUpdaterService? _appUpdaterService;
        private readonly IFileDialogService? _fileDialogService;
        private readonly IConfigService? _configService;
        private readonly IPathService? _pathService;
        private readonly IEnumerable<IAIProviderClient>? _aiProviders;
        private readonly ILoggerService? _logger;
        private readonly ISteamClaimValidator? _steamClaimValidator;
        private readonly GameInstallationDetectionService? _gameInstallationDetectionService;
        private readonly ISteamSubscriptionService? _steamSubscriptionService;
        private readonly ISaveHeaderService? _saveHeaderService;
        private readonly IModListManager? _modListManager;
        private readonly Shared.Models.GameInstance? _gameInstance;
        private int _openDialogCount;
        public bool IsAnyDialogOpen => _openDialogCount > 0;

        public event PropertyChangedEventHandler? PropertyChanged;

        public DialogService(
            IAppUpdaterService appUpdaterService,
            IConfigService? configService = null,
            IPathService? pathService = null,
            IEnumerable<IAIProviderClient>? aiProviders = null,
            IFileDialogService? fileDialogService = null,
            ILoggerService? logger = null,
            ISteamClaimValidator? steamClaimValidator = null,
            GameInstallationDetectionService? gameInstallationDetectionService = null,
            ISteamSubscriptionService? steamSubscriptionService = null,
            ISaveHeaderService? saveHeaderService = null,
            IModListManager? modListManager = null,
            Shared.Models.GameInstance? gameInstance = null)
        {
            _appUpdaterService = appUpdaterService;
            _configService = configService;
            _pathService = pathService;
            _aiProviders = aiProviders;
            _fileDialogService = fileDialogService;
            _logger = logger;
            _steamClaimValidator = steamClaimValidator;
            _gameInstallationDetectionService = gameInstallationDetectionService;
            _steamSubscriptionService = steamSubscriptionService;
            _saveHeaderService = saveHeaderService;
            _modListManager = modListManager;
            _gameInstance = gameInstance;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void IncrementDialogCount()
        {
            _openDialogCount++;
            if (_openDialogCount == 1) OnPropertyChanged(nameof(IsAnyDialogOpen));
        }

        private void DecrementDialogCount()
        {
            _openDialogCount--;
            if (_openDialogCount == 0) OnPropertyChanged(nameof(IsAnyDialogOpen));
        }

        /// <summary>
        /// Resolves a start location for storage pickers. Only existing
        /// directories are used; otherwise the picker opens at its default.
        /// </summary>
        private static async Task<Avalonia.Platform.Storage.IStorageFolder?> GetStorageFolderAsync(
            Avalonia.Platform.Storage.IStorageProvider storageProvider, string? initialDirectory)
        {
            if (string.IsNullOrEmpty(initialDirectory) || !Directory.Exists(initialDirectory))
            {
                return null;
            }

            try
            {
                return await storageProvider.TryGetFolderFromPathAsync(new Uri(initialDirectory));
            }
            catch
            {
                return null;
            }
        }

        private Window? GetActiveWindow()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Try to find the topmost active window
                var windows = desktop.Windows;
                for (int i = windows.Count - 1; i >= 0; i--)
                {
                    if (windows[i].IsActive || windows[i].IsVisible)
                        return windows[i];
                }
                return desktop.MainWindow;
            }
            return null;
        }

        private async Task<TResult> ShowDialogInternalAsync<TResult>(DialogViewModelBase<TResult> viewModel, BaseDialog dialog)
        {
            IncrementDialogCount();
            try
            {
                // dialog.ApplyTemplate();

                var owner = GetActiveWindow();
                if (owner != null && owner != dialog) // Don't set self as owner
                {
                    await dialog.ShowDialog(owner);
                }
                else
                {
                    dialog.Show();
                }
                return viewModel.DialogResult;
            }
            finally
            {
                DecrementDialogCount();
            }
        }

        public async Task ShowInformation(string title, string message)
        {
            var viewModel = new MessageDialogViewModel(title, message, MessageDialogType.Information);
            var dialog = new MessageDialogView(viewModel);
            await ShowDialogInternalAsync<MessageDialogResult>(viewModel, dialog);
        }

        public async Task ShowWarning(string title, string message)
        {
            var viewModel = new MessageDialogViewModel(title, message, MessageDialogType.Warning);
            var dialog = new MessageDialogView(viewModel);
            await ShowDialogInternalAsync<MessageDialogResult>(viewModel, dialog);
        }

        public async Task ShowError(string title, string message)
        {
            var viewModel = new MessageDialogViewModel(title, message, MessageDialogType.Error);
            var dialog = new MessageDialogView(viewModel);
            await ShowDialogInternalAsync<MessageDialogResult>(viewModel, dialog);
        }

        public MessageDialogResult ShowConfirmation(string title, string message, bool showCancel = false)
        {
            return ShowConfirmationAsync(title, message, showCancel).GetAwaiter().GetResult();
        }

        public async Task<MessageDialogResult> ShowConfirmationAsync(string title, string message, bool showCancel = false)
        {
            var viewModel = new MessageDialogViewModel(title, message, MessageDialogType.Question)
            {
                ShowOkButton = true,
                ShowCancelButton = showCancel
            };
            var dialog = new MessageDialogView(viewModel);
            return await ShowDialogInternalAsync<MessageDialogResult>(viewModel, dialog);
        }

        public async Task<UpdateCheckDialogResult> ShowUpdateCheckDialogAsync(UpdateCheckDialogViewModel viewModel)
        {
            var dialog = new UpdateCheckDialogView(viewModel);
            return await ShowDialogInternalAsync<UpdateCheckDialogResult>(viewModel, dialog);
        }

        public UpdateCheckDialogResult ShowUpdateCheckDialog(UpdateCheckDialogViewModel viewModel)
        {
            var dialog = new UpdateCheckDialogView(viewModel);
            return ShowDialogInternalAsync<UpdateCheckDialogResult>(viewModel, dialog).GetAwaiter().GetResult();
        }

        public async Task ShowMessageWithCopy(string title, string message, MessageDialogType dialogType = MessageDialogType.Information)
        {
            var viewModel = new MessageDialogViewModel(title, message, dialogType)
            {
                ShowCopyButton = true
            };
            var dialog = new MessageDialogView(viewModel);
            await ShowDialogInternalAsync<MessageDialogResult>(viewModel, dialog);
        }

        public async Task<(MessageDialogResult Result, string Input)> ShowInputDialogAsync(string title, string message, string defaultInput = "")
        {
            var viewModel = new InputDialogViewModel(title, message, defaultInput);
            var dialog = new InputDialogView(viewModel);
            var result = await ShowDialogInternalAsync<MessageDialogResult>(viewModel, dialog);
            return (result, viewModel.Input);
        }

        public (MessageDialogResult Result, string Input) ShowInputDialog(string title, string message, string defaultInput = "")
        {
            var viewModel = new InputDialogViewModel(title, message, defaultInput);
            var dialog = new InputDialogView(viewModel);
            var result = ShowDialogInternalAsync<MessageDialogResult>(viewModel, dialog).GetAwaiter().GetResult();
            return (result, viewModel.Input);
        }

        public async Task ShowAboutDialog()
        {
            if (_appUpdaterService == null) return;
            var viewModel = new AboutDialogViewModel(_appUpdaterService);
            var dialog = new AboutDialogView(viewModel);
            await ShowDialogInternalAsync<bool>(viewModel, dialog);
        }

        public async Task<bool> ShowLogViewerAsync()
        {
            var viewModel = new LogViewerViewModel(_pathService, fileDialogService: _fileDialogService);
            var dialog = new LogViewerView(viewModel);
            return await ShowDialogInternalAsync<bool>(viewModel, dialog);
        }

        public async Task<bool> ShowAppLogsAsync()
        {
            // The logs directory resolves exactly like the logger's: the
            // logs_directory setting when present, the app data Logs folder
            // otherwise.
            var viewModel = new AppLogsViewModel(_configService, fileDialogService: _fileDialogService);
            await viewModel.RefreshAsync();
            var dialog = new AppLogsView(viewModel);
            return await ShowDialogInternalAsync<bool>(viewModel, dialog);
        }

        /// <summary>
        /// Modals the read-only saves dialog: the save files of the game and
        /// what the current mod list means for them. Nothing is ever written.
        /// </summary>
        public async Task<bool> ShowSavesDialogAsync(IModListIOService? modListIOService = null)
        {
            if (_saveHeaderService == null) return false;

            var viewModel = new SavesDialogViewModel(_saveHeaderService, _modListManager, modListIOService, this);
            await viewModel.RefreshAsync();
            var dialog = new SavesDialogView(viewModel);
            return await ShowDialogInternalAsync<bool>(viewModel, dialog);
        }

        public async Task<bool> ShowSettingsDialogAsync(bool showAiProviders = true)
        {
            if (_configService == null) return false;
            // _logger is intentionally optional in DialogService (ctor param may be null);
            // SettingsDialogViewModel only consumes it opportunistically, so the null-forgiving
            // operator documents that a missing logger is an accepted state, not an error.
            var viewModel = new SettingsDialogViewModel(
                _configService, _pathService!, _fileDialogService, _logger!,
                _aiProviders,
                _steamClaimValidator!,
                _gameInstallationDetectionService!,
                dialogService: this,
                showAiProviders: showAiProviders);
            var dialog = new SettingsDialogView(viewModel);
            return await ShowDialogInternalAsync<bool>(viewModel, dialog);
        }

        public ProgressDialogViewModel ShowProgressDialog(string title, string message, bool canCancel = false, bool isIndeterminate = true, CancellationTokenSource? cts = null, bool closeable = false, bool showInTaskbar = true)
        {
            var viewModel = new ProgressDialogViewModel(title, message, canCancel, isIndeterminate, cts);
            var dialog = new ProgressDialogView(viewModel)
            {
                Closeable = closeable
            };

            IncrementDialogCount();
            dialog.Closed += (s, e) => DecrementDialogCount();

            var owner = GetActiveWindow();
            if (owner != null && owner != dialog) dialog.Show(owner);
            else dialog.Show();

            return viewModel;
        }

        public async Task<ModCustomizationResult> ShowCustomizeModDialog(CustomizeModDialogViewModel viewModel)
        {
            var dialog = new CustomizeModDialogView(viewModel);
            return await ShowDialogInternalAsync<ModCustomizationResult>(viewModel, dialog);
        }

        public async Task<bool> ShowIncompatibilityDialogAsync(RimSharp.Features.ModManager.Dialogs.Incompatibilities.ModIncompatibilityDialogViewModel viewModel)
        {
            var dialog = new RimSharp.Features.ModManager.Dialogs.Incompatibilities.ModIncompatibilityDialogView(viewModel);
            return await ShowDialogInternalAsync<bool>(viewModel, dialog);
        }

        public void ShowIncompatibilityDialog(RimSharp.Features.ModManager.Dialogs.Incompatibilities.ModIncompatibilityDialogViewModel viewModel)
        {
            var dialog = new RimSharp.Features.ModManager.Dialogs.Incompatibilities.ModIncompatibilityDialogView(viewModel);
            _ = ShowDialogInternalAsync<bool>(viewModel, dialog);
        }

        public async Task<bool> ShowDuplicateModsDialogAsync(RimSharp.Features.ModManager.Dialogs.DuplicateMods.DuplicateModDialogViewModel viewModel)
        {
            var dialog = new RimSharp.Features.ModManager.Dialogs.DuplicateMods.DuplicateModDialogView(viewModel);
            return await ShowDialogInternalAsync<bool>(viewModel, dialog);
        }

        public void ShowDuplicateModsDialog(RimSharp.Features.ModManager.Dialogs.DuplicateMods.DuplicateModDialogViewModel viewModel)
        {
            var dialog = new RimSharp.Features.ModManager.Dialogs.DuplicateMods.DuplicateModDialogView(viewModel);
            _ = ShowDialogInternalAsync<bool>(viewModel, dialog);
        }

        public async Task<bool> ShowAIAcausalIncompatibilityReviewAsync(RimSharp.Features.ModManager.Dialogs.AIIncompatibilityReview.AIIncompatibilityReviewDialogViewModel viewModel)
        {
            var dialog = new RimSharp.Features.ModManager.Dialogs.AIIncompatibilityReview.AIIncompatibilityReviewDialogView(viewModel);
            return await ShowDialogInternalAsync(viewModel, dialog);
        }

        public async Task<(bool Result, string? FilePath)> ShowOpenFileDialogAsync(string title, string filter, string initialDirectory = "")
        {
            if (_fileDialogService != null)
            {
                return await _fileDialogService.ShowOpenFileDialogAsync(title, filter, string.IsNullOrEmpty(initialDirectory) ? null : initialDirectory);
            }

            var window = GetActiveWindow();
            if (window == null) return (false, null);

            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });

            if (files.Count > 0)
            {
                return (true, files[0].Path.LocalPath);
            }
            return (false, null);
        }

        public (bool Result, string? FilePath) ShowOpenFileDialog(string title, string filter, string initialDirectory = "")
        {
            if (_fileDialogService != null)
            {
                return _fileDialogService.ShowOpenFileDialogAsync(title, filter, string.IsNullOrEmpty(initialDirectory) ? null : initialDirectory).GetAwaiter().GetResult();
            }

            var window = GetActiveWindow();
            if (window == null) return (false, null);

            var files = window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            }).GetAwaiter().GetResult();

            if (files.Count > 0)
            {
                return (true, files[0].Path.LocalPath);
            }
            return (false, null);
        }

        public (bool Result, string? FilePath) ShowSaveFileDialog(string title, string filter, string initialDirectory = "", string defaultExt = "", string defaultFileName = "")
        {
            if (_fileDialogService != null)
            {
                return _fileDialogService.ShowSaveFileDialogAsync(title, filter,
                    string.IsNullOrEmpty(initialDirectory) ? null : initialDirectory,
                    string.IsNullOrEmpty(defaultExt) ? null : defaultExt,
                    string.IsNullOrEmpty(defaultFileName) ? null : defaultFileName).GetAwaiter().GetResult();
            }

            var window = GetActiveWindow();
            if (window == null) return (false, null);

            var file = window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                DefaultExtension = defaultExt,
                SuggestedFileName = defaultFileName
            }).GetAwaiter().GetResult();

            if (file != null)
            {
                return (true, file.Path.LocalPath);
            }
            return (false, null);
        }

        public async Task<(bool Result, string? FilePath)> ShowSaveFileDialogAsync(string title, string filter, string initialDirectory = "", string defaultExt = "", string defaultFileName = "")
        {
            if (_fileDialogService != null)
            {
                return await _fileDialogService.ShowSaveFileDialogAsync(title, filter,
                    string.IsNullOrEmpty(initialDirectory) ? null : initialDirectory,
                    string.IsNullOrEmpty(defaultExt) ? null : defaultExt,
                    string.IsNullOrEmpty(defaultFileName) ? null : defaultFileName);
            }

            var window = GetActiveWindow();
            if (window == null) return (false, null);

            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                DefaultExtension = defaultExt,
                SuggestedFileName = defaultFileName
            });

            if (file != null)
            {
                return (true, file.Path.LocalPath);
            }
            return (false, null);
        }

        public async Task<(bool Result, string? Path)> ShowOpenFolderDialogAsync(string title, string initialDirectory = "")
        {
            if (_fileDialogService != null)
            {
                return await _fileDialogService.ShowOpenFolderDialogAsync(title, string.IsNullOrEmpty(initialDirectory) ? null : initialDirectory);
            }

            var window = GetActiveWindow();
            if (window == null) return (false, null);

            var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = await GetStorageFolderAsync(window.StorageProvider, initialDirectory)
            });

            if (folders.Count > 0)
            {
                return (true, folders[0].Path.LocalPath);
            }
            return (false, null);
        }

        public async Task<ModFilterDialogResult> ShowModFilterDialogAsync(ModFilterDialogViewModel viewModel)
        {
            var dialog = new ModFilterDialogView(viewModel);
            return await ShowDialogInternalAsync<ModFilterDialogResult>(viewModel, dialog);
        }

        public ModFilterDialogResult ShowModFilterDialog(ModFilterDialogViewModel viewModel)
        {
            var dialog = new ModFilterDialogView(viewModel);
            return ShowDialogInternalAsync<ModFilterDialogResult>(viewModel, dialog).GetAwaiter().GetResult();
        }

        public async Task<ModReplacementDialogResult> ShowModReplacementDialogAsync(ModReplacementDialogViewModel viewModel)
        {
            var dialog = new ModReplacementDialogView(viewModel);
            return await ShowDialogInternalAsync<ModReplacementDialogResult>(viewModel, dialog);
        }

        public ModReplacementDialogResult ShowModReplacementDialog(ModReplacementDialogViewModel viewModel)
        {
            var dialog = new ModReplacementDialogView(viewModel);
            return ShowDialogInternalAsync<ModReplacementDialogResult>(viewModel, dialog).GetAwaiter().GetResult();
        }

        public async Task<DependencyResolutionDialogResult> ShowDependencyResolutionDialogAsync(DependencyResolutionDialogViewModel viewModel)
        {
            var dialog = new DependencyResolutionDialogView(viewModel);
            return await ShowDialogInternalAsync<DependencyResolutionDialogResult>(viewModel, dialog);
        }

        public DependencyResolutionDialogResult ShowDependencyResolutionDialog(DependencyResolutionDialogViewModel viewModel)
        {
            var dialog = new DependencyResolutionDialogView(viewModel);
            return ShowDialogInternalAsync<DependencyResolutionDialogResult>(viewModel, dialog).GetAwaiter().GetResult();
        }

        public async Task<MissingModSelectionDialogOutput> ShowMissingModSelectionDialogAsync(MissingModSelectionDialogViewModel viewModel)
        {
            var dialog = new MissingModSelectionDialogView(viewModel);
            return await ShowDialogInternalAsync<MissingModSelectionDialogOutput>(viewModel, dialog);
        }

        public MissingModSelectionDialogOutput ShowMissingModSelectionDialog(MissingModSelectionDialogViewModel viewModel)
        {
            var dialog = new MissingModSelectionDialogView(viewModel);
            return ShowDialogInternalAsync<MissingModSelectionDialogOutput>(viewModel, dialog).GetAwaiter().GetResult();
        }

        public async Task<List<string>?> ShowCollectionDialogAsync(CollectionDialogViewModel viewModel)
        {
            var dialog = new CollectionDialogView(viewModel);
            return await ShowDialogInternalAsync<List<string>>(viewModel, dialog);
        }

        public List<string>? ShowCollectionDialog(CollectionDialogViewModel viewModel)
        {
            var dialog = new CollectionDialogView(viewModel);
            return ShowDialogInternalAsync<List<string>>(viewModel, dialog).GetAwaiter().GetResult();
        }

        public async Task<bool> ShowDependencyRuleEditorAsync(DependencyRuleEditorDialogViewModel viewModel)
        {
            var dialog = new DependencyRuleEditorDialogView(viewModel);
            return await ShowDialogInternalAsync<bool>(viewModel, dialog);
        }

        public bool ShowDependencyRuleEditor(DependencyRuleEditorDialogViewModel viewModel)
        {
            var dialog = new DependencyRuleEditorDialogView(viewModel);
            return ShowDialogInternalAsync<bool>(viewModel, dialog).GetAwaiter().GetResult();
        }

        public async Task<bool> ShowIncompatibilityRuleEditorAsync(IncompatibilityRuleEditorDialogViewModel viewModel)
        {
            var dialog = new IncompatibilityRuleEditorDialogView(viewModel);
            return await ShowDialogInternalAsync<bool>(viewModel, dialog);
        }

        public bool ShowIncompatibilityRuleEditor(IncompatibilityRuleEditorDialogViewModel viewModel)
        {
            var dialog = new IncompatibilityRuleEditorDialogView(viewModel);
            return ShowDialogInternalAsync<bool>(viewModel, dialog).GetAwaiter().GetResult();
        }

        public async Task<(bool, IEnumerable<string>?)> ShowStripModsDialogAsync(StripDialogViewModel viewModel)
        {
            var dialog = new StripModsDialogView(viewModel);
            return await ShowDialogInternalAsync<(bool, IEnumerable<string>?)>(viewModel, dialog);
        }

        public (bool, IEnumerable<string>?) ShowStripModsDialog(StripDialogViewModel viewModel)
        {
            var dialog = new StripModsDialogView(viewModel);
            return ShowDialogInternalAsync<(bool, IEnumerable<string>?)>(viewModel, dialog).GetAwaiter().GetResult();
        }

        public async Task<bool> ShowActiveIssuesDialogAsync(ActiveIssuesDialogViewModel viewModel)
        {
            var dialog = new ActiveIssuesDialogView(viewModel);
            return await ShowDialogInternalAsync<bool>(viewModel, dialog);
        }

        public async Task<RimSharp.Shared.Models.ModItem?> ShowModSelectorDialogAsync(ModSelectorDialogViewModel viewModel)
        {
            var dialog = new ModSelectorDialogView(viewModel);
            return await ShowDialogInternalAsync<RimSharp.Shared.Models.ModItem?>(viewModel, dialog);
        }

        public RimSharp.Shared.Models.ModItem? ShowModSelectorDialog(ModSelectorDialogViewModel viewModel)
        {
            var dialog = new ModSelectorDialogView(viewModel);
            return ShowDialogInternalAsync<RimSharp.Shared.Models.ModItem?>(viewModel, dialog).GetAwaiter().GetResult();
        }
    }
}


