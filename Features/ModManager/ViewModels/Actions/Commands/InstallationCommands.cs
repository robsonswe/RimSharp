using Microsoft.Win32;
using RimSharp.Core.Commands;
using RimSharp.MyApp.AppFiles;
using RimSharp.MyApp.Dialogs;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    public partial class ModActionsViewModel
    {
        private void InitializeInstallationCommands()
        {
            InstallFromZipCommand = CreateCancellableAsyncCommand(
                ExecuteInstallFromZip,
                CanExecuteSimpleCommands,
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) });

            InstallFromGithubCommand = CreateCancellableAsyncCommand(
                ExecuteInstallFromGithub,
                CanExecuteSimpleCommands,
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) });
        }

        public async Task ExecuteInstallFromZip(CancellationToken ct)
        {
            IsLoadingRequest?.Invoke(this, true);
            _installSuccess = false;
            try
            {
                var fileDialog = new OpenFileDialog
                {
                    Filter = "Zip Files (*.zip)|*.zip",
                    Title = "Select Mod Zip File"
                };

                bool? dialogResult = null;
                await RunOnUIThreadAsync(() => dialogResult = fileDialog.ShowDialog());
                if (dialogResult != true) return;

                var zipPath = fileDialog.FileName;
                ct.ThrowIfCancellationRequested();

                if (!IsValidZipFile(zipPath))
                {
                    _dialogService.ShowError("Invalid File", "The selected file is not a valid zip file.");
                    return;
                }

                using (var archive = ZipFile.OpenRead(zipPath))
                {
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

                    var rootFolder = GetRootModFolder(archive);
                    string modName = rootFolder == null
                        ? Path.GetFileNameWithoutExtension(zipPath).Replace(" ", "")
                        : rootFolder.FullName.TrimEnd('/', '\\');

                    var targetDir = Path.Combine(modsPath, modName);

                    if (Directory.Exists(targetDir))
                    {
                        _dialogService.ShowError("Install Error", $"A mod folder already exists at:\n{targetDir}");
                        return;
                    }

                    await Task.Run(() => ExtractMod(archive, targetDir, rootFolder, ct), ct);
                    ct.ThrowIfCancellationRequested();

                    _dialogService.ShowInformation("Install Complete", $"Mod '{modInfo.Name}' was successfully installed.");
                    _installSuccess = true;
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
                    RequestDataRefresh?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private async Task ExecuteInstallFromGithub(CancellationToken ct)
        {
            IsLoadingRequest?.Invoke(this, true);
            _installSuccess = false;
            try
            {
                var dialogResultTuple = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    _dialogService.ShowInputDialog("Install from GitHub", "Enter the GitHub repository URL:", ""));

                // Deconstruct the result returned by ShowInputDialog
                (MessageDialogResult result, string gitUrl) = dialogResultTuple;

                if (result != MessageDialogResult.OK || string.IsNullOrWhiteSpace(gitUrl)) return;
                ct.ThrowIfCancellationRequested();

                var modInfo = await ValidateGitHubModRepo(gitUrl, ct);
                if (modInfo == null) return;
                ct.ThrowIfCancellationRequested();

                var confirmResult = _dialogService.ShowConfirmation(
                    "Confirm Installation", $"Are you sure you want to install the mod '{modInfo.Name}'?", showCancel: true);
                if (confirmResult != MessageDialogResult.OK) return;
                ct.ThrowIfCancellationRequested();

                var modsPath = _pathService.GetModsPath();
                if (string.IsNullOrEmpty(modsPath))
                {
                    _dialogService.ShowError("Path Error", "Mods directory is not set.");
                    return;
                }

                var repoInfo = ParseGitHubUrl(gitUrl);
                if (repoInfo == null) { return; }
                var modName = repoInfo.Value.repo;
                var targetDir = Path.Combine(modsPath, modName);

                if (Directory.Exists(targetDir))
                {
                    _dialogService.ShowError("Install Error", $"A mod folder already exists at:\n{targetDir}");
                    return;
                }

                await Task.Run(() => CloneGitMod(gitUrl, targetDir), ct);
                ct.ThrowIfCancellationRequested();

                _dialogService.ShowInformation("Install Complete", $"Mod '{modInfo.Name}' was successfully installed from GitHub.");
                _installSuccess = true;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ExecuteInstallFromGithub] Installation cancelled.");
                _dialogService.ShowWarning("Installation Cancelled", "Mod installation was cancelled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error installing mod from GitHub: {ex}");
                _dialogService.ShowError("Install Error", $"Error installing mod: {ex.Message}");
            }
            finally
            {
                IsLoadingRequest?.Invoke(this, false);
                if (_installSuccess)
                {
                    RequestDataRefresh?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }
}