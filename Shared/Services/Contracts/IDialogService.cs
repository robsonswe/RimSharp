#nullable enable
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.ModManager.Dialogs.CustomizeMod;
using RimSharp.Features.ModManager.Dialogs.ModSelector;
using RimSharp.Features.ModManager.Dialogs.Dependencies;
using RimSharp.Features.ModManager.Dialogs.Filter;
using RimSharp.Features.ModManager.Dialogs.Replacements;
using RimSharp.Features.WorkshopDownloader.Dialogs.UpdateCheck;
using RimSharp.Features.ModManager.Dialogs.MissingMods;

using RimSharp.AppDir.Dialogs;
using RimSharp.Features.WorkshopDownloader.Dialogs.Collection;
using System.Collections.Generic;
using RimSharp.Features.ModManager.Dialogs.Strip;
using RimSharp.Features.ModManager.Dialogs.ActiveIssues;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IDialogService
    {
        bool IsAnyDialogOpen { get; }
        Task ShowInformation(string title, string message);
        Task ShowWarning(string title, string message);
        Task ShowError(string title, string message);
        MessageDialogResult ShowConfirmation(string title, string message, bool showCancel = false);
        Task<MessageDialogResult> ShowConfirmationAsync(string title, string message, bool showCancel = false);
        Task ShowMessageWithCopy(string title, string message, MessageDialogType dialogType = MessageDialogType.Information);
        Task<UpdateCheckDialogResult> ShowUpdateCheckDialogAsync(UpdateCheckDialogViewModel viewModel);
        UpdateCheckDialogResult ShowUpdateCheckDialog(UpdateCheckDialogViewModel viewModel);

        ProgressDialogViewModel ShowProgressDialog(string title, string message, bool canCancel = false, bool isIndeterminate = true, CancellationTokenSource? cts = null, bool closeable = false, bool showInTaskbar = true);

        (MessageDialogResult Result, string Input) ShowInputDialog(string title, string message, string defaultInput = "");
        Task<(MessageDialogResult Result, string Input)> ShowInputDialogAsync(string title, string message, string defaultInput = "");
        
        Task<ModCustomizationResult> ShowCustomizeModDialog(CustomizeModDialogViewModel viewModel);
        Task<ModFilterDialogResult> ShowModFilterDialogAsync(ModFilterDialogViewModel viewModel);
        ModFilterDialogResult ShowModFilterDialog(ModFilterDialogViewModel viewModel);
        Task<ModReplacementDialogResult> ShowModReplacementDialogAsync(ModReplacementDialogViewModel viewModel);
        ModReplacementDialogResult ShowModReplacementDialog(ModReplacementDialogViewModel viewModel);
        Task<DependencyResolutionDialogResult> ShowDependencyResolutionDialogAsync(DependencyResolutionDialogViewModel viewModel);
        DependencyResolutionDialogResult ShowDependencyResolutionDialog(DependencyResolutionDialogViewModel viewModel);
        Task<MissingModSelectionDialogOutput> ShowMissingModSelectionDialogAsync(MissingModSelectionDialogViewModel viewModel);
        MissingModSelectionDialogOutput ShowMissingModSelectionDialog(MissingModSelectionDialogViewModel viewModel);
        Task<List<string>?> ShowCollectionDialogAsync(CollectionDialogViewModel viewModel);
        List<string>? ShowCollectionDialog(CollectionDialogViewModel viewModel);
        Task<(bool, IEnumerable<string>?)> ShowStripModsDialogAsync(StripDialogViewModel viewModel);
        (bool, IEnumerable<string>?) ShowStripModsDialog(StripDialogViewModel viewModel);
        Task<bool> ShowActiveIssuesDialogAsync(ActiveIssuesDialogViewModel viewModel);
        Task<bool> ShowDependencyRuleEditorAsync(DependencyRuleEditorDialogViewModel viewModel);
        bool ShowDependencyRuleEditor(DependencyRuleEditorDialogViewModel viewModel);
        Task<bool> ShowIncompatibilityRuleEditorAsync(IncompatibilityRuleEditorDialogViewModel viewModel);
        bool ShowIncompatibilityRuleEditor(IncompatibilityRuleEditorDialogViewModel viewModel);
        Task<bool> ShowIncompatibilityDialogAsync(RimSharp.Features.ModManager.Dialogs.Incompatibilities.ModIncompatibilityDialogViewModel viewModel);
        void ShowIncompatibilityDialog(RimSharp.Features.ModManager.Dialogs.Incompatibilities.ModIncompatibilityDialogViewModel viewModel);
        Task<bool> ShowDuplicateModsDialogAsync(RimSharp.Features.ModManager.Dialogs.DuplicateMods.DuplicateModDialogViewModel viewModel);
        void ShowDuplicateModsDialog(RimSharp.Features.ModManager.Dialogs.DuplicateMods.DuplicateModDialogViewModel viewModel);
        
        Task<RimSharp.Shared.Models.ModItem?> ShowModSelectorDialogAsync(ModSelectorDialogViewModel viewModel);
        RimSharp.Shared.Models.ModItem? ShowModSelectorDialog(ModSelectorDialogViewModel viewModel);
        
        // File dialogs - delegated to IFileDialogService for cross-platform support
        Task<(bool Result, string? FilePath)> ShowOpenFileDialogAsync(string title, string filter, string initialDirectory = "");
        (bool Result, string? FilePath) ShowOpenFileDialog(string title, string filter, string initialDirectory = "");
        Task<(bool Result, string? FilePath)> ShowSaveFileDialogAsync(string title, string filter, string initialDirectory = "", string defaultExt = "", string defaultFileName = "");
        (bool Result, string? FilePath) ShowSaveFileDialog(string title, string filter, string initialDirectory = "", string defaultExt = "", string defaultFileName = "");
        Task<(bool Result, string? Path)> ShowOpenFolderDialogAsync(string title, string initialDirectory = "");

        Task ShowAboutDialog();
    }
}