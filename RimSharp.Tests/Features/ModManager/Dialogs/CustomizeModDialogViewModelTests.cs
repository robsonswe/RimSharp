using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
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


