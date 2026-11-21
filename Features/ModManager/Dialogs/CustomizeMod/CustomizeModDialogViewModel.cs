#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Features.ModManager.Dialogs.ModSelector;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public enum CustomLoadPosition
    {
        UseInherited,
        Normal,
        ForceTop,
        ForceBottom
    }

    public enum RelationFilterKind
    {
        All,
        LoadBefore,
        LoadAfter,
        Incompatibilities,
        Inactive
    }

    public enum RuleOriginKind
    {
        Official,
        Database,
        Custom
    }

    public sealed class ModRelationRowViewModel : ViewModelBase
    {
        public string TargetPackageId { get; }
        public string DisplayName { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public string SourceLabel { get; }
        public RuleOriginKind OriginKind { get; }
        public CustomRelationKind RelationKind { get; }
        public bool IsDependency { get; }

        private bool _isDisabledByUser;
        public bool IsDisabledByUser
        {
            get => _isDisabledByUser;
            set
            {
                if (SetProperty(ref _isDisabledByUser, value))
                    NotifyComputedStates();
            }
        }

        private string? _overriddenByText;
        public string? OverriddenByText
        {
            get => _overriddenByText;
            set
            {
                if (SetProperty(ref _overriddenByText, value))
                    NotifyComputedStates();
            }
        }

        public bool IsCustom => OriginKind == RuleOriginKind.Custom;
        public bool IsInherited => !IsCustom;
        public bool IsOfficial => OriginKind == RuleOriginKind.Official;
        public bool IsDatabase => OriginKind == RuleOriginKind.Database;
        public bool IsOverriddenByCustom => IsInherited && !string.IsNullOrEmpty(OverriddenByText);
        public bool IsActiveInherited => IsInherited && !IsDisabledByUser && !IsOverriddenByCustom;
        public bool IsInactive => IsDisabledByUser || IsOverriddenByCustom;
        public bool CanToggleDisabled => IsInherited && !IsDependency && !IsOverriddenByCustom;
        public bool HasComment => !string.IsNullOrWhiteSpace(Comment);

        public bool IsHard => RelationKind == CustomRelationKind.HardIncompatibility;
        public bool IsSoftOrOrder => !IsHard;

        public string RelationLabel => RelationKind switch
        {
            CustomRelationKind.LoadBefore => IsDependency ? "Dependency" : "Load Before · Target later",
            CustomRelationKind.LoadAfter => IsDependency ? "Dependency · Target earlier" : "Load After · Target earlier",
            CustomRelationKind.SoftIncompatibility => "Soft Conflict",
            CustomRelationKind.HardIncompatibility => "Hard Conflict",
            _ => string.Empty
        };

        public string ToggleDisableText => IsDisabledByUser ? "Enable" : "Disable";

        public ICommand? ToggleDisabledCommand { get; set; }
        public ICommand? EditCommand { get; set; }
        public ICommand? RemoveCommand { get; set; }

        public ModRelationRowViewModel(
            string targetPackageId,
            string displayName,
            string comment,
            string sourceLabel,
            RuleOriginKind originKind,
            CustomRelationKind relationKind,
            bool isDependency = false)
        {
            TargetPackageId = targetPackageId;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? targetPackageId : displayName;
            Comment = comment;
            SourceLabel = sourceLabel;
            OriginKind = originKind;
            RelationKind = relationKind;
            IsDependency = isDependency;
        }

        private void NotifyComputedStates()
        {
            OnPropertyChanged(nameof(IsOverriddenByCustom));
            OnPropertyChanged(nameof(IsActiveInherited));
            OnPropertyChanged(nameof(IsInactive));
            OnPropertyChanged(nameof(CanToggleDisabled));
            OnPropertyChanged(nameof(ToggleDisableText));
        }
    }

    public class CustomizeModDialogViewModel : DialogViewModelBase<ModCustomizationResult>
    {
        private readonly IModService _modService;
        private readonly IDialogService _dialogService;
        private readonly ModItem _mod;
        private readonly ModCustomInfo _customInfo;
        private readonly ModRuleSourceRegistry? _registry;
        private readonly IModRulesService? _rulesService;
        private readonly IModDictionaryService? _dictionary;

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set => SetProperty(ref _isFavorite, value);
        }

        private string _externalUrl = string.Empty;
        public string ExternalUrl
        {
            get => _externalUrl;
            set => SetProperty(ref _externalUrl, value);
        }

        private string _tags = string.Empty;
        public string Tags
        {
            get => _tags;
            set => SetProperty(ref _tags, value);
        }

        private string _supportedVersions = string.Empty;
        public string SupportedVersions
        {
            get => _supportedVersions;
            set => SetProperty(ref _supportedVersions, value);
        }

        public List<string> OriginalSupportedVersions { get; }
        public bool HasOriginalVersions => OriginalSupportedVersions.Count > 0;

        // --- Load Order Position (Mutually Exclusive + Override-aware) ---
        public bool HasInheritedLoadTop { get; }
        public bool HasInheritedLoadBottom { get; }
        public bool HasInheritedLoadPosition => HasInheritedLoadTop || HasInheritedLoadBottom;
        public string InheritedLoadPositionLabel { get; }

        private CustomLoadPosition _loadPosition = CustomLoadPosition.UseInherited;
        public CustomLoadPosition LoadPosition
        {
            get => _loadPosition;
            set
            {
                if (SetProperty(ref _loadPosition, value))
                {
                    OnPropertyChanged(nameof(IsPosUseInherited));
                    OnPropertyChanged(nameof(IsPosNormal));
                    OnPropertyChanged(nameof(IsPosForceTop));
                    OnPropertyChanged(nameof(IsPosForceBottom));
                    OnPropertyChanged(nameof(CanEditLoadPositionReason));
                    ReevaluateOverrideAndContradictionStates();
                }
            }
        }

        public bool IsPosUseInherited => LoadPosition == CustomLoadPosition.UseInherited;
        public bool IsPosNormal => LoadPosition == CustomLoadPosition.Normal;
        public bool IsPosForceTop => LoadPosition == CustomLoadPosition.ForceTop;
        public bool IsPosForceBottom => LoadPosition == CustomLoadPosition.ForceBottom;
        public bool CanEditLoadPositionReason => LoadPosition == CustomLoadPosition.ForceTop || LoadPosition == CustomLoadPosition.ForceBottom;

        private string _loadPositionComment = string.Empty;
        public string LoadPositionComment
        {
            get => _loadPositionComment;
            set => SetProperty(ref _loadPositionComment, value);
        }

        private string _loadPositionOverrideNotice = string.Empty;
        public string LoadPositionOverrideNotice
        {
            get => _loadPositionOverrideNotice;
            private set
            {
                if (SetProperty(ref _loadPositionOverrideNotice, value))
                    OnPropertyChanged(nameof(HasLoadPositionOverrideNotice));
            }
        }
        public bool HasLoadPositionOverrideNotice => !string.IsNullOrWhiteSpace(LoadPositionOverrideNotice);

        private string _loadPositionContradictionWarning = string.Empty;
        public string LoadPositionContradictionWarning
        {
            get => _loadPositionContradictionWarning;
            private set
            {
                if (SetProperty(ref _loadPositionContradictionWarning, value))
                {
                    OnPropertyChanged(nameof(HasLoadPositionContradiction));
                    OnPropertyChanged(nameof(CanSave));
                }
            }
        }
        public bool HasLoadPositionContradiction => !string.IsNullOrWhiteSpace(LoadPositionContradictionWarning);
        public bool CanSave => !HasLoadPositionContradiction;

        // --- Unified Relations ---
        public ObservableCollection<ModRelationRowViewModel> Relations { get; } = new();
        public ObservableCollection<ModRelationRowViewModel> FilteredRelations { get; } = new();

        private RelationFilterKind _activeFilter = RelationFilterKind.All;
        public RelationFilterKind ActiveFilter
        {
            get => _activeFilter;
            set
            {
                if (SetProperty(ref _activeFilter, value))
                {
                    OnPropertyChanged(nameof(IsFilterAll));
                    OnPropertyChanged(nameof(IsFilterBefore));
                    OnPropertyChanged(nameof(IsFilterAfter));
                    OnPropertyChanged(nameof(IsFilterIncomp));
                    OnPropertyChanged(nameof(IsFilterInactive));
                    RefreshFilteredRelations();
                }
            }
        }

        public bool IsFilterAll => ActiveFilter == RelationFilterKind.All;
        public bool IsFilterBefore => ActiveFilter == RelationFilterKind.LoadBefore;
        public bool IsFilterAfter => ActiveFilter == RelationFilterKind.LoadAfter;
        public bool IsFilterIncomp => ActiveFilter == RelationFilterKind.Incompatibilities;
        public bool IsFilterInactive => ActiveFilter == RelationFilterKind.Inactive;

        public int ActiveRelationsCount => Relations.Count(r => r.IsCustom || r.IsActiveInherited);
        public bool HasNoFilteredRelations => FilteredRelations.Count == 0;

        public ICommand SetLoadPositionCommand { get; }
        public ICommand SetRelationFilterCommand { get; }
        public ICommand AddRelationCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public CustomizeModDialogViewModel(
            ModItem mod,
            ModCustomInfo customInfo,
            IModService modService,
            IDialogService dialogService,
            ModRuleSourceRegistry? registry = null,
            IModRulesService? rulesService = null,
            IModDictionaryService? dictionary = null)
            : base($"Customize Mod: {mod.DisplayName}")
        {
            _mod = mod ?? throw new ArgumentNullException(nameof(mod));
            _customInfo = customInfo ?? new ModCustomInfo();
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _registry = registry;
            _rulesService = rulesService;
            _dictionary = dictionary;

            IsFavorite = _customInfo.Favorite ?? false;
            ExternalUrl = _customInfo.ExternalUrl ?? mod.ExternalUrl ?? string.Empty;
            Tags = _customInfo.Tags ?? string.Empty;
            SupportedVersions = string.Join(", ", _customInfo.SupportedVersions ?? new List<string>());

            OriginalSupportedVersions = mod.SupportedVersions
                .Where(v => v.Source != VersionSource.Custom)
                .Select(v => v.Version)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Determine inherited load position provenance
            HasInheritedLoadTop = mod.DatabaseLoadTop || (mod.LoadTop && _customInfo.LoadTop?.Value != true);
            HasInheritedLoadBottom = mod.DatabaseLoadBottom || (mod.LoadBottom && _customInfo.LoadBottom?.Value != true);

            if (HasInheritedLoadTop)
            {
                var src = string.IsNullOrWhiteSpace(mod.DatabaseLoadTopSource)
                    ? "Mod / Rules DB"
                    : RuleProvenanceLabels.GetSourceDisplayName(mod.DatabaseLoadTopSource, _registry);
                InheritedLoadPositionLabel = $"Inherited: Force Top ({src})";
            }
            else if (HasInheritedLoadBottom)
            {
                var src = string.IsNullOrWhiteSpace(mod.DatabaseLoadBottomSource)
                    ? "Mod / Rules DB"
                    : RuleProvenanceLabels.GetSourceDisplayName(mod.DatabaseLoadBottomSource, _registry);
                InheritedLoadPositionLabel = $"Inherited: Force Bottom ({src})";
            }
            else
            {
                InheritedLoadPositionLabel = "Inherited: Normal (Auto)";
            }

            // Initialize LoadPosition choice from CustomInfo
            if (_customInfo.LoadTop?.Value == true)
            {
                _loadPosition = CustomLoadPosition.ForceTop;
                _loadPositionComment = string.Join(", ", _customInfo.LoadTop.Comment ?? Enumerable.Empty<string>());
            }
            else if (_customInfo.LoadBottom?.Value == true)
            {
                _loadPosition = CustomLoadPosition.ForceBottom;
                _loadPositionComment = string.Join(", ", _customInfo.LoadBottom.Comment ?? Enumerable.Empty<string>());
            }
            else if ((HasInheritedLoadTop && _customInfo.DisableInheritedLoadTop == true) ||
                     (HasInheritedLoadBottom && _customInfo.DisableInheritedLoadBottom == true))
            {
                _loadPosition = CustomLoadPosition.Normal;
            }
            else
            {
                _loadPosition = CustomLoadPosition.UseInherited;
            }

            SetLoadPositionCommand = CreateCommand<string>(SetLoadPosition);
            SetRelationFilterCommand = CreateCommand<string>(SetRelationFilter);
            AddRelationCommand = CreateAsyncCommand(AddRelationAsync);
            SaveCommand = CreateAsyncCommand(SaveAsync, () => CanSave, nameof(CanSave));
            CancelCommand = CreateCommand(() => CloseDialog(ModCustomizationResult.Cancel));

            BuildRelations();
            ReevaluateOverrideAndContradictionStates();
            RefreshFilteredRelations();
        }

        private void SetLoadPosition(string? param)
        {
            if (Enum.TryParse<CustomLoadPosition>(param, out var pos))
                LoadPosition = pos;
        }

        private void SetRelationFilter(string? param)
        {
            if (Enum.TryParse<RelationFilterKind>(param, out var filter))
                ActiveFilter = filter;
        }

        private void BuildRelations()
        {
            Relations.Clear();
            var dbRule = _rulesService?.GetRulesForMod(_mod.PackageId);

            var disabledBefore = new HashSet<string>(_customInfo.DisabledLoadBefore ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var disabledAfter = new HashSet<string>(_customInfo.DisabledLoadAfter ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var disabledIncomp = new HashSet<string>(_customInfo.DisabledIncompatibilities ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            // 1. Dependencies (Official About.xml; LoadAfter direction: target loads earlier)
            foreach (var dep in _mod.ModDependencies)
            {
                if (string.IsNullOrWhiteSpace(dep.PackageId)) continue;
                var name = string.IsNullOrWhiteSpace(dep.DisplayName) ? ResolveTargetName(dep.PackageId) : dep.DisplayName;
                AddRelationRow(new ModRelationRowViewModel(
                    dep.PackageId, name, "Required mod dependency",
                    "Official", RuleOriginKind.Official, CustomRelationKind.LoadAfter, isDependency: true));
            }

            // 2. Official & DB LoadBefore
            foreach (var target in _mod.AboutLoadBefore)
            {
                AddRelationRow(new ModRelationRowViewModel(
                    target, ResolveTargetName(target), string.Empty,
                    "Official", RuleOriginKind.Official, CustomRelationKind.LoadBefore)
                { IsDisabledByUser = disabledBefore.Contains(target) });
            }
            foreach (var kvp in _mod.DatabaseLoadBefore)
            {
                var srcLabel = RuleProvenanceLabels.GetSourceDisplayName(kvp.Value, _registry);
                var comment = dbRule?.LoadBefore != null && dbRule.LoadBefore.TryGetValue(kvp.Key, out var r)
                    ? string.Join(", ", r.Comment ?? Enumerable.Empty<string>())
                    : string.Empty;
                AddRelationRow(new ModRelationRowViewModel(
                    kvp.Key, ResolveTargetName(kvp.Key), comment,
                    srcLabel, RuleOriginKind.Database, CustomRelationKind.LoadBefore)
                { IsDisabledByUser = disabledBefore.Contains(kvp.Key) });
            }

            // 3. Official & DB LoadAfter
            foreach (var target in _mod.AboutLoadAfter)
            {
                AddRelationRow(new ModRelationRowViewModel(
                    target, ResolveTargetName(target), string.Empty,
                    "Official", RuleOriginKind.Official, CustomRelationKind.LoadAfter)
                { IsDisabledByUser = disabledAfter.Contains(target) });
            }
            foreach (var kvp in _mod.DatabaseLoadAfter)
            {
                var srcLabel = RuleProvenanceLabels.GetSourceDisplayName(kvp.Value, _registry);
                var comment = dbRule?.LoadAfter != null && dbRule.LoadAfter.TryGetValue(kvp.Key, out var r)
                    ? string.Join(", ", r.Comment ?? Enumerable.Empty<string>())
                    : string.Empty;
                AddRelationRow(new ModRelationRowViewModel(
                    kvp.Key, ResolveTargetName(kvp.Key), comment,
                    srcLabel, RuleOriginKind.Database, CustomRelationKind.LoadAfter)
                { IsDisabledByUser = disabledAfter.Contains(kvp.Key) });
            }

            // 4. Official & DB Incompatibilities
            foreach (var target in _mod.AboutIncompatibleWith)
            {
                var comment = $"Incompatible according to '{_mod.DisplayName}' authors";
                AddRelationRow(new ModRelationRowViewModel(
                    target, ResolveTargetName(target), comment,
                    "Official", RuleOriginKind.Official, CustomRelationKind.HardIncompatibility)
                { IsDisabledByUser = disabledIncomp.Contains(target) });
            }
            foreach (var kvp in _mod.DatabaseIncompatibleWith)
            {
                var srcLabel = RuleProvenanceLabels.GetSourceDisplayName(kvp.Value, _registry);
                ModIncompatibilityRule? ruleObj = null;
                if (!_mod.IncompatibleWith.TryGetValue(kvp.Key, out ruleObj) || _customInfo.IncompatibleWith?.ContainsKey(kvp.Key) == true)
                {
                    dbRule?.Incompatibilities?.TryGetValue(kvp.Key, out ruleObj);
                }
                var kind = ruleObj?.HardIncompatibility == true
                    ? CustomRelationKind.HardIncompatibility
                    : CustomRelationKind.SoftIncompatibility;
                var name = ruleObj?.Name?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(name)) name = ResolveTargetName(kvp.Key);
                var comment = string.Join(", ", ruleObj?.Comment ?? Enumerable.Empty<string>());

                AddRelationRow(new ModRelationRowViewModel(
                    kvp.Key, name, comment,
                    srcLabel, RuleOriginKind.Database, kind)
                { IsDisabledByUser = disabledIncomp.Contains(kvp.Key) });
            }

            // 5. Custom Rules
            if (_customInfo.LoadBefore != null)
            {
                foreach (var kvp in _customInfo.LoadBefore)
                {
                    AddRelationRow(CreateCustomRow(kvp.Key, kvp.Value.Name, kvp.Value.Comment, CustomRelationKind.LoadBefore));
                }
            }
            if (_customInfo.LoadAfter != null)
            {
                foreach (var kvp in _customInfo.LoadAfter)
                {
                    AddRelationRow(CreateCustomRow(kvp.Key, kvp.Value.Name, kvp.Value.Comment, CustomRelationKind.LoadAfter));
                }
            }
            if (_customInfo.IncompatibleWith != null)
            {
                foreach (var kvp in _customInfo.IncompatibleWith)
                {
                    var kind = kvp.Value.HardIncompatibility
                        ? CustomRelationKind.HardIncompatibility
                        : CustomRelationKind.SoftIncompatibility;
                    AddRelationRow(CreateCustomRow(kvp.Key, kvp.Value.Name, kvp.Value.Comment, kind));
                }
            }
        }

        private ModRelationRowViewModel CreateCustomRow(
            string target,
            List<string>? names,
            List<string>? comments,
            CustomRelationKind kind)
        {
            var display = names != null && names.Count > 0 ? string.Join(", ", names) : ResolveTargetName(target);
            var comment = comments != null && comments.Count > 0 ? string.Join(", ", comments) : string.Empty;
            return new ModRelationRowViewModel(target, display, comment, "Custom", RuleOriginKind.Custom, kind);
        }

        private void AddRelationRow(ModRelationRowViewModel row)
        {
            if (row.IsCustom)
            {
                row.EditCommand = CreateAsyncCommand(() => EditCustomRelationAsync(row));
                row.RemoveCommand = CreateCommand(() => RemoveCustomRelation(row));
            }
            else if (!row.IsDependency)
            {
                row.ToggleDisabledCommand = CreateCommand(() =>
                {
                    row.IsDisabledByUser = !row.IsDisabledByUser;
                    ReevaluateOverrideAndContradictionStates();
                    RefreshFilteredRelations();
                });
            }
            Relations.Add(row);
        }

        private string ResolveTargetName(string packageId)
        {
            var loaded = _modService.GetLoadedMods()
                .FirstOrDefault(m => string.Equals(m.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
            if (loaded != null) return loaded.DisplayName;

            var dictEntry = _dictionary?.GetEntryByPackageId(packageId);
            if (dictEntry != null)
            {
                if (!string.IsNullOrWhiteSpace(dictEntry.SteamName)) return dictEntry.SteamName;
                if (!string.IsNullOrWhiteSpace(dictEntry.AboutName)) return dictEntry.AboutName;
                if (!string.IsNullOrWhiteSpace(dictEntry.Name)) return dictEntry.Name;
            }
            return packageId;
        }

        private async Task AddRelationAsync()
        {
            var editor = new RelationRuleEditorDialogViewModel(
                "Add Relation Rule",
                _dialogService,
                _modService,
                GetEffectiveCustomPositionForGuard(),
                BuildTargetContext,
                BuildSelectorHints());

            if (await _dialogService.ShowRelationRuleEditorAsync(editor))
            {
                UpsertCustomRule(editor.PackageId, editor.DisplayName, editor.Comment, editor.RelationKind);
            }
        }

        private async Task EditCustomRelationAsync(ModRelationRowViewModel row)
        {
            if (!row.IsCustom) return;

            var editor = new RelationRuleEditorDialogViewModel(
                "Edit Relation Rule",
                _dialogService,
                _modService,
                GetEffectiveCustomPositionForGuard(),
                BuildTargetContext,
                BuildSelectorHints(),
                editingOriginalPackageId: row.TargetPackageId)
            {
                PackageId = row.TargetPackageId,
                DisplayName = row.DisplayName,
                Comment = row.Comment,
                RelationKind = row.RelationKind
            };

            if (await _dialogService.ShowRelationRuleEditorAsync(editor))
            {
                Relations.Remove(row);
                UpsertCustomRule(editor.PackageId, editor.DisplayName, editor.Comment, editor.RelationKind);
            }
        }

        private void UpsertCustomRule(string packageId, string displayName, string comment, CustomRelationKind kind)
        {
            var existingCustom = Relations.FirstOrDefault(r =>
                r.IsCustom && string.Equals(r.TargetPackageId, packageId, StringComparison.OrdinalIgnoreCase));
            if (existingCustom != null)
                Relations.Remove(existingCustom);

            AddRelationRow(new ModRelationRowViewModel(
                packageId,
                string.IsNullOrWhiteSpace(displayName) ? ResolveTargetName(packageId) : displayName,
                comment,
                "Custom",
                RuleOriginKind.Custom,
                kind));

            ReevaluateOverrideAndContradictionStates();
            RefreshFilteredRelations();
        }

        private void RemoveCustomRelation(ModRelationRowViewModel row)
        {
            if (!row.IsCustom) return;
            Relations.Remove(row);
            ReevaluateOverrideAndContradictionStates();
            RefreshFilteredRelations();
        }

        private CustomLoadPosition GetEffectiveCustomPositionForGuard() => LoadPosition switch
        {
            CustomLoadPosition.ForceTop => CustomLoadPosition.ForceTop,
            CustomLoadPosition.ForceBottom => CustomLoadPosition.ForceBottom,
            _ => CustomLoadPosition.Normal
        };

        private TargetRelationContext BuildTargetContext(string packageId)
        {
            var ctx = new TargetRelationContext
            {
                IsSelf = string.Equals(packageId, _mod.PackageId, StringComparison.OrdinalIgnoreCase)
            };

            foreach (var r in Relations.Where(x => string.Equals(x.TargetPackageId, packageId, StringComparison.OrdinalIgnoreCase)))
            {
                if (r.IsDependency)
                {
                    ctx.IsDependency = true;
                }
                else if (r.IsCustom)
                {
                    ctx.HasExistingCustom = true;
                    ctx.ExistingCustomLabel = r.RelationLabel;
                }
                else
                {
                    ctx.InheritedKinds.Add(r.RelationKind);
                    ctx.InheritedDescriptions.Add($"{r.SourceLabel}: {r.RelationLabel}");
                }
            }
            return ctx;
        }

        private IReadOnlyDictionary<string, ModSelectorHint> BuildSelectorHints()
        {
            var dict = new Dictionary<string, ModSelectorHint>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Relations)
            {
                if (r.IsDependency)
                {
                    dict[r.TargetPackageId] = new ModSelectorHint("Mod Dependency", ModSelectorHintKind.Dependency);
                }
                else if (r.IsCustom)
                {
                    dict[r.TargetPackageId] = new ModSelectorHint($"Custom: {r.RelationLabel}", ModSelectorHintKind.Custom);
                }
                else if (!dict.ContainsKey(r.TargetPackageId))
                {
                    if (r.IsOverriddenByCustom)
                        dict[r.TargetPackageId] = new ModSelectorHint($"Overridden ({r.SourceLabel})", ModSelectorHintKind.Overridden);
                    else if (r.IsDisabledByUser)
                        dict[r.TargetPackageId] = new ModSelectorHint($"Disabled ({r.SourceLabel})", ModSelectorHintKind.Disabled);
                    else
                        dict[r.TargetPackageId] = new ModSelectorHint($"{r.SourceLabel}: {r.RelationLabel}", ModSelectorHintKind.Inherited);
                }
            }
            return dict;
        }

        private void ReevaluateOverrideAndContradictionStates()
        {
            var customByTarget = Relations
                .Where(r => r.IsCustom)
                .ToDictionary(r => r.TargetPackageId, r => r, StringComparer.OrdinalIgnoreCase);

            foreach (var row in Relations.Where(r => r.IsInherited))
            {
                if (row.IsDependency)
                {
                    row.OverriddenByText = null;
                    continue;
                }

                if (customByTarget.TryGetValue(row.TargetPackageId, out var customRow))
                    row.OverriddenByText = $"Overridden by Custom ({customRow.RelationLabel})";
                else
                    row.OverriddenByText = null;
            }

            // Load position override feedback (Inherited vs Custom)
            if (HasInheritedLoadTop && (LoadPosition == CustomLoadPosition.Normal || LoadPosition == CustomLoadPosition.ForceBottom))
                LoadPositionOverrideNotice = $"Custom setting overrides the inherited Force Top pinning.";
            else if (HasInheritedLoadBottom && (LoadPosition == CustomLoadPosition.Normal || LoadPosition == CustomLoadPosition.ForceTop))
                LoadPositionOverrideNotice = $"Custom setting overrides the inherited Force Bottom pinning.";
            else
                LoadPositionOverrideNotice = string.Empty;

            // Custom-vs-Custom contradictions (always prevented)
            var customAfter = Relations.Where(r => r.IsCustom && r.RelationKind == CustomRelationKind.LoadAfter).ToList();
            var customBefore = Relations.Where(r => r.IsCustom && r.RelationKind == CustomRelationKind.LoadBefore).ToList();

            if (LoadPosition == CustomLoadPosition.ForceTop && customAfter.Count > 0)
            {
                LoadPositionContradictionWarning =
                    $"Custom Force Top contradicts your Custom Load After rule(s): {string.Join(", ", customAfter.Select(r => r.DisplayName))}.";
            }
            else if (LoadPosition == CustomLoadPosition.ForceBottom && customBefore.Count > 0)
            {
                LoadPositionContradictionWarning =
                    $"Custom Force Bottom contradicts your Custom Load Before rule(s): {string.Join(", ", customBefore.Select(r => r.DisplayName))}.";
            }
            else
            {
                LoadPositionContradictionWarning = string.Empty;
            }

            OnPropertyChanged(nameof(ActiveRelationsCount));
        }

        private void RefreshFilteredRelations()
        {
            FilteredRelations.Clear();
            var items = Relations.Where(r => ActiveFilter switch
            {
                RelationFilterKind.LoadBefore => r.RelationKind == CustomRelationKind.LoadBefore,
                RelationFilterKind.LoadAfter => r.RelationKind == CustomRelationKind.LoadAfter,
                RelationFilterKind.Incompatibilities => r.RelationKind == CustomRelationKind.SoftIncompatibility || r.RelationKind == CustomRelationKind.HardIncompatibility,
                RelationFilterKind.Inactive => r.IsInactive,
                _ => true
            });

            foreach (var item in items)
                FilteredRelations.Add(item);

            OnPropertyChanged(nameof(HasNoFilteredRelations));
        }

        private async Task SaveAsync()
        {
            if (!CanSave) return;

            var customInfo = new ModCustomInfo
            {
                Favorite = IsFavorite ? true : null,
                ExternalUrl = ExternalUrl,
                Tags = Tags,
                SupportedVersions = ParseCommaSeparatedList(SupportedVersions)
            };

            if (LoadPosition == CustomLoadPosition.ForceTop)
            {
                customInfo.LoadTop = new LoadBottomRule { Value = true, Comment = ParseCommaSeparatedList(LoadPositionComment) };
            }
            else if (LoadPosition == CustomLoadPosition.ForceBottom)
            {
                customInfo.LoadBottom = new LoadBottomRule { Value = true, Comment = ParseCommaSeparatedList(LoadPositionComment) };
            }
            else if (LoadPosition == CustomLoadPosition.Normal)
            {
                if (HasInheritedLoadTop) customInfo.DisableInheritedLoadTop = true;
                if (HasInheritedLoadBottom) customInfo.DisableInheritedLoadBottom = true;
            }

            foreach (var r in Relations)
            {
                if (r.IsCustom)
                {
                    if (r.RelationKind == CustomRelationKind.LoadBefore)
                    {
                        customInfo.LoadBefore[r.TargetPackageId] = new ModDependencyRule
                        {
                            Name = new List<string> { r.DisplayName },
                            Comment = ParseCommaSeparatedList(r.Comment)
                        };
                    }
                    else if (r.RelationKind == CustomRelationKind.LoadAfter)
                    {
                        customInfo.LoadAfter[r.TargetPackageId] = new ModDependencyRule
                        {
                            Name = new List<string> { r.DisplayName },
                            Comment = ParseCommaSeparatedList(r.Comment)
                        };
                    }
                    else
                    {
                        customInfo.IncompatibleWith[r.TargetPackageId] = new ModIncompatibilityRule
                        {
                            Name = new List<string> { r.DisplayName },
                            Comment = ParseCommaSeparatedList(r.Comment),
                            HardIncompatibility = r.RelationKind == CustomRelationKind.HardIncompatibility
                        };
                    }
                }
                else if (r.IsDisabledByUser && !r.IsOverriddenByCustom && !r.IsDependency)
                {
                    if (r.RelationKind == CustomRelationKind.LoadBefore)
                        customInfo.DisabledLoadBefore.Add(r.TargetPackageId);
                    else if (r.RelationKind == CustomRelationKind.LoadAfter)
                        customInfo.DisabledLoadAfter.Add(r.TargetPackageId);
                    else
                        customInfo.DisabledIncompatibilities.Add(r.TargetPackageId);
                }
            }

            try
            {
                await _modService.SaveCustomModInfoAsync(_mod.PackageId, customInfo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CustomizeModDialogViewModel] Failed to save custom mod info for '{_mod.PackageId}': {ex}");
                await _dialogService.ShowError(
                    "Could not save custom mod",
                    $"Your changes to '{_mod.Name}' could not be saved: {ex.Message}");
                return;
            }

            CloseDialog(ModCustomizationResult.Save);
        }

        private static List<string> ParseCommaSeparatedList(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new List<string>();
            return input.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();
        }
    }
}
