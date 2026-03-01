#nullable enable
using RimSharp.Core.Commands;
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;
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
                        var (result, filePath) = await _dialogService.ShowOpenFileDialogAsync(
                            "Select Mod Zip File",
                            "Zip Files (*.zip)|*.zip");
        
                        if (!result || string.IsNullOrEmpty(filePath)) return;
        
                        var zipPath = filePath;
                        ct.ThrowIfCancellationRequested();
        
                        if (!IsValidZipFile(zipPath))
                        {
                            await _dialogService.ShowError("Invalid File", "The selected file is not a valid zip file.");
                            return;
                        }
        
                        using (var archive = ZipFile.OpenRead(zipPath))
                        {
                            var modInfo = await ValidateModZip(archive, ct);
                            ct.ThrowIfCancellationRequested();
        
                            if (modInfo == null)
                            {
                                await _dialogService.ShowError("Invalid Mod", "The selected zip file doesn't contain a valid RimWorld mod.");
                                return;
                            }
        
                            var confirmResult = await _dialogService.ShowConfirmationAsync(
                                "Confirm Installation",
                                $"Are you sure you want to install the mod '{modInfo.Name}'?",
                                showCancel: true);
        
                            if (confirmResult != MessageDialogResult.OK && confirmResult != MessageDialogResult.Yes) return;
                            ct.ThrowIfCancellationRequested();
        
                            var modsPath = _pathService.GetModsPath();
                            if (string.IsNullOrEmpty(modsPath))
                            {
                                await _dialogService.ShowError("Path Error", "Mods directory is not set.");
                                return;
                            }
        
                            var rootFolder = GetRootModFolder(archive);
                            string modName = rootFolder == null
                                ? Path.GetFileNameWithoutExtension(zipPath).Replace(" ", "")
                                : rootFolder.FullName.TrimEnd('/', '\\');
        
                            var targetDir = Path.Combine(modsPath, modName);
        
                            if (Directory.Exists(targetDir))
                            {
                                await _dialogService.ShowError("Install Error", $"A mod folder already exists at:\n{targetDir}");
                                return;
                            }
        
                            await Task.Run(() => ExtractMod(archive, targetDir, rootFolder, ct), ct);
                            ct.ThrowIfCancellationRequested();
        
                            await _dialogService.ShowInformation("Install Complete", $"Mod '{modInfo.Name}' was successfully installed.");
                            _installSuccess = true;
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        await _dialogService.ShowError("Install Error", $"Error installing mod: {ex.Message}");
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
                        var dialogResultTuple = await _dialogService.ShowInputDialogAsync("Install from GitHub", "Enter the GitHub repository URL:", "");
        
                        (MessageDialogResult dialogResult, string gitUrl) = dialogResultTuple;
        
                        if (dialogResult != MessageDialogResult.OK && dialogResult != MessageDialogResult.Yes || string.IsNullOrWhiteSpace(gitUrl)) return;
                        ct.ThrowIfCancellationRequested();
        
                        ProgressDialogViewModel? progressDialog = null;
                        ModItem? modInfo = null;
        
                        try
                        {
                            await RunOnUIThreadAsync(() =>
                            {
                                progressDialog = _dialogService.ShowProgressDialog(
                                    "Validating Repository",
                                    "Checking GitHub repository and fetching mod info...",
                                    canCancel: true,
                                    isIndeterminate: true,
                                    cts: null,
                                    closeable: false);
                            });
        
                            if (progressDialog == null) throw new InvalidOperationException("Failed to create progress dialog.");
        
                            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, progressDialog.CancellationToken);
                            
                            modInfo = await ValidateGitHubModRepo(gitUrl, linkedCts.Token);
                            
                            await RunOnUIThreadAsync(() => progressDialog.ForceClose());
        
                            if (modInfo == null)
                            {
                                await _dialogService.ShowError("Invalid Repository", "The provided URL does not point to a valid RimWorld mod repository.");
                                return;
                            }
        
                            var confirmResult = await _dialogService.ShowConfirmationAsync(
                                "Confirm Installation",
                                $"Are you sure you want to install '{modInfo.Name}' from GitHub?",
                                showCancel: true);
        
                            if (confirmResult != MessageDialogResult.OK && confirmResult != MessageDialogResult.Yes) return;
                            ct.ThrowIfCancellationRequested();
        
                            var modsPath = _pathService.GetModsPath();
                            if (string.IsNullOrEmpty(modsPath))
                            {
                                await _dialogService.ShowError("Path Error", "Mods directory is not set.");
                                return;
                            }
        
                            string folderName = modInfo.PackageId?.Replace(".", "_") ?? Guid.NewGuid().ToString("N");
                            var targetDir = Path.Combine(modsPath, folderName);
        
                            if (Directory.Exists(targetDir))
                            {
                                await _dialogService.ShowError("Install Error", $"A folder already exists for this mod at:\n{targetDir}");
                                return;
                            }
        
                            await RunOnUIThreadAsync(() =>
                            {
                                progressDialog = _dialogService.ShowProgressDialog(
                                    "Installing from GitHub",
                                    $"Cloning repository for '{modInfo.Name}'...",
                                    canCancel: true,
                                    isIndeterminate: true,
                                    cts: null,
                                    closeable: false);
                            });
        
                            using var linkedCtsInstall = CancellationTokenSource.CreateLinkedTokenSource(ct, progressDialog!.CancellationToken);
                            
                            await _gitService.CloneAsync(gitUrl, targetDir, linkedCtsInstall.Token);
                            
                            await RunOnUIThreadAsync(() => progressDialog.ForceClose());
        
                            await _dialogService.ShowInformation("Install Complete", $"Mod '{modInfo.Name}' was successfully installed from GitHub.");
                            _installSuccess = true;
                        }
                        catch (OperationCanceledException)
                        {
                            if (progressDialog != null) await RunOnUIThreadAsync(() => progressDialog.ForceClose());
                        }
                        catch (Exception ex)
                        {
                            if (progressDialog != null) await RunOnUIThreadAsync(() => progressDialog.ForceClose());
                            await _dialogService.ShowError("GitHub Install Error", $"An error occurred: {ex.Message}");
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        await _dialogService.ShowError("GitHub Install Error", $"An unexpected error occurred: {ex.Message}");
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
