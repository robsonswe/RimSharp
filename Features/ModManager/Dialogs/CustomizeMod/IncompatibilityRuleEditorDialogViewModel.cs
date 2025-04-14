using System.Windows;
using System.Windows.Input;
using RimSharp.MyApp.Dialogs;

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public class IncompatibilityRuleEditorDialogViewModel : DialogViewModelBase<bool>
    {
        private string _packageId;
        public string PackageId
        {
            get => _packageId;
            set => SetProperty(ref _packageId, value);
        }

        private string _displayName;
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        private string _comment;
        public string Comment
        {
            get => _comment;
            set => SetProperty(ref _comment, value);
        }

        private bool _hardIncompatibility;
        public bool HardIncompatibility
        {
            get => _hardIncompatibility;
            set => SetProperty(ref _hardIncompatibility, value);
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public IncompatibilityRuleEditorDialogViewModel(string title) : base(title)
        {
            SaveCommand = CreateCommand(Save);
            CancelCommand = CreateCommand(Cancel);
        }

        private void Save()
        {
            // Basic validation: ensure PackageId is not empty
            if (string.IsNullOrWhiteSpace(PackageId))
            {
                MessageBox.Show("Package ID cannot be empty.", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Trim whitespace from PackageId
            PackageId = PackageId.Trim();
            
            CloseDialog(true);
        }

        private void Cancel()
        {
            CloseDialog(false);
        }
    }
}