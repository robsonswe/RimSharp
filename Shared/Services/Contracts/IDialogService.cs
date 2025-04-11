using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Dialogs.UpdateCheck;
using RimSharp.MyApp.Dialogs; // If you want async wrappers later

namespace RimSharp.Shared.Services.Contracts
{
    public interface IDialogService
    {
        // Simple messages (blocking, like MessageBox.Show)
        void ShowInformation(string title, string message);
        void ShowWarning(string title, string message);
        void ShowError(string title, string message);

        // Confirmation (blocking, returns result)
        MessageDialogResult ShowConfirmation(string title, string message, bool showCancel = false);

        void ShowMessageWithCopy(string title, string message, MessageDialogType dialogType = MessageDialogType.Information);

        UpdateCheckDialogResult ShowUpdateCheckDialog(UpdateCheckDialogViewModel viewModel);

        // Updated to accept an optional CancellationTokenSource
        ProgressDialogViewModel ShowProgressDialog(string title, string message, bool canCancel = false, bool isIndeterminate = true, CancellationTokenSource cts = null);

        // More generic method (optional)
        // TResult ShowDialog<TResult>(DialogViewModelBase<TResult> viewModel);
    }
}
