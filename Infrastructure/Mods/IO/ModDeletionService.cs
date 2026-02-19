#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.IO
{
    public class ModDeletionService : IModDeletionService
    {
        public async Task DeleteDirectoryRobustAsync(string path, CancellationToken ct = default)
        {
            if (!Directory.Exists(path)) return;

            await Task.Run(() =>
            {
                // Recursive method to clear attributes and delete
                DeleteRecursive(path, ct);
            }, ct);
        }

        private void DeleteRecursive(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // 1. Clear attributes of the directory itself
            ClearReadOnlyAttribute(path);

            // 2. Process all files in the directory
            foreach (var file in Directory.GetFiles(path))
            {
                ct.ThrowIfCancellationRequested();
                ClearReadOnlyAttribute(file);
                File.Delete(file);
            }

            // 3. Process all subdirectories recursively
            foreach (var dir in Directory.GetDirectories(path))
            {
                DeleteRecursive(dir, ct);
            }

            // 4. Finally, delete the (now empty) directory
            Directory.Delete(path, false);
        }

        private void ClearReadOnlyAttribute(string path)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModDeletionService] Failed to clear ReadOnly attribute on '{path}': {ex.Message}");
                // We don't throw here, as the subsequent delete might still work or will throw its own error
            }
        }
    }
}
