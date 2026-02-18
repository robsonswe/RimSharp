#nullable enable
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    public partial class ModActionsViewModel
    {
        private async Task<ModItem?> ValidateGitHubModRepo(string gitUrl, CancellationToken ct = default)
        {
            return await _gitService.ValidateGitHubModRepoAsync(gitUrl, ct);
        }

        private async Task CloneGitMod(string gitUrl, string destinationPath, CancellationToken ct = default)
        {
            Debug.WriteLine($"[CloneGitMod] Starting clone operation. Source: '{gitUrl}', Destination: '{destinationPath}'");
            try
            {
                await _gitService.CloneAsync(gitUrl, destinationPath, ct);
                Debug.WriteLine($"[CloneGitMod] Clone successful for '{gitUrl}' to '{destinationPath}'.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CloneGitMod] Error cloning repository '{gitUrl}': {ex}");
                throw new InvalidOperationException($"Failed to clone repository '{gitUrl}': {ex.Message}", ex);
            }
        }
    }
}
