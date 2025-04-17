using RimSharp.AppDir.AppFiles; // For RunOnUIThread
using RimSharp.Shared.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text; // For StringBuilder

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    // Partial class definition starts here
    public partial class ModActionsViewModel
    {
        // Helper method called by Open... commands
        private void OpenItems(IList selectedItems, Func<ModItem, string> targetSelector, string itemTypeDescription, Func<string, bool> validator = null)
        {
            var mods = selectedItems?.Cast<ModItem>().ToList();
            if (mods == null || !mods.Any()) return;

            var opened = new List<string>();
            var notOpened = new List<string>();
            int openLimit = 10; // Limit how many items we try to open at once

            foreach (var mod in mods)
            {
                string target = null;
                try
                {
                    target = targetSelector(mod);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error selecting target for {mod.Name}: {ex.Message}");
                    notOpened.Add($"{mod.Name}: Error retrieving target ({itemTypeDescription})");
                    continue;
                }

                bool isValid = !string.IsNullOrWhiteSpace(target) && (validator == null || validator(target));

                if (isValid)
                {
                    if (opened.Count < openLimit)
                    {
                        try
                        {
                            Debug.WriteLine($"Opening {itemTypeDescription}: {target}");
                            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                            opened.Add(mod.Name);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Could not open {itemTypeDescription} for {mod.Name} ({target}): {ex}");
                            notOpened.Add($"{mod.Name} ({itemTypeDescription}): {ex.Message}");
                        }
                    }
                    else
                    {
                        if (opened.Count == openLimit)
                        {
                            notOpened.Add($"... and {mods.Count - openLimit} more (limit of {openLimit} reached).");
                        }
                    }
                }
                else
                {
                    notOpened.Add($"{mod.Name}: No valid {itemTypeDescription} available{(validator != null ? " or path/URL does not exist" : "")}");
                }
            }

            if (notOpened.Count > 0)
            {
                var message = new StringBuilder();
                if (opened.Count > 0)
                {
                    message.AppendLine($"Opened {itemTypeDescription} for {opened.Count} mod(s).");
                    message.AppendLine();
                }
                message.AppendLine($"Could not open {itemTypeDescription} for:");
                message.AppendLine(string.Join("\n - ", notOpened.Prepend("")));

                // ***** This is the line that uses the extension method *****
                RunOnUIThread(() => _dialogService.ShowWarning($"Open {itemTypeDescription.CapitalizeFirst()}", message.ToString().Trim()));
            }
        }

    }

    public static class StringExtensions
    {
        public static string CapitalizeFirst(this string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            if (input.Length == 1) return input.ToUpper();
            return char.ToUpper(input[0]) + input.Substring(1);
        }
    }

}