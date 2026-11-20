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
        public async Task AddLoadBefore_WhenValid_ShouldAddRule()
        {

            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod" };
            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);

            _mockDialogService.ShowDependencyRuleEditorAsync(Arg.Any<DependencyRuleEditorDialogViewModel>())
                .Returns(x => 
                {
                    var dialogVm = x.Arg<DependencyRuleEditorDialogViewModel>();
                    dialogVm.PackageId = "other.mod";
                    return Task.FromResult(true);
                });

            vm.AddLoadBeforeCommand.Execute(null);
            await Task.Delay(100, TestContext.Current.CancellationToken);

            vm.CustomLoadBefore.Should().HaveCount(1);
            vm.CustomLoadBefore[0].PackageId.Should().Be("other.mod");
        }

        [Fact]
        public async Task AddLoadBefore_WhenPackageIdDuplicated_ShouldShowWarningAndNotAdd()
        {

            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod", LoadBefore = new List<string> { "dup.id" } };
            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);

            _mockDialogService.ShowDependencyRuleEditorAsync(Arg.Any<DependencyRuleEditorDialogViewModel>())
                .Returns(x => 
                {
                    var dialogVm = x.Arg<DependencyRuleEditorDialogViewModel>();
                    dialogVm.PackageId = "dup.id";
                    return Task.FromResult(true);
                });

            vm.AddLoadBeforeCommand.Execute(null);
            await Task.Delay(100, TestContext.Current.CancellationToken);

            vm.CustomLoadBefore.Should().BeEmpty();
            await _mockDialogService.Received(1).ShowWarning(Arg.Any<string>(), Arg.Is<string>(s => s.Contains("already exists")));
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

            // The save is still in flight — the dialog must remain open.
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
        public void Constructor_ShouldMirrorLoadTopFromCustomInfo()
        {
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod" };
            var customInfo = new ModCustomInfo
            {
                LoadTop = new LoadBottomRule { Value = true, Comment = new List<string> { "tier 1 please" } }
            };

            var vm = new CustomizeModDialogViewModel(mod, customInfo, _mockModService, _mockDialogService);

            vm.LoadTop.Should().BeTrue();
            vm.LoadTopComment.Should().Be("tier 1 please");
            vm.CanCustomizeLoadTop.Should().BeTrue();
        }

        [Fact]
        public void Constructor_WhenModAlreadyLoadsTop_ShouldBlockCustomization()
        {
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod", LoadTop = true };

            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);

            vm.HasOriginalLoadTop.Should().BeTrue();
            vm.CanCustomizeLoadTop.Should().BeFalse();
            vm.LoadTop.Should().BeFalse();
        }

        [Fact]
        public async Task SaveCommand_ShouldPersistLoadTop()
        {
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod" };
            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);
            vm.LoadTop = true;
            vm.LoadTopComment = "tier 1 please";

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
        public async Task SaveCommand_WhenLoadTopUnchecked_ShouldNotPersistLoadTop()
        {
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod" };
            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);

            vm.SaveCommand.Execute(null);
            await Task.Delay(100, TestContext.Current.CancellationToken);

            await _mockModService.Received(1).SaveCustomModInfoAsync(
                "test.mod",
                Arg.Is<ModCustomInfo>(info => info.LoadTop == null));
        }
    }
}


