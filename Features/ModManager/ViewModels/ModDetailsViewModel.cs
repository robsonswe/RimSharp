using RimSharp.Core.Commands;
using RimSharp.MyApp.AppFiles;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts; // For IDialogService
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System;

namespace RimSharp.Features.ModManager.ViewModels
{
    public class ModDetailsViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogService;
        private ModItem _currentMod;

        public ModItem CurrentMod
        {
            get => _currentMod;
            set
            {
                if (SetProperty(ref _currentMod, value))
                {
                    (OpenUrlCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    // Raise property changed for all derived properties if needed
                    OnPropertyChanged(nameof(HasValidUrlOrPath));
                }
            }
        }

        public bool HasValidUrlOrPath => CurrentMod != null &&
                                         (!string.IsNullOrWhiteSpace(CurrentMod.Url) ||
                                          (!string.IsNullOrWhiteSpace(CurrentMod.Path) && Directory.Exists(CurrentMod.Path)));


        // --- Commands specific to the SINGLE selected mod ---
        public ICommand OpenUrlCommand { get; private set; } // Renamed for clarity


        public ModDetailsViewModel(IDialogService dialogService)
        {
            _dialogService = dialogService;
            OpenUrlCommand = new RelayCommand(ExecuteOpenUrl, CanExecuteOpenUrl);
        }

        private bool CanExecuteOpenUrl()
        {
            return HasValidUrlOrPath;
        }

        private void ExecuteOpenUrl()
        {
            if (CurrentMod == null) return;

            string target = CurrentMod.Url; // Prefer URL
            if (string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(CurrentMod.Path))
            {
                target = CurrentMod.Path; // Fallback to Path
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                _dialogService.ShowInformation("Information", "No URL or local path available for the selected mod.");
                return;
            }

            try
            {
                // Use ShellExecute for both URLs and Folders
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not open path/URL '{target}': {ex}");
                _dialogService.ShowError("Error", $"Could not open path/URL: {ex.Message}");
            }
        }
    }
}