using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.MyApp.Dialogs;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public class CustomizeModDialogViewModel : DialogViewModelBase<ModCustomizationResult>
    {
        private readonly IModService _modService;
        private readonly ModItem _mod;
        private ModCustomInfo _customInfo;

        // Properties for binding
        private string _externalUrl;
        public string ExternalUrl
        {
            get => _externalUrl;
            set => SetProperty(ref _externalUrl, value);
        }

        private string _tags;
        public string Tags
        {
            get => _tags;
            set => SetProperty(ref _tags, value);
        }

        private string _supportedVersions;
        public string SupportedVersions
        {
            get => _supportedVersions;
            set => SetProperty(ref _supportedVersions, value);
        }

        private bool _loadBottom;
        public bool LoadBottom
        {
            get => _loadBottom;
            set => SetProperty(ref _loadBottom, value);
        }

        private string _loadBottomComment;
        public string LoadBottomComment
        {
            get => _loadBottomComment;
            set => SetProperty(ref _loadBottomComment, value);
        }

        // TODO: Add properties for LoadBefore, LoadAfter, IncompatibleWith collections
        // For simplicity, we'll use string representations for now 
        private string _loadBefore;
        public string LoadBefore
        {
            get => _loadBefore;
            set => SetProperty(ref _loadBefore, value);
        }

        private string _loadAfter;
        public string LoadAfter
        {
            get => _loadAfter;
            set => SetProperty(ref _loadAfter, value);
        }

        private string _incompatibleWith;
        public string IncompatibleWith
        {
            get => _incompatibleWith;
            set => SetProperty(ref _incompatibleWith, value);
        }

        // Commands
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public CustomizeModDialogViewModel(ModItem mod, ModCustomInfo customInfo, IModService modService) 
            : base($"Customize Mod: {mod.Name}")
        {
            _mod = mod ?? throw new ArgumentNullException(nameof(mod));
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _customInfo = customInfo ?? new ModCustomInfo();

            // Initialize properties from custom info
            ExternalUrl = _customInfo.ExternalUrl ?? mod.ExternalUrl ?? "";
            Tags = _customInfo.Tags ?? mod.Tags ?? "";
            
            // Convert list to comma-separated string
            SupportedVersions = string.Join(", ", _customInfo.SupportedVersions ?? new List<string>());
            
            // Setup LoadBottom
            LoadBottom = _customInfo.LoadBottom?.Value ?? mod.LoadBottom;
            LoadBottomComment = _customInfo.LoadBottom != null ? string.Join(", ", _customInfo.LoadBottom.Comment) : "";

            // Initialize dictionaries as comma-separated strings (simplified for now)
            // In a more complete implementation, you'd want to create proper collection editors
            LoadBefore = SerializeDependencyRules(_customInfo.LoadBefore);
            LoadAfter = SerializeDependencyRules(_customInfo.LoadAfter);
            IncompatibleWith = SerializeIncompatibilityRules(_customInfo.IncompatibleWith);

            // Setup commands
            SaveCommand = CreateCommand(Save);
            CancelCommand = CreateCommand(Cancel);
        }

        private void Save()
        {
            // Create a new custom info object or update existing
            var customInfo = new ModCustomInfo
            {
                ExternalUrl = ExternalUrl,
                Tags = Tags,
                // Parse comma-separated versions
                SupportedVersions = ParseCommaSeparatedList(SupportedVersions),
                // Setup LoadBottom if checked
                LoadBottom = LoadBottom ? new LoadBottomRule
                {
                    Value = true,
                    Comment = ParseCommaSeparatedList(LoadBottomComment)
                } : null,
                // Parse rule dictionaries (simplified)
                LoadBefore = ParseDependencyRules(LoadBefore),
                LoadAfter = ParseDependencyRules(LoadAfter),
                IncompatibleWith = ParseIncompatibilityRules(IncompatibleWith)
            };

            // Save the customizations asynchronously
            _ = Task.Run(async () => await _modService.SaveCustomModInfoAsync(_mod.PackageId, customInfo));

            CloseDialog(ModCustomizationResult.Save);
        }

        private void Cancel()
        {
            CloseDialog(ModCustomizationResult.Cancel);
        }

        // Helper methods for serializing/deserializing rules
        private string SerializeDependencyRules(Dictionary<string, ModDependencyRule> rules)
        {
            if (rules == null || rules.Count == 0)
                return string.Empty;

            // Simple serialization for demo - in a real app you'd want a proper UI for this
            List<string> items = new List<string>();
            foreach (var kvp in rules)
            {
                var rule = kvp.Value;
                string name = string.Join(", ", rule.Name);
                string comment = string.Join(", ", rule.Comment);
                items.Add($"{kvp.Key}:{name}:{comment}");
            }
            return string.Join("; ", items);
        }

        private Dictionary<string, ModDependencyRule> ParseDependencyRules(string serialized)
        {
            var result = new Dictionary<string, ModDependencyRule>();
            if (string.IsNullOrWhiteSpace(serialized))
                return result;

            string[] items = serialized.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in items)
            {
                string[] parts = item.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1)
                {
                    string packageId = parts[0].Trim();
                    string name = parts.Length > 1 ? parts[1].Trim() : "";
                    string comment = parts.Length > 2 ? parts[2].Trim() : "";

                    result[packageId] = new ModDependencyRule
                    {
                        Name = ParseCommaSeparatedList(name),
                        Comment = ParseCommaSeparatedList(comment)
                    };
                }
            }
            return result;
        }

        private string SerializeIncompatibilityRules(Dictionary<string, ModIncompatibilityRule> rules)
        {
            if (rules == null || rules.Count == 0)
                return string.Empty;

            // Simple serialization for demo
            List<string> items = new List<string>();
            foreach (var kvp in rules)
            {
                var rule = kvp.Value;
                string name = string.Join(", ", rule.Name);
                string comment = string.Join(", ", rule.Comment);
                items.Add($"{kvp.Key}:{name}:{comment}:{rule.HardIncompatibility}");
            }
            return string.Join("; ", items);
        }

        private Dictionary<string, ModIncompatibilityRule> ParseIncompatibilityRules(string serialized)
        {
            var result = new Dictionary<string, ModIncompatibilityRule>();
            if (string.IsNullOrWhiteSpace(serialized))
                return result;

            string[] items = serialized.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in items)
            {
                string[] parts = item.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1)
                {
                    string packageId = parts[0].Trim();
                    string name = parts.Length > 1 ? parts[1].Trim() : "";
                    string comment = parts.Length > 2 ? parts[2].Trim() : "";
                    bool hard = parts.Length > 3 && bool.TryParse(parts[3], out bool h) && h;

                    result[packageId] = new ModIncompatibilityRule
                    {
                        Name = ParseCommaSeparatedList(name),
                        Comment = ParseCommaSeparatedList(comment),
                        HardIncompatibility = hard
                    };
                }
            }
            return result;
        }

        private List<string> ParseCommaSeparatedList(string input)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(input))
                return result;

            string[] items = input.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in items)
            {
                result.Add(item.Trim());
            }
            return result;
        }
    }
}
