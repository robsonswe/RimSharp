using System;
using System.Windows.Input;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public class DependencyRuleEditorDialogViewModel : DialogViewModelBase<bool>
    {
        private readonly IDialogService _dialogService;
        private string _packageId = string.Empty;
        private string _displayName = string.Empty;
        private string _comment = string.Empty;

        public string PackageId
        {
            get => _packageId;
            set => SetProperty(ref _packageId, value);
        }

        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        public string Comment
        {
            get => _comment;
            set => SetProperty(ref _comment, value);
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public DependencyRuleEditorDialogViewModel(string title, IDialogService dialogService) : base(title)
        {
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            SaveCommand = CreateCommand(Save);
            CancelCommand = CreateCommand(Cancel);
        }

        private void Save()
        {
            if (string.IsNullOrWhiteSpace(PackageId))
            {
                _dialogService.ShowWarning("Validation Error", "Package ID cannot be empty.");
                return;
            }
            
            PackageId = PackageId.Trim();
            CloseDialog(true);
        }

        private void Cancel()
        {
            CloseDialog(false);
        }
    }
}
