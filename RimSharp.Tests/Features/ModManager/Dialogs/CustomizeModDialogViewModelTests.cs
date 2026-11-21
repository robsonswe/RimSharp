using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Core.Commands.Base;
using RimSharp.Features.ModManager.Dialogs.CustomizeMod;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs
{
    public class CustomizeModDialogViewModelTests
    {
        private readonly IModService _mockModService;
        private readonly IDialogService _mockDialogService;

        public CustomizeModDialogViewModelTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();
            _mockModService = Substitute.For<IModService>();
            _mockDialogService = Substitute.For<IDialogService>();
            _mockModService.GetLoadedMods().Returns(new List<ModItem>());
        }

        private void EditorAdds(string packageId, CustomRelationKind kind = CustomRelationKind.LoadBefore)
        {
            _mockDialogService.ShowRelationRuleEditorAsync(Arg.Any<RelationRuleEditorDialogViewModel>())
                .Returns(x =>
                {
                    var editor = x.Arg<RelationRuleEditorDialogViewModel>();
                    editor.PackageId = packageId;
                    editor.DisplayName = packageId;
                    editor.RelationKind = kind;
                    return Task.FromResult(true);
                });
        }

        [Fact]
        public void Constructor_ShouldInitializePropertiesFromModAndCustomInfo()
        {
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod", ExternalUrl = "orig.url" };
            var customInfo = new ModCustomInfo { Favorite = true, ExternalUrl = "custom.url" };

            var vm = new CustomizeModDialogViewModel(mod, customInfo, _mockModService, _mockDialogService);

            vm.IsFavorite.Should().BeTrue();
            vm.ExternalUrl.Should().Be("custom.url");
        }

        [Fact]
        public async Task AddRelation_WhenEditorConfirms_ShouldAddCustomRule()
        {
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod" };
            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);
            EditorAdds("other.mod");

            vm.AddRelationCommand.Execute(null);
            await Task.Delay(100, TestContext.Current.CancellationToken);

            vm.Relations.Should().ContainSingle(r => r.IsCustom && r.TargetPackageId == "other.mod");
            await _mockDialogService.Received(1).ShowRelationRuleEditorAsync(Arg.Any<RelationRuleEditorDialogViewModel>());
        }

        [Fact]
        public async Task AddRelation_WhenEditorCancels_ShouldNotAddRule()
        {
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod" };
            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);
            _mockDialogService.ShowRelationRuleEditorAsync(Arg.Any<RelationRuleEditorDialogViewModel>())
                .Returns(Task.FromResult(false));

            vm.AddRelationCommand.Execute(null);
            await Task.Delay(100, TestContext.Current.CancellationToken);

            vm.Relations.Should().BeEmpty();
        }

        [Fact]
        public async Task SaveCommand_ShouldCallModService()
        {
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod" };
            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);
            vm.IsFavorite = true;

            vm.SaveCommand.Execute(null);
            await Task.Delay(100, TestContext.Current.CancellationToken);

            await _mockModService.Received(1).SaveCustomModInfoAsync("test.mod", Arg.Is<ModCustomInfo>(info => info.Favorite == true));
        }

        [Fact]
        public async Task SaveCommand_WaitsForSaveBeforeClosingDialog()
        {
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod" };
            var saveTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _mockModService.SaveCustomModInfoAsync(Arg.Any<string>(), Arg.Any<ModCustomInfo>())
                .Returns(saveTcs.Task);

            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);
            var closed = false;
            vm.RequestCloseDialog += (_, _) => closed = true;

            var execution = ((AsyncDelegateCommand)vm.SaveCommand).ExecuteAsync();

            await Task.Delay(50, TestContext.Current.CancellationToken);
            closed.Should().BeFalse("the dialog must not close before the save completes");
            vm.DialogResultForWindow.Should().NotBeTrue();

            saveTcs.SetResult();
            await execution;

            closed.Should().BeTrue("the dialog closes only after the save completes");
            vm.DialogResultForWindow.Should().BeTrue();
        }

        [Fact]
        public async Task SaveCommand_WhenSaveFails_ShowsErrorAndDoesNotClose()
        {
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod" };
            _mockModService.SaveCustomModInfoAsync(Arg.Any<string>(), Arg.Any<ModCustomInfo>())
                .Returns(Task.FromException(new InvalidOperationException("disk full")));

            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);
            var closed = false;
            vm.RequestCloseDialog += (_, _) => closed = true;

            await ((AsyncDelegateCommand)vm.SaveCommand).ExecuteAsync();

            closed.Should().BeFalse("a failed save must not close the dialog");
            await _mockDialogService.Received(1).ShowError("Could not save custom mod", Arg.Is<string>(s => s.Contains("disk full")));
        }

        [Fact]
        public void Constructor_WhenCustomLoadTop_ShouldSelectForceTop()
        {
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod" };
            var customInfo = new ModCustomInfo
            {
                LoadTop = new LoadBottomRule { Value = true, Comment = new List<string> { "tier 1 please" } }
            };

            var vm = new CustomizeModDialogViewModel(mod, customInfo, _mockModService, _mockDialogService);

            vm.LoadPosition.Should().Be(CustomLoadPosition.ForceTop);
            vm.IsPosForceTop.Should().BeTrue();
            vm.LoadPositionComment.Should().Be("tier 1 please");
        }

        [Fact]
        public void Constructor_WhenModAlreadyLoadsTop_ShouldExposeInheritedLoadTop()
        {
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod", LoadTop = true };

            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);

            vm.HasInheritedLoadTop.Should().BeTrue();
            vm.HasInheritedLoadPosition.Should().BeTrue();
            vm.LoadPosition.Should().Be(CustomLoadPosition.UseInherited);
        }

        [Fact]
        public async Task SaveCommand_ShouldPersistForceTop()
        {
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod" };
            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);
            vm.LoadPosition = CustomLoadPosition.ForceTop;
            vm.LoadPositionComment = "tier 1 please";

            vm.SaveCommand.Execute(null);
            await Task.Delay(100, TestContext.Current.CancellationToken);

            await _mockModService.Received(1).SaveCustomModInfoAsync(
                "test.mod",
                Arg.Is<ModCustomInfo>(info =>
                    info.LoadTop != null &&
                    info.LoadTop.Value &&
                    info.LoadTop.Comment.Contains("tier 1 please")));
        }

        [Fact]
        public async Task SaveCommand_WhenNormalOverInheritedLoadTop_ShouldDisableInheritedLoadTop()
        {
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod", LoadTop = true };
            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);
            vm.LoadPosition = CustomLoadPosition.Normal;

            vm.SaveCommand.Execute(null);
            await Task.Delay(100, TestContext.Current.CancellationToken);

            await _mockModService.Received(1).SaveCustomModInfoAsync(
                "test.mod",
                Arg.Is<ModCustomInfo>(info => info.DisableInheritedLoadTop == true));
        }

        [Fact]
        public async Task ToggleDisabledCommand_OnInheritedRow_ShouldPersistDisabledRule()
        {
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod" };
            mod.AboutLoadBefore.Add("other.mod");
            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);

            var row = vm.Relations.Single(r => r.TargetPackageId == "other.mod" && r.IsInherited);
            row.CanToggleDisabled.Should().BeTrue();
            row.ToggleDisabledCommand!.Execute(null);
            row.IsDisabledByUser.Should().BeTrue();

            vm.SaveCommand.Execute(null);
            await Task.Delay(100, TestContext.Current.CancellationToken);

            await _mockModService.Received(1).SaveCustomModInfoAsync(
                "test.mod",
                Arg.Is<ModCustomInfo>(info => info.DisabledLoadBefore.Contains("other.mod")));
        }

        [Fact]
        public async Task ForceTop_WithCustomLoadAfter_ShouldBlockSave()
        {
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod" };
            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);
            EditorAdds("other.mod", CustomRelationKind.LoadAfter);

            vm.AddRelationCommand.Execute(null);
            await Task.Delay(100, TestContext.Current.CancellationToken);

            vm.LoadPosition = CustomLoadPosition.ForceTop;

            vm.HasLoadPositionContradiction.Should().BeTrue();
            vm.CanSave.Should().BeFalse();
            vm.LoadPositionContradictionWarning.Should().Contain("other.mod");
        }
    }
}
