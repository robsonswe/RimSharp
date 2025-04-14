using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.ModManager.Dialogs.CustomizeMod;
using RimSharp.Features.ModManager.Dialogs.Filter;
using RimSharp.Features.WorkshopDownloader.Dialogs.UpdateCheck;
using RimSharp.MyApp.Dialogs;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IDialogService
    {
        void ShowInformation(string title, string message);
        void ShowWarning(string title, string message);
        void ShowError(string title, string message);
        MessageDialogResult ShowConfirmation(string title, string message, bool showCancel = false);
        void ShowMessageWithCopy(string title, string message, MessageDialogType dialogType = MessageDialogType.Information);
        UpdateCheckDialogResult ShowUpdateCheckDialog(UpdateCheckDialogViewModel viewModel);
        ProgressDialogViewModel ShowProgressDialog(string title, string message, bool canCancel = false, bool isIndeterminate = true, CancellationTokenSource cts = null, bool closeable = true);
        (MessageDialogResult Result, string Input) ShowInputDialog(string title, string message, string defaultInput = "");
        ModCustomizationResult ShowCustomizeModDialog(CustomizeModDialogViewModel viewModel);
        ModFilterDialogResult ShowModFilterDialog(ModFilterDialogViewModel viewModel);

    }
}