using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public class ModDependencyRuleViewModel : ViewModelBase
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

        private bool _isOriginal;
        public bool IsOriginal
        {
            get => _isOriginal;
            set => SetProperty(ref _isOriginal, value);
        }
    }

    public class ModIncompatibilityRuleViewModel : ViewModelBase
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

        private bool _isOriginal;
        public bool IsOriginal
        {
            get => _isOriginal;
            set => SetProperty(ref _isOriginal, value);
        }
    }

    public class CustomizeModDialogViewModel : DialogViewModelBase<ModCustomizationResult>
    {
        private readonly IModService _modService;
        private readonly IDialogService _dialogService;
        private readonly ModItem _mod;
        private ModCustomInfo _customInfo;

        private readonly List<string> _originalLoadBefore;
        private readonly List<string> _originalLoadAfter;
        private readonly List<string> _originalIncompatibilities;
        private readonly bool _originalLoadBottom;
        private readonly List<string> _originalSupportedVersions;
        private readonly string _originalExternalUrl;
        private readonly string _originalTags;


        // Original mod properties (display-only)
        public bool HasOriginalLoadBottom => _originalLoadBottom;
        public List<string> OriginalLoadBeforeItems => _originalLoadBefore;
        public List<string> OriginalLoadAfterItems => _originalLoadAfter;
        public List<string> OriginalIncompatibilityItems => _originalIncompatibilities;
        public List<string> OriginalSupportedVersions => _originalSupportedVersions;
        public bool HasOriginalVersions => OriginalSupportedVersions?.Any() ?? false;
        public bool HasOriginalLoadBefore => OriginalLoadBeforeItems?.Any() ?? false;
        public bool HasOriginalLoadAfter => OriginalLoadAfterItems?.Any() ?? false;
        public bool HasOriginalIncompatibilities => OriginalIncompatibilityItems?.Any() ?? false;

        // Basic properties for binding
        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set => SetProperty(ref _isFavorite, value);
        }

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

        // LoadBottom custom options
        public bool CanCustomizeLoadBottom => !HasOriginalLoadBottom;

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

        // Collection properties for rules
        public ObservableCollection<ModDependencyRuleViewModel> CustomLoadBefore { get; } = new();
        public ObservableCollection<ModDependencyRuleViewModel> CustomLoadAfter { get; } = new();
        public ObservableCollection<ModIncompatibilityRuleViewModel> CustomIncompatibilities { get; } = new();

        // Selected items for editing
        private ModDependencyRuleViewModel _selectedLoadBeforeRule;
        public ModDependencyRuleViewModel SelectedLoadBeforeRule
        {
            get => _selectedLoadBeforeRule;
            set => SetProperty(ref _selectedLoadBeforeRule, value);
        }

        private ModDependencyRuleViewModel _selectedLoadAfterRule;
        public ModDependencyRuleViewModel SelectedLoadAfterRule
        {
            get => _selectedLoadAfterRule;
            set => SetProperty(ref _selectedLoadAfterRule, value);
        }

        private ModIncompatibilityRuleViewModel _selectedIncompatibilityRule;
        public ModIncompatibilityRuleViewModel SelectedIncompatibilityRule
        {
            get => _selectedIncompatibilityRule;
            set => SetProperty(ref _selectedIncompatibilityRule, value);
        }

        // Commands
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        // Rule management commands
        public ICommand AddLoadBeforeCommand { get; }
        public ICommand EditLoadBeforeCommand { get; }
        public ICommand RemoveLoadBeforeCommand { get; }

        public ICommand AddLoadAfterCommand { get; }
        public ICommand EditLoadAfterCommand { get; }
        public ICommand RemoveLoadAfterCommand { get; }

        public ICommand AddIncompatibilityCommand { get; }
        public ICommand EditIncompatibilityCommand { get; }
        public ICommand RemoveIncompatibilityCommand { get; }

        public CustomizeModDialogViewModel(ModItem mod, ModCustomInfo customInfo, IModService modService, IDialogService dialogService)
            : base($"Customize Mod: {mod.Name}")
        {
            _mod = mod ?? throw new ArgumentNullException(nameof(mod));
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _customInfo = customInfo ?? new ModCustomInfo();

            // Determine original data by subtracting custom rules from the merged mod data
            _originalLoadBefore = mod.LoadBefore?
                .Where(id => !_customInfo.LoadBefore?.ContainsKey(id) ?? true)
                .ToList() ?? new List<string>();

            _originalLoadAfter = mod.LoadAfter?
                .Where(id => !_customInfo.LoadAfter?.ContainsKey(id) ?? true)
                .ToList() ?? new List<string>();

            _originalIncompatibilities = mod.IncompatibleWith?.Keys
                .Where(id => !(_customInfo.IncompatibleWith?.ContainsKey(id) ?? false))
                 .ToList() ?? new List<string>();

            // For simple properties, we consider them original if they match the mod's value
            // and aren't explicitly set in custom info
            _originalLoadBottom = mod.LoadBottom &&
                (_customInfo.LoadBottom == null || !_customInfo.LoadBottom.Value);

            _originalSupportedVersions = mod.SupportedVersionStrings
                .Where(v => !(_customInfo.SupportedVersions?.Contains(v) ?? false))
                .ToList();

            _originalExternalUrl = string.IsNullOrEmpty(_customInfo.ExternalUrl) ?
                mod.ExternalUrl : null;

            _originalTags = string.IsNullOrEmpty(_customInfo.Tags) ?
                mod.Tags : null;

            // Initialize basic properties
            IsFavorite = _customInfo.Favorite ?? false;
            ExternalUrl = _customInfo.ExternalUrl ?? mod.ExternalUrl ?? "";
            Tags = _customInfo.Tags ?? mod.Tags ?? "";
            SupportedVersions = string.Join(", ", _customInfo.SupportedVersions ?? new List<string>());

            // Setup LoadBottom
            LoadBottom = !HasOriginalLoadBottom && (_customInfo.LoadBottom?.Value ?? false);
            LoadBottomComment = string.Join(", ", _customInfo.LoadBottom?.Comment ?? Enumerable.Empty<string>());

            // Initialize rule collections
            InitializeRuleCollections();

            // Setup commands
            SaveCommand = CreateCommand(Save);
            CancelCommand = CreateCommand(Cancel);

            // Rule management commands with property observation
            AddLoadBeforeCommand = CreateCommand(AddLoadBefore);
            EditLoadBeforeCommand = CreateCommand(EditLoadBefore, CanEditLoadBefore, nameof(SelectedLoadBeforeRule));
            RemoveLoadBeforeCommand = CreateCommand(RemoveLoadBefore, CanRemoveLoadBefore, nameof(SelectedLoadBeforeRule));

            AddLoadAfterCommand = CreateCommand(AddLoadAfter);
            EditLoadAfterCommand = CreateCommand(EditLoadAfter, CanEditLoadAfter, nameof(SelectedLoadAfterRule));
            RemoveLoadAfterCommand = CreateCommand(RemoveLoadAfter, CanRemoveLoadAfter, nameof(SelectedLoadAfterRule));

            AddIncompatibilityCommand = CreateCommand(AddIncompatibility);
            EditIncompatibilityCommand = CreateCommand(EditIncompatibility, CanEditIncompatibility, nameof(SelectedIncompatibilityRule));
            RemoveIncompatibilityCommand = CreateCommand(RemoveIncompatibility, CanRemoveIncompatibility, nameof(SelectedIncompatibilityRule));
        }

        private void InitializeRuleCollections()
        {
            // Clear existing collections
            CustomLoadBefore.Clear();
            CustomLoadAfter.Clear();
            CustomIncompatibilities.Clear();

            // Provide an empty list if the source list is null before joining
            var emptyList = new List<string>(); // Cache an empty list for efficiency

            // LoadBefore rules - only add custom rules that aren't in original
            if (_customInfo.LoadBefore != null)
            {
                foreach (var rule in _customInfo.LoadBefore)
                {
                    // Only add if this isn't actually an original rule
                    if (!_originalLoadBefore.Contains(rule.Key))
                    {
                        CustomLoadBefore.Add(new ModDependencyRuleViewModel
                        {
                            PackageId = rule.Key,
                            // Use ?? to handle potential null lists
                            DisplayName = string.Join(", ", rule.Value.Name ?? emptyList),
                            Comment = string.Join(", ", rule.Value.Comment ?? emptyList),
                            IsOriginal = false
                        });
                    }
                }
            }

            // Similar logic for LoadAfter
            if (_customInfo.LoadAfter != null)
            {
                foreach (var rule in _customInfo.LoadAfter)
                {
                    if (!_originalLoadAfter.Contains(rule.Key))
                    {
                        CustomLoadAfter.Add(new ModDependencyRuleViewModel
                        {
                            PackageId = rule.Key,
                            // Use ?? to handle potential null lists
                            DisplayName = string.Join(", ", rule.Value.Name ?? emptyList),
                            Comment = string.Join(", ", rule.Value.Comment ?? emptyList),
                            IsOriginal = false
                        });
                    }
                }
            }

            // Similar logic for Incompatibilities
            if (_customInfo.IncompatibleWith != null)
            {
                foreach (var rule in _customInfo.IncompatibleWith)
                {
                    if (!_originalIncompatibilities.Contains(rule.Key))
                    {
                        CustomIncompatibilities.Add(new ModIncompatibilityRuleViewModel
                        {
                            PackageId = rule.Key,
                            // Use ?? to handle potential null lists
                            DisplayName = string.Join(", ", rule.Value.Name ?? emptyList),
                            Comment = string.Join(", ", rule.Value.Comment ?? emptyList),
                            HardIncompatibility = rule.Value.HardIncompatibility,
                            IsOriginal = false
                        });
                    }
                }
            }
        }

        private void Save()
        {
            var customInfo = new ModCustomInfo
            {
                Favorite = this.IsFavorite ? (bool?)true : null,
                ExternalUrl = ExternalUrl,
                Tags = Tags,
                SupportedVersions = ParseCommaSeparatedList(SupportedVersions),
                LoadBottom = CanCustomizeLoadBottom && LoadBottom
                    ? new LoadBottomRule
                    {
                        Value = true,
                        Comment = ParseCommaSeparatedList(LoadBottomComment)
                    }
                    : null,
                LoadBefore = ConvertToDependencyRules(CustomLoadBefore),
                LoadAfter = ConvertToDependencyRules(CustomLoadAfter),
                IncompatibleWith = ConvertToIncompatibilityRules(CustomIncompatibilities)
            };

            _ = Task.Run(async () => await _modService.SaveCustomModInfoAsync(_mod.PackageId, customInfo));
            CloseDialog(ModCustomizationResult.Save);
        }

        private Dictionary<string, ModDependencyRule> ConvertToDependencyRules(ObservableCollection<ModDependencyRuleViewModel> viewModels)
        {
            var rules = new Dictionary<string, ModDependencyRule>();
            foreach (var vm in viewModels)
            {
                rules[vm.PackageId] = new ModDependencyRule
                {
                    Name = ParseCommaSeparatedList(vm.DisplayName),
                    Comment = ParseCommaSeparatedList(vm.Comment)
                };
            }
            return rules;
        }

        private Dictionary<string, ModIncompatibilityRule> ConvertToIncompatibilityRules(ObservableCollection<ModIncompatibilityRuleViewModel> viewModels)
        {
            var rules = new Dictionary<string, ModIncompatibilityRule>();
            foreach (var vm in viewModels)
            {
                rules[vm.PackageId] = new ModIncompatibilityRule
                {
                    Name = ParseCommaSeparatedList(vm.DisplayName),
                    Comment = ParseCommaSeparatedList(vm.Comment),
                    HardIncompatibility = vm.HardIncompatibility
                };
            }
            return rules;
        }

        private List<string> ParseCommaSeparatedList(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new List<string>();

            return input.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .ToList();
        }

        private void Cancel()
        {
            CloseDialog(ModCustomizationResult.Cancel);
        }

        #region Validation Methods

        /// <summary>
        /// Checks if the package ID already exists in any of the lists (both original and custom)
        /// </summary>
        private bool IsPackageIdDuplicated(string packageId)
        {
            // Check original lists
            if (_originalLoadBefore.Contains(packageId) ||
                _originalLoadAfter.Contains(packageId) ||
                _originalIncompatibilities.Contains(packageId))
                return true;

            // Check custom lists
            if (CustomLoadBefore.Any(x => x.PackageId == packageId) ||
                CustomLoadAfter.Any(x => x.PackageId == packageId) ||
                CustomIncompatibilities.Any(x => x.PackageId == packageId))
                return true;

            return false;
        }

        /// <summary>
        /// Checks if the package ID exists in any list except the one specified
        /// </summary>
        private bool IsPackageIdInOtherLists(string packageId, string listType)
        {
            // Check if in LoadBefore (original or custom)
            if (listType != "LoadBefore" &&
                (_originalLoadBefore.Contains(packageId) || CustomLoadBefore.Any(x => x.PackageId == packageId)))
                return true;

            // Check if in LoadAfter (original or custom)
            if (listType != "LoadAfter" &&
                (_originalLoadAfter.Contains(packageId) || CustomLoadAfter.Any(x => x.PackageId == packageId)))
                return true;

            // Check if in IncompatibleWith (original or custom)
            if (listType != "IncompatibleWith" &&
                (_originalIncompatibilities.Contains(packageId) || CustomIncompatibilities.Any(x => x.PackageId == packageId)))
                return true;

            return false;
        }

        /// <summary>
        /// Validates a package ID before adding to LoadBefore
        /// </summary>
        private bool ValidateLoadBefore(string packageId)
        {
            // Check if already in LoadBefore
            if (_originalLoadBefore.Contains(packageId) || CustomLoadBefore.Any(x => x.PackageId == packageId))
            {
                _dialogService.ShowWarning("Duplicate Entry", $"Package ID '{packageId}' already exists in the Load Before list.");
                return false;
            }

            // Check if in other lists
            if (IsPackageIdInOtherLists(packageId, "LoadBefore"))
            {
                _dialogService.ShowWarning("Rule Conflict", $"Package ID '{packageId}' already exists in another list. " +
                    "A package ID cannot be in multiple lists.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates a package ID before adding to LoadAfter
        /// </summary>
        private bool ValidateLoadAfter(string packageId)
        {
            // Check if already in LoadAfter
            if (_originalLoadAfter.Contains(packageId) || CustomLoadAfter.Any(x => x.PackageId == packageId))
            {
                _dialogService.ShowWarning("Duplicate Entry", $"Package ID '{packageId}' already exists in the Load After list.");
                return false;
            }

            // Check if in other lists
            if (IsPackageIdInOtherLists(packageId, "LoadAfter"))
            {
                _dialogService.ShowWarning("Rule Conflict", $"Package ID '{packageId}' already exists in another list. " +
                    "A package ID cannot be in multiple lists.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates a package ID before adding to IncompatibleWith
        /// </summary>
        private bool ValidateIncompatibility(string packageId)
        {
            // Check if already in IncompatibleWith
            if (_originalIncompatibilities.Contains(packageId) || CustomIncompatibilities.Any(x => x.PackageId == packageId))
            {
                _dialogService.ShowWarning("Duplicate Entry", $"Package ID '{packageId}' already exists in the Incompatible With list.");
                return false;
            }

            // Check if in other lists
            if (IsPackageIdInOtherLists(packageId, "IncompatibleWith"))
            {
                _dialogService.ShowWarning("Rule Conflict", $"Package ID '{packageId}' already exists in another list. " +
                    "A package ID cannot be in multiple lists.");
                return false;
            }

            return true;
        }

        #endregion

        #region Rule Management Methods

        private void AddLoadBefore()
        {
            var dialogViewModel = new DependencyRuleEditorDialogViewModel("Add Load Before Rule");
            if (_dialogService.ShowDependencyRuleEditor(dialogViewModel))
            {
                // Validate the package ID before adding
                if (!ValidateLoadBefore(dialogViewModel.PackageId))
                    return;

                var newRule = new ModDependencyRuleViewModel
                {
                    PackageId = dialogViewModel.PackageId,
                    DisplayName = dialogViewModel.DisplayName,
                    Comment = dialogViewModel.Comment,
                    IsOriginal = false
                };
                CustomLoadBefore.Add(newRule);
                SelectedLoadBeforeRule = newRule;
            }
        }

        private bool CanEditLoadBefore() => SelectedLoadBeforeRule != null && !SelectedLoadBeforeRule.IsOriginal;
        private void EditLoadBefore()
        {
            if (!CanEditLoadBefore()) return;

            var dialogViewModel = new DependencyRuleEditorDialogViewModel("Edit Load Before Rule")
            {
                PackageId = SelectedLoadBeforeRule.PackageId,
                DisplayName = SelectedLoadBeforeRule.DisplayName,
                Comment = SelectedLoadBeforeRule.Comment
            };
            if (_dialogService.ShowDependencyRuleEditor(dialogViewModel))
            {
                // If the package ID changed, validate it
                if (dialogViewModel.PackageId != SelectedLoadBeforeRule.PackageId)
                {
                    if (!ValidateLoadBefore(dialogViewModel.PackageId))
                        return;
                }

                SelectedLoadBeforeRule.PackageId = dialogViewModel.PackageId;
                SelectedLoadBeforeRule.DisplayName = dialogViewModel.DisplayName;
                SelectedLoadBeforeRule.Comment = dialogViewModel.Comment;
            }
        }

        private bool CanRemoveLoadBefore() => SelectedLoadBeforeRule != null && !SelectedLoadBeforeRule.IsOriginal;
        private void RemoveLoadBefore()
        {
            if (SelectedLoadBeforeRule != null)
            {
                CustomLoadBefore.Remove(SelectedLoadBeforeRule);
            }
        }

        private void AddLoadAfter()
        {
            var dialogViewModel = new DependencyRuleEditorDialogViewModel("Add Load After Rule");
            if (_dialogService.ShowDependencyRuleEditor(dialogViewModel))
            {
                // Validate the package ID before adding
                if (!ValidateLoadAfter(dialogViewModel.PackageId))
                    return;

                var newRule = new ModDependencyRuleViewModel
                {
                    PackageId = dialogViewModel.PackageId,
                    DisplayName = dialogViewModel.DisplayName,
                    Comment = dialogViewModel.Comment,
                    IsOriginal = false
                };
                CustomLoadAfter.Add(newRule);
                SelectedLoadAfterRule = newRule;
            }
        }

        private bool CanEditLoadAfter() => SelectedLoadAfterRule != null && !SelectedLoadAfterRule.IsOriginal;
        private void EditLoadAfter()
        {
            if (!CanEditLoadAfter()) return;

            var dialogViewModel = new DependencyRuleEditorDialogViewModel("Edit Load After Rule")
            {
                PackageId = SelectedLoadAfterRule.PackageId,
                DisplayName = SelectedLoadAfterRule.DisplayName,
                Comment = SelectedLoadAfterRule.Comment
            };
            if (_dialogService.ShowDependencyRuleEditor(dialogViewModel))
            {
                // If the package ID changed, validate it
                if (dialogViewModel.PackageId != SelectedLoadAfterRule.PackageId)
                {
                    if (!ValidateLoadAfter(dialogViewModel.PackageId))
                        return;
                }

                SelectedLoadAfterRule.PackageId = dialogViewModel.PackageId;
                SelectedLoadAfterRule.DisplayName = dialogViewModel.DisplayName;
                SelectedLoadAfterRule.Comment = dialogViewModel.Comment;
            }
        }

        private bool CanRemoveLoadAfter() => SelectedLoadAfterRule != null && !SelectedLoadAfterRule.IsOriginal;
        private void RemoveLoadAfter()
        {
            if (SelectedLoadAfterRule != null)
            {
                CustomLoadAfter.Remove(SelectedLoadAfterRule);
            }
        }

        private void AddIncompatibility()
        {
            var dialogViewModel = new IncompatibilityRuleEditorDialogViewModel("Add Incompatibility Rule");
            if (_dialogService.ShowIncompatibilityRuleEditor(dialogViewModel))
            {
                // Validate the package ID before adding
                if (!ValidateIncompatibility(dialogViewModel.PackageId))
                    return;

                var newRule = new ModIncompatibilityRuleViewModel
                {
                    PackageId = dialogViewModel.PackageId,
                    DisplayName = dialogViewModel.DisplayName,
                    Comment = dialogViewModel.Comment,
                    HardIncompatibility = dialogViewModel.HardIncompatibility,
                    IsOriginal = false
                };
                CustomIncompatibilities.Add(newRule);
                SelectedIncompatibilityRule = newRule;
            }
        }

        private bool CanEditIncompatibility() => SelectedIncompatibilityRule != null && !SelectedIncompatibilityRule.IsOriginal;
        private void EditIncompatibility()
        {
            if (!CanEditIncompatibility()) return;

            var dialogViewModel = new IncompatibilityRuleEditorDialogViewModel("Edit Incompatibility Rule")
            {
                PackageId = SelectedIncompatibilityRule.PackageId,
                DisplayName = SelectedIncompatibilityRule.DisplayName,
                Comment = SelectedIncompatibilityRule.Comment,
                HardIncompatibility = SelectedIncompatibilityRule.HardIncompatibility
            };
            if (_dialogService.ShowIncompatibilityRuleEditor(dialogViewModel))
            {
                // If the package ID changed, validate it
                if (dialogViewModel.PackageId != SelectedIncompatibilityRule.PackageId)
                {
                    if (!ValidateIncompatibility(dialogViewModel.PackageId))
                        return;
                }

                SelectedIncompatibilityRule.PackageId = dialogViewModel.PackageId;
                SelectedIncompatibilityRule.DisplayName = dialogViewModel.DisplayName;
                SelectedIncompatibilityRule.Comment = dialogViewModel.Comment;
                SelectedIncompatibilityRule.HardIncompatibility = dialogViewModel.HardIncompatibility;
            }
        }

        private bool CanRemoveIncompatibility() => SelectedIncompatibilityRule != null && !SelectedIncompatibilityRule.IsOriginal;
        private void RemoveIncompatibility()
        {
            if (SelectedIncompatibilityRule != null)
            {
                CustomIncompatibilities.Remove(SelectedIncompatibilityRule);
            }
        }
        #endregion
    }
}