#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.AppDir.Dialogs;
using RimSharp.Features.ModManager.Dialogs.ModSelector;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public enum CustomRelationKind
    {
        LoadBefore,
        LoadAfter,
        SoftIncompatibility,
        HardIncompatibility
    }

    public enum EditorAlertLevel
    {
        None,
        Info,
        Warning,
        Danger
    }

    public class RelationRuleEditorDialogViewModel : DialogViewModelBase<bool>
    {
        private readonly IDialogService _dialogService;
        private readonly IModService _modService;
        private readonly Func<string, TargetRelationContext>? _contextLookup;
        private readonly IReadOnlyDictionary<string, ModSelectorHint>? _selectorHints;
        private readonly string? _editingOriginalPackageId;
        private readonly CustomLoadPosition _activeLoadPosition;

        private string _packageId = string.Empty;
        private string _displayName = string.Empty;
        private string _comment = string.Empty;
        private CustomRelationKind _relationKind = CustomRelationKind.LoadBefore;
        private string _statusMessage = string.Empty;
        private EditorAlertLevel _statusLevel = EditorAlertLevel.None;

        public string PackageId
        {
            get => _packageId;
            set
            {
                if (SetProperty(ref _packageId, value))
                    ReevaluateConstraints();
            }
        }

        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        public string Comment
        {
            get => _comment;
            set => SetProperty(ref _comment, value);
        }

        public CustomRelationKind RelationKind
        {
            get => _relationKind;
            set
            {
                if (SetProperty(ref _relationKind, value))
                {
                    OnPropertyChanged(nameof(IsLoadBefore));
                    OnPropertyChanged(nameof(IsLoadAfter));
                    OnPropertyChanged(nameof(IsSoftIncompat));
                    OnPropertyChanged(nameof(IsHardIncompat));
                    ReevaluateConstraints();
                }
            }
        }

        public bool IsLoadBefore => RelationKind == CustomRelationKind.LoadBefore;
        public bool IsLoadAfter => RelationKind == CustomRelationKind.LoadAfter;
        public bool IsSoftIncompat => RelationKind == CustomRelationKind.SoftIncompatibility;
        public bool IsHardIncompat => RelationKind == CustomRelationKind.HardIncompatibility;

        public bool CanSelectLoadBefore { get; private set; } = true;
        public bool CanSelectLoadAfter { get; private set; } = true;
        public bool CanSelectSoftIncompat { get; private set; } = true;
        public bool CanSelectHardIncompat { get; private set; } = true;

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
        public bool IsStatusInfo => _statusLevel == EditorAlertLevel.Info;
        public bool IsStatusWarning => _statusLevel == EditorAlertLevel.Warning;
        public bool IsStatusDanger => _statusLevel == EditorAlertLevel.Danger;
        public bool CanSave => _statusLevel != EditorAlertLevel.Danger && !string.IsNullOrWhiteSpace(PackageId);

        public ICommand SetRelationKindCommand { get; }
        public ICommand OpenModSelectorCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public RelationRuleEditorDialogViewModel(
            string title,
            IDialogService dialogService,
            IModService modService,
            CustomLoadPosition activeLoadPosition = CustomLoadPosition.Normal,
            Func<string, TargetRelationContext>? contextLookup = null,
            IReadOnlyDictionary<string, ModSelectorHint>? selectorHints = null,
            string? editingOriginalPackageId = null)
            : base(title)
        {
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _activeLoadPosition = activeLoadPosition;
            _contextLookup = contextLookup;
            _selectorHints = selectorHints;
            _editingOriginalPackageId = editingOriginalPackageId;

            SetRelationKindCommand = CreateCommand<string>(SetRelationKind);
            OpenModSelectorCommand = CreateAsyncCommand(OpenModSelectorAsync);
            SaveCommand = CreateCommand(Save, () => CanSave, nameof(CanSave));
            CancelCommand = CreateCommand(() => CloseDialog(false));

            ReevaluateConstraints();
        }

        private void SetRelationKind(string? param)
        {
            if (Enum.TryParse<CustomRelationKind>(param, out var kind))
                RelationKind = kind;
        }

        private async Task OpenModSelectorAsync()
        {
            var allMods = _modService.GetLoadedMods();
            var viewModel = new ModSelectorDialogViewModel(allMods, _selectorHints);
            if (!string.IsNullOrWhiteSpace(PackageId))
                viewModel.SearchText = PackageId;

            var selectedMod = await _dialogService.ShowModSelectorDialogAsync(viewModel);
            if (selectedMod != null)
            {
                PackageId = selectedMod.PackageId;
                DisplayName = selectedMod.DisplayName;
            }
        }

        private void ReevaluateConstraints()
        {
            var pkg = PackageId?.Trim() ?? string.Empty;
            var ctx = (!string.IsNullOrEmpty(pkg) && _contextLookup != null)
                ? _contextLookup(pkg)
                : TargetRelationContext.Empty;

            CanSelectLoadBefore = !ctx.IsDependency && _activeLoadPosition != CustomLoadPosition.ForceBottom;
            CanSelectLoadAfter = !ctx.IsDependency && _activeLoadPosition != CustomLoadPosition.ForceTop;
            CanSelectSoftIncompat = !ctx.IsDependency;
            CanSelectHardIncompat = !ctx.IsDependency;

            OnPropertyChanged(nameof(CanSelectLoadBefore));
            OnPropertyChanged(nameof(CanSelectLoadAfter));
            OnPropertyChanged(nameof(CanSelectSoftIncompat));
            OnPropertyChanged(nameof(CanSelectHardIncompat));

            if (string.IsNullOrEmpty(pkg))
            {
                SetStatus(EditorAlertLevel.None, string.Empty);
            }
            else if (ctx.IsSelf)
            {
                SetStatus(EditorAlertLevel.Danger, "A mod cannot reference itself.");
            }
            else if (ctx.IsDependency)
            {
                SetStatus(EditorAlertLevel.Danger,
                    $"'{pkg}' is a required dependency in About.xml and already loads before this mod.");
            }
            else if (RelationKind == CustomRelationKind.LoadBefore && _activeLoadPosition == CustomLoadPosition.ForceBottom)
            {
                SetStatus(EditorAlertLevel.Danger,
                    "Blocked: This mod is set to Custom Force Bottom, so it cannot Load Before another mod.");
            }
            else if (RelationKind == CustomRelationKind.LoadAfter && _activeLoadPosition == CustomLoadPosition.ForceTop)
            {
                SetStatus(EditorAlertLevel.Danger,
                    "Blocked: This mod is set to Custom Force Top, so it cannot Load After another mod.");
            }
            else if (ctx.HasExistingCustom &&
                     !string.Equals(pkg, _editingOriginalPackageId, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(EditorAlertLevel.Warning,
                    $"You already have a Custom {ctx.ExistingCustomLabel} rule for '{pkg}'. Saving will replace it.");
            }
            else if (ctx.InheritedDescriptions.Count > 0)
            {
                bool sameAsInherited = ctx.InheritedKinds.Contains(RelationKind);
                if (sameAsInherited)
                {
                    SetStatus(EditorAlertLevel.Warning,
                        $"Already defined by {string.Join(", ", ctx.InheritedDescriptions)}.");
                }
                else
                {
                    SetStatus(EditorAlertLevel.Info,
                        $"Will override inherited rule ({string.Join(", ", ctx.InheritedDescriptions)}), marking it Overridden by Custom.");
                }
            }
            else
            {
                SetStatus(EditorAlertLevel.None, string.Empty);
            }

            OnPropertyChanged(nameof(CanSave));
        }

        private void SetStatus(EditorAlertLevel level, string message)
        {
            _statusLevel = level;
            StatusMessage = message;
            OnPropertyChanged(nameof(HasStatusMessage));
            OnPropertyChanged(nameof(IsStatusInfo));
            OnPropertyChanged(nameof(IsStatusWarning));
            OnPropertyChanged(nameof(IsStatusDanger));
        }

        private void Save()
        {
            if (!CanSave) return;
            PackageId = PackageId.Trim();
            DisplayName = DisplayName?.Trim() ?? string.Empty;
            Comment = Comment?.Trim() ?? string.Empty;
            CloseDialog(true);
        }
    }

    public sealed class TargetRelationContext
    {
        public static TargetRelationContext Empty { get; } = new();
        public bool IsSelf { get; set; }
        public bool IsDependency { get; set; }
        public bool HasExistingCustom { get; set; }
        public string ExistingCustomLabel { get; set; } = string.Empty;
        public List<string> InheritedDescriptions { get; } = new();
        public HashSet<CustomRelationKind> InheritedKinds { get; } = new();
    }
}
