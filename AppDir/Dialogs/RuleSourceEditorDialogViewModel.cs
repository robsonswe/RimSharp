using System.Windows.Input;
using RimSharp.AppDir.AppFiles;

namespace RimSharp.AppDir.Dialogs
{
    /// <summary>
    /// Two-field editor for a rule database source: display name + (remote URL
    /// or local path). Used by the Settings "Rule Databases" tab when adding
    /// or editing a remote source, so both pieces of information are captured
    /// in a single dialog instead of two separate prompts.
    /// </summary>
    public class RuleSourceEditorDialogViewModel : DialogViewModelBase<MessageDialogResult>
    {
        private string _name = string.Empty;
        private string _url = string.Empty;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Url
        {
            get => _url;
            set => SetProperty(ref _url, value);
        }

        public string Message { get; }

        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }

        public RuleSourceEditorDialogViewModel(string title, string message, string defaultName, string defaultUrl)
            : base(title)
        {
            Message = message;
            Name = defaultName ?? string.Empty;
            Url = defaultUrl ?? string.Empty;

            OkCommand = CreateCommand(
                () => CloseDialog(MessageDialogResult.OK),
                () => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Url),
                nameof(Name), nameof(Url));

            CancelCommand = CreateCommand(
                () => CloseDialog(MessageDialogResult.Cancel));
        }
    }
}
