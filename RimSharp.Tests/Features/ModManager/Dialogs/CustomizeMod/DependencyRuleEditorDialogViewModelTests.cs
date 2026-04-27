using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.ModManager.Dialogs.CustomizeMod;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs.CustomizeMod
{
    public class DependencyRuleEditorDialogViewModelTests
    {
        private readonly IDialogService _mockDialogService;
        private readonly IModService _mockModService;

        public DependencyRuleEditorDialogViewModelTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();
            _mockDialogService = Substitute.For<IDialogService>();
            _mockModService = Substitute.For<IModService>();
        }

        [Fact]
        public void Constructor_ShouldSetTitle()
        {
            var vm = new DependencyRuleEditorDialogViewModel("Test Title", _mockDialogService, _mockModService);

            vm.Title.Should().Be("Test Title");
        }

        [Fact]
        public void Constructor_WhenDialogServiceIsNull_ShouldThrow()
        {
            var act = () => new DependencyRuleEditorDialogViewModel("Title", null!, _mockModService);

            act.Should().Throw<System.ArgumentNullException>().WithParameterName("dialogService");
        }

        [Fact]
        public void Constructor_WhenModServiceIsNull_ShouldThrow()
        {
            var act = () => new DependencyRuleEditorDialogViewModel("Title", _mockDialogService, null!);

            act.Should().Throw<System.ArgumentNullException>().WithParameterName("modService");
        }

        [Fact]
        public void PackageId_SetProperty_ShouldRaisePropertyChanged()
        {
            var vm = new DependencyRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);
            var raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(vm.PackageId)) raised = true; };

            vm.PackageId = "new.id";

            raised.Should().BeTrue();
        }

        [Fact]
        public void DisplayName_SetProperty_ShouldRaisePropertyChanged()
        {
            var vm = new DependencyRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);
            var raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(vm.DisplayName)) raised = true; };

            vm.DisplayName = "New Name";

            raised.Should().BeTrue();
        }

        [Fact]
        public void SaveCommand_WhenPackageIdIsEmpty_ShouldShowWarningAndNotClose()
        {
            var vm = new DependencyRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);
            vm.PackageId = string.Empty;
            bool closedRequested = false;
            vm.RequestCloseDialog += (s, e) => closedRequested = true;

            vm.SaveCommand.Execute(null);

            _mockDialogService.Received(1).ShowWarning(Arg.Any<string>(), Arg.Is<string>(s => s.Contains("cannot be empty")));
            closedRequested.Should().BeFalse();
        }

        [Fact]
        public void SaveCommand_WhenPackageIdIsValid_ShouldCloseWithTrue()
        {
            var vm = new DependencyRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);
            vm.PackageId = "valid.mod";
            bool closedRequested = false;
            vm.RequestCloseDialog += (s, e) => closedRequested = true;

            vm.SaveCommand.Execute(null);

            closedRequested.Should().BeTrue();
            vm.DialogResult.Should().BeTrue();
        }

        [Fact]
        public void SaveCommand_WhenPackageIdHasWhitespace_ShouldTrimAndSave()
        {
            var vm = new DependencyRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);
            vm.PackageId = "  valid.mod  ";

            vm.SaveCommand.Execute(null);

            vm.PackageId.Should().Be("valid.mod");
        }

        [Fact]
        public void CancelCommand_ShouldCloseWithFalse()
        {
            var vm = new DependencyRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);
            bool closedRequested = false;
            vm.RequestCloseDialog += (s, e) => closedRequested = true;

            vm.CancelCommand.Execute(null);

            closedRequested.Should().BeTrue();
            vm.DialogResult.Should().BeFalse();
        }

        [Fact]
        public async Task OpenModSelectorCommand_WhenModSelected_ShouldUpdatePackageIdAndDisplayName()
        {
            var selectedMod = new ModItem { PackageId = "selected.mod", Name = "Selected Mod" };
            _mockModService.GetLoadedMods().Returns(new System.Collections.Generic.List<ModItem> { selectedMod });
            _mockDialogService.ShowModSelectorDialogAsync(Arg.Any<RimSharp.Features.ModManager.Dialogs.ModSelector.ModSelectorDialogViewModel>())
                .Returns(Task.FromResult<ModItem?>(selectedMod));

            var vm = new DependencyRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);

            vm.OpenModSelectorCommand.Execute(null);
            await Task.Delay(200);

            vm.PackageId.Should().Be("selected.mod");
            vm.DisplayName.Should().Be("Selected Mod");
        }

        [Fact]
        public async Task OpenModSelectorCommand_WhenNoModSelected_ShouldNotChangePackageId()
        {
            _mockModService.GetLoadedMods().Returns(new System.Collections.Generic.List<ModItem>());
            _mockDialogService.ShowModSelectorDialogAsync(Arg.Any<RimSharp.Features.ModManager.Dialogs.ModSelector.ModSelectorDialogViewModel>())
                .Returns(Task.FromResult<ModItem?>(null));

            var vm = new DependencyRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);
            vm.PackageId = "original.id";

            vm.OpenModSelectorCommand.Execute(null);
            await Task.Delay(200);

            vm.PackageId.Should().Be("original.id");
        }
    }
}
