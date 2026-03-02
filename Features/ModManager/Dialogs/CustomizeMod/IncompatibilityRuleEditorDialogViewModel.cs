using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Features.ModManager.Dialogs.ModSelector;

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public class IncompatibilityRuleEditorDialogViewModel : DialogViewModelBase<bool>
    {
        private readonly IDialogService _dialogService;
        private readonly IModService _modService;
        private string _packageId = string.Empty;
        private string _displayName = string.Empty;
        private string _comment = string.Empty;
        private bool _hardIncompatibility;

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

        public bool HardIncompatibility
        {
            get => _hardIncompatibility;
            set => SetProperty(ref _hardIncompatibility, value);
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand OpenModSelectorCommand { get; }

        public IncompatibilityRuleEditorDialogViewModel(string title, IDialogService dialogService, IModService modService) : base(title)
        {
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));

            SaveCommand = CreateCommand(Save);
            CancelCommand = CreateCommand(Cancel);
            OpenModSelectorCommand = CreateAsyncCommand(OpenModSelectorAsync);
        }

        private async Task OpenModSelectorAsync()
        {
            var allMods = _modService.GetLoadedMods();
            var viewModel = new ModSelectorDialogViewModel(allMods);

            if (!string.IsNullOrWhiteSpace(PackageId))
            {
                viewModel.SearchText = PackageId;
            }

            var selectedMod = await _dialogService.ShowModSelectorDialogAsync(viewModel);
            if (selectedMod != null)
            {
                PackageId = selectedMod.PackageId;
                DisplayName = selectedMod.Name;
            }
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


