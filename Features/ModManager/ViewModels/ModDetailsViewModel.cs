using RimSharp.Core.Commands;
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts; // For IDialogService
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System;
using System.Threading.Tasks;

namespace RimSharp.Features.ModManager.ViewModels
{
    public class ModDetailsViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogService;
        private ModItem? _currentMod;

        public ModItem? CurrentMod
        {
            get => _currentMod;
            set
            {
                // Use base SetProperty
                if (SetProperty(ref _currentMod, value))
                {
                    // Command observation handles CanExecute updates automatically
                    OnPropertyChanged(nameof(HasValidUrlOrPath));
                }
            }
        }

        // This property depends on CurrentMod
        public bool HasValidUrlOrPath => CurrentMod != null &&
                                         (!string.IsNullOrWhiteSpace(CurrentMod.Url) ||
                                          (!string.IsNullOrWhiteSpace(CurrentMod.Path) && Directory.Exists(CurrentMod.Path)));


        // --- Commands specific to the SINGLE selected mod ---
        public ICommand OpenUrlCommand { get; }


        public ModDetailsViewModel(IDialogService dialogService)
        {
            _dialogService = dialogService;
            // Use base helper CreateAsyncCommand and observe CurrentMod
            OpenUrlCommand = CreateAsyncCommand<string>(ExecuteOpenUrl, CanExecuteOpenUrl, nameof(CurrentMod));
        }

        private bool CanExecuteOpenUrl(string? targetPath)
        {
            return HasValidUrlOrPath;
        }

        private async Task ExecuteOpenUrl(string? targetPath)
        {
            if (CurrentMod == null) return;

            // Priority:
            // 1. targetPath if provided (e.g. from clicking the Path link)
            // 2. CurrentMod.Url (default mod link)
            // 3. CurrentMod.Path (fallback)
            string? target = targetPath;
            
            if (string.IsNullOrWhiteSpace(target))
            {
                target = CurrentMod.Url;
                if (string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(CurrentMod.Path))
                {
                    target = CurrentMod.Path;
                }
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                await _dialogService.ShowInformation("Information", "No URL or local path available for the selected mod.");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not open path/URL '{target}': {ex}");
                await _dialogService.ShowError("Error", $"Could not open path/URL: {ex.Message}");
            }
        }
    }
}
