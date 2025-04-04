using RimSharp.ViewModels.Dialogs; // Namespace for MessageDialogResult
using System.Threading.Tasks; // If you want async wrappers later

namespace RimSharp.Services
{
    public interface IDialogService
    {
        // Simple messages (blocking, like MessageBox.Show)
        void ShowInformation(string title, string message);
        void ShowWarning(string title, string message);
        void ShowError(string title, string message);

        // Confirmation (blocking, returns result)
        MessageDialogResult ShowConfirmation(string title, string message, bool showCancel = false);

        // More generic method (optional)
        // TResult ShowDialog<TResult>(DialogViewModelBase<TResult> viewModel);
    }
}
