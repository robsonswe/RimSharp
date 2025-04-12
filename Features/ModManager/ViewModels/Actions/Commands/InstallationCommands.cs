using Microsoft.Win32;
using RimSharp.Core.Commands;
using RimSharp.MyApp.AppFiles; // For ViewModelBase/RunOnUIThread if needed
using RimSharp.MyApp.Dialogs; // For MessageDialogResult
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    // Mark the class as partial
    public partial class ModActionsViewModel
    {
        // Partial initialization method
        private void InitializeInstallationCommands()
        {
            InstallFromZipCommand = new AsyncRelayCommand(ExecuteInstallFromZip, CanExecuteSimpleCommands);
            InstallFromGithubCommand = new AsyncRelayCommand(ExecuteInstallFromGithub, CanExecuteSimpleCommands);
        }

        // --- Execution Methods ---
        public async Task ExecuteInstallFromZip(CancellationToken ct = default)
        {
            IsLoadingRequest?.Invoke(this, true);
            _installSuccess = false; // Reset flag
            try
            {
                var fileDialog = new OpenFileDialog
                {
                    Filter = "Zip Files (*.zip)|*.zip",
                    Title = "Select Mod Zip File"
                };

                if (fileDialog.ShowDialog() != true) return; // IsLoading reset in finally

                var zipPath = fileDialog.FileName;
                ct.ThrowIfCancellationRequested();

                // Use helper from ZipInstallationHelper.cs
                if (!IsValidZipFile(zipPath))
                {
                    _dialogService.ShowError("Invalid File", "The selected file is not a valid zip file.");
                    return;
                }

                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    // Use helper from ZipInstallationHelper.cs
                    var modInfo = await ValidateModZip(archive, ct);
                    ct.ThrowIfCancellationRequested();

                    if (modInfo == null)
                    {
                        _dialogService.ShowError("Invalid Mod", "The selected zip file doesn't contain a valid RimWorld mod.");
                        return;
                    }

                    var result = _dialogService.ShowConfirmation(
                        "Confirm Installation",
                        $"Are you sure you want to install the mod '{modInfo.Name}'?",
                        showCancel: true);

                    if (result != MessageDialogResult.OK) return;
                    ct.ThrowIfCancellationRequested();

                    var modsPath = _pathService.GetModsPath();
                    if (string.IsNullOrEmpty(modsPath))
                    {
                        _dialogService.ShowError("Path Error", "Mods directory is not set.");
                        return;
                    }

                    // Use helper from ZipInstallationHelper.cs
                    var rootFolder = GetRootModFolder(archive);
                    string modName = rootFolder == null
                        ? Path.GetFileNameWithoutExtension(zipPath).Replace(" ", "") // Consider better sanitization
                        : rootFolder.FullName.TrimEnd('/', '\\');

                    var targetDir = Path.Combine(modsPath, modName);

                    if (Directory.Exists(targetDir))
                    {
                         _dialogService.ShowError("Install Error", $"A mod folder already exists at:\n{targetDir}");
                         return;
                    }

                    // Consider adding validation for invalid characters in modName here

                    // Use helper from ZipInstallationHelper.cs
                    await Task.Run(() => ExtractMod(archive, targetDir, rootFolder, ct), ct);

                    _dialogService.ShowInformation("Install Complete", $"Mod '{modInfo.Name}' was successfully installed.");
                    _installSuccess = true; // Set flag for finally block
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ExecuteInstallFromZip] Installation cancelled.");
                _dialogService.ShowWarning("Installation Cancelled", "Mod installation was cancelled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error installing mod from zip: {ex}");
                _dialogService.ShowError("Install Error", $"Error installing mod: {ex.Message}");
            }
            finally
            {
                IsLoadingRequest?.Invoke(this, false);
                if (_installSuccess)
                {
                    RequestDataRefresh?.Invoke(this, EventArgs.Empty); // Refresh data only on success
                }
            }
        }

        private async Task ExecuteInstallFromGithub(CancellationToken ct = default)
        {
            IsLoadingRequest?.Invoke(this, true);
            _installSuccess = false; // Reset flag
            try
            {
                // Step 1: Get URL
                var (result, gitUrl) = _dialogService.ShowInputDialog(
                    "Install from GitHub", "Enter the GitHub repository URL:", "");
                if (result != MessageDialogResult.OK || string.IsNullOrWhiteSpace(gitUrl)) return;
                ct.ThrowIfCancellationRequested();

                // Step 2: Validate (Uses GitHubInstallationHelper.cs)
                var modInfo = await ValidateGitHubModRepo(gitUrl, ct);
                if (modInfo == null) return; // Error shown in helper
                ct.ThrowIfCancellationRequested();

                // Step 3: Confirm
                var confirmResult = _dialogService.ShowConfirmation(
                    "Confirm Installation", $"Are you sure you want to install the mod '{modInfo.Name}'?", showCancel: true);
                if (confirmResult != MessageDialogResult.OK) return;
                ct.ThrowIfCancellationRequested();

                // Step 4: Prepare target directory
                var modsPath = _pathService.GetModsPath();
                if (string.IsNullOrEmpty(modsPath))
                {
                    _dialogService.ShowError("Path Error", "Mods directory is not set.");
                    return;
                }

                // Uses GitHubInstallationHelper.cs
                var repoInfo = ParseGitHubUrl(gitUrl);
                if (repoInfo == null) { /* Should have been caught by ValidateGitHubModRepo */ return; }
                var modName = repoInfo.Value.repo; // Use repository name as folder name
                var targetDir = Path.Combine(modsPath, modName);

                if (Directory.Exists(targetDir))
                {
                    _dialogService.ShowError("Install Error", $"A mod folder already exists at:\n{targetDir}");
                    return;
                }

                // Step 5: Clone (Uses GitHubInstallationHelper.cs)
                // Run synchronous LibGit2Sharp clone on a background thread
                await Task.Run(() => CloneGitMod(gitUrl, targetDir), ct);

                 _installSuccess = true; // Set flag for finally block
            }
            catch (OperationCanceledException)
            {
                _dialogService.ShowWarning("Installation Cancelled", "Mod installation was cancelled.");
            }
            catch (Exception ex) // Catch specific LibGit2Sharp exceptions if needed
            {
                Debug.WriteLine($"Error installing mod from GitHub: {ex}");
                _dialogService.ShowError("Install Error", $"Error installing mod: {ex.Message}");
            }
            finally
            {
                IsLoadingRequest?.Invoke(this, false);
                 if (_installSuccess)
                {
                    RequestDataRefresh?.Invoke(this, EventArgs.Empty); // Refresh data only on success
                }
            }
        }
    }
}