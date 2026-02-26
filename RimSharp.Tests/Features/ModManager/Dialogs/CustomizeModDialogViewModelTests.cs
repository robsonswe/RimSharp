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
            // Arrange
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod", ExternalUrl = "orig.url" };
            var customInfo = new ModCustomInfo { Favorite = true, ExternalUrl = "custom.url" };

            // Act
            var vm = new CustomizeModDialogViewModel(mod, customInfo, _mockModService, _mockDialogService);

            // Assert
            vm.IsFavorite.Should().BeTrue();
            vm.ExternalUrl.Should().Be("custom.url");
        }

        [Fact]
        public async Task AddLoadBefore_WhenValid_ShouldAddRule()
        {
            // Arrange
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod" };
            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);
            
            _mockDialogService.ShowDependencyRuleEditorAsync(Arg.Any<DependencyRuleEditorDialogViewModel>())
                .Returns(x => 
                {
                    var dialogVm = x.Arg<DependencyRuleEditorDialogViewModel>();
                    dialogVm.PackageId = "other.mod";
                    return Task.FromResult(true);
                });

            // Act
            vm.AddLoadBeforeCommand.Execute(null);
            await Task.Delay(100); // Wait for async command

            // Assert
            vm.CustomLoadBefore.Should().HaveCount(1);
            vm.CustomLoadBefore[0].PackageId.Should().Be("other.mod");
        }

        [Fact]
        public async Task AddLoadBefore_WhenPackageIdDuplicated_ShouldShowWarningAndNotAdd()
        {
            // Arrange
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod", LoadBefore = new List<string> { "dup.id" } };
            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);
            
            _mockDialogService.ShowDependencyRuleEditorAsync(Arg.Any<DependencyRuleEditorDialogViewModel>())
                .Returns(x => 
                {
                    var dialogVm = x.Arg<DependencyRuleEditorDialogViewModel>();
                    dialogVm.PackageId = "dup.id";
                    return Task.FromResult(true);
                });

            // Act
            vm.AddLoadBeforeCommand.Execute(null);
            await Task.Delay(100); // Wait for async command

            // Assert
            vm.CustomLoadBefore.Should().BeEmpty();
            await _mockDialogService.Received(1).ShowWarning(Arg.Any<string>(), Arg.Is<string>(s => s.Contains("already exists")));
        }

        [Fact]
        public async Task SaveCommand_ShouldCallModService()
        {
            // Arrange
            var mod = new ModItem { Name = "Test Mod", PackageId = "test.mod" };
            var vm = new CustomizeModDialogViewModel(mod, new ModCustomInfo(), _mockModService, _mockDialogService);
            vm.IsFavorite = true;

            // Act
            vm.SaveCommand.Execute(null);
            
            // Wait for Task.Run
            await Task.Delay(100);

            // Assert
            await _mockModService.Received(1).SaveCustomModInfoAsync("test.mod", Arg.Is<ModCustomInfo>(info => info.Favorite == true));
        }
    }
}
