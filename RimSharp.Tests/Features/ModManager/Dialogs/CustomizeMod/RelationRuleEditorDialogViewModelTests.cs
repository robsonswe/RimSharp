using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.ModManager.Dialogs.CustomizeMod;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs.CustomizeMod
{
    public class RelationRuleEditorDialogViewModelTests
    {
        private readonly IDialogService _mockDialogService;
        private readonly IModService _mockModService;

        public RelationRuleEditorDialogViewModelTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();
            _mockDialogService = Substitute.For<IDialogService>();
            _mockModService = Substitute.For<IModService>();
            _mockModService.GetLoadedMods().Returns(new List<ModItem>());
        }

        [Fact]
        public void Constructor_ShouldSetTitle()
        {
            var vm = new RelationRuleEditorDialogViewModel("Test Title", _mockDialogService, _mockModService);

            vm.Title.Should().Be("Test Title");
            vm.RelationKind.Should().Be(CustomRelationKind.LoadBefore);
            vm.CanSave.Should().BeFalse();
        }

        [Fact]
        public void Constructor_WhenDialogServiceIsNull_ShouldThrow()
        {
            var act = () => new RelationRuleEditorDialogViewModel("Title", null!, _mockModService);

            act.Should().Throw<ArgumentNullException>().WithParameterName("dialogService");
        }

        [Fact]
        public void Constructor_WhenModServiceIsNull_ShouldThrow()
        {
            var act = () => new RelationRuleEditorDialogViewModel("Title", _mockDialogService, null!);

            act.Should().Throw<ArgumentNullException>().WithParameterName("modService");
        }

        [Fact]
        public void PackageId_SetProperty_ShouldRaisePropertyChanged()
        {
            var vm = new RelationRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);
            var raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(vm.PackageId)) raised = true; };

            vm.PackageId = "new.id";

            raised.Should().BeTrue();
            vm.CanSave.Should().BeTrue();
        }

        [Fact]
        public void SaveCommand_WhenPackageIdIsEmpty_ShouldNotClose()
        {
            var vm = new RelationRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);
            var closed = false;
            vm.RequestCloseDialog += (_, _) => closed = true;

            vm.SaveCommand.Execute(null);

            closed.Should().BeFalse();
        }

        [Fact]
        public void SaveCommand_WhenPackageIdIsValid_ShouldTrimAndCloseWithTrue()
        {
            var vm = new RelationRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService)
            {
                PackageId = "  valid.mod  ",
                DisplayName = "  Valid  ",
                Comment = "  note  "
            };
            var closed = false;
            vm.RequestCloseDialog += (_, _) => closed = true;

            vm.SaveCommand.Execute(null);

            closed.Should().BeTrue();
            vm.DialogResult.Should().BeTrue();
            vm.PackageId.Should().Be("valid.mod");
            vm.DisplayName.Should().Be("Valid");
            vm.Comment.Should().Be("note");
        }

        [Fact]
        public void CancelCommand_ShouldCloseWithFalse()
        {
            var vm = new RelationRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);
            var closed = false;
            vm.RequestCloseDialog += (_, _) => closed = true;

            vm.CancelCommand.Execute(null);

            closed.Should().BeTrue();
            vm.DialogResult.Should().BeFalse();
        }

        [Fact]
        public void SelfReference_ShouldBeDangerAndBlockSave()
        {
            var vm = new RelationRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService,
                contextLookup: id => new TargetRelationContext { IsSelf = true });

            vm.PackageId = "self.mod";

            vm.IsStatusDanger.Should().BeTrue();
            vm.StatusMessage.Should().Contain("cannot reference itself");
            vm.CanSave.Should().BeFalse();
        }

        [Fact]
        public void Dependency_ShouldBeDangerAndDisableOrderKinds()
        {
            var vm = new RelationRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService,
                contextLookup: id => new TargetRelationContext { IsDependency = true });

            vm.PackageId = "dep.mod";

            vm.IsStatusDanger.Should().BeTrue();
            vm.CanSelectLoadBefore.Should().BeFalse();
            vm.CanSelectLoadAfter.Should().BeFalse();
            vm.CanSelectSoftIncompat.Should().BeFalse();
            vm.CanSelectHardIncompat.Should().BeFalse();
        }

        [Fact]
        public void ForceTop_WithLoadAfter_ShouldBeDangerAndDisableLoadAfter()
        {
            var vm = new RelationRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService,
                activeLoadPosition: CustomLoadPosition.ForceTop);

            vm.PackageId = "other.mod";
            vm.SetRelationKindCommand.Execute("LoadAfter");

            vm.RelationKind.Should().Be(CustomRelationKind.LoadAfter);
            vm.IsStatusDanger.Should().BeTrue();
            vm.CanSelectLoadAfter.Should().BeFalse();
            vm.CanSave.Should().BeFalse();
        }

        [Fact]
        public void ForceBottom_WithLoadBefore_ShouldBeDangerAndDisableLoadBefore()
        {
            var vm = new RelationRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService,
                activeLoadPosition: CustomLoadPosition.ForceBottom);

            vm.PackageId = "other.mod";
            vm.SetRelationKindCommand.Execute("LoadBefore");

            vm.IsStatusDanger.Should().BeTrue();
            vm.CanSelectLoadBefore.Should().BeFalse();
            vm.CanSave.Should().BeFalse();
        }

        [Fact]
        public void InheritedRule_SameKind_ShouldWarn_DifferentKind_ShouldInfo()
        {
            var ctx = new TargetRelationContext();
            ctx.InheritedKinds.Add(CustomRelationKind.LoadBefore);
            ctx.InheritedDescriptions.Add("RimSharpDB: Load Before · Target later");
            var vm = new RelationRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService,
                contextLookup: id => ctx);

            vm.PackageId = "other.mod";
            vm.IsStatusWarning.Should().BeTrue();

            vm.SetRelationKindCommand.Execute("LoadAfter");
            vm.IsStatusInfo.Should().BeTrue();
            vm.StatusMessage.Should().Contain("override inherited rule");
        }

        [Fact]
        public void ExistingCustomRule_ShouldWarn()
        {
            var ctx = new TargetRelationContext
            {
                HasExistingCustom = true,
                ExistingCustomLabel = "Soft Conflict"
            };
            var vm = new RelationRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService,
                contextLookup: id => ctx);

            vm.PackageId = "other.mod";

            vm.IsStatusWarning.Should().BeTrue();
            vm.StatusMessage.Should().Contain("Soft Conflict");
        }

        [Fact]
        public async Task OpenModSelectorCommand_WhenModSelected_ShouldUpdatePackageIdAndDisplayName()
        {
            var selectedMod = new ModItem { PackageId = "selected.mod", Name = "Selected Mod" };
            _mockModService.GetLoadedMods().Returns(new List<ModItem> { selectedMod });
            _mockDialogService.ShowModSelectorDialogAsync(
                    Arg.Any<RimSharp.Features.ModManager.Dialogs.ModSelector.ModSelectorDialogViewModel>())
                .Returns(Task.FromResult<ModItem?>(selectedMod));

            var vm = new RelationRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);

            vm.OpenModSelectorCommand.Execute(null);
            await Task.Delay(100, TestContext.Current.CancellationToken);

            vm.PackageId.Should().Be("selected.mod");
            vm.DisplayName.Should().Be("Selected Mod");
        }
    }
}
