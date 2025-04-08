using System.Threading.Tasks;
using RimSharp.MyApp.Dialogs; // If you want async wrappers later
using RimSharp.Features.WorkshopDownloader.ViewModels;

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

        ProgressDialogViewModel ShowProgressDialog(string title, string message, bool canCancel = false, bool isIndeterminate = true);


        // More generic method (optional)
        // TResult ShowDialog<TResult>(DialogViewModelBase<TResult> viewModel);
    }
}
