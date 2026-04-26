using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.ModManager.Dialogs.CustomizeMod;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs.CustomizeMod
{
    public class IncompatibilityRuleEditorDialogViewModelTests
    {
        private readonly IDialogService _mockDialogService;
        private readonly IModService _mockModService;

        public IncompatibilityRuleEditorDialogViewModelTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();
            _mockDialogService = Substitute.For<IDialogService>();
            _mockModService = Substitute.For<IModService>();
        }

        [Fact]
        public void Constructor_ShouldSetTitle()
        {
            var vm = new IncompatibilityRuleEditorDialogViewModel("Test Title", _mockDialogService, _mockModService);

            vm.Title.Should().Be("Test Title");
        }

        [Fact]
        public void Constructor_WhenDialogServiceIsNull_ShouldThrow()
        {
            var act = () => new IncompatibilityRuleEditorDialogViewModel("Title", null!, _mockModService);

            act.Should().Throw<System.ArgumentNullException>().WithParameterName("dialogService");
        }

        [Fact]
        public void Constructor_WhenModServiceIsNull_ShouldThrow()
        {
            var act = () => new IncompatibilityRuleEditorDialogViewModel("Title", _mockDialogService, null!);

            act.Should().Throw<System.ArgumentNullException>().WithParameterName("modService");
        }

        [Fact]
        public void HardIncompatibility_DefaultValue_ShouldBeFalse()
        {
            var vm = new IncompatibilityRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);

            vm.HardIncompatibility.Should().BeFalse();
        }

        [Fact]
        public void HardIncompatibility_SetProperty_ShouldRaisePropertyChanged()
        {
            var vm = new IncompatibilityRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);
            var raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(vm.HardIncompatibility)) raised = true; };

            vm.HardIncompatibility = true;

            raised.Should().BeTrue();
            vm.HardIncompatibility.Should().BeTrue();
        }

        [Fact]
        public void SaveCommand_WhenPackageIdIsEmpty_ShouldShowWarningAndNotClose()
        {
            var vm = new IncompatibilityRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);
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
            var vm = new IncompatibilityRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);
            vm.PackageId = "incompatible.mod";
            bool closedRequested = false;
            vm.RequestCloseDialog += (s, e) => closedRequested = true;

            vm.SaveCommand.Execute(null);

            closedRequested.Should().BeTrue();
            vm.DialogResult.Should().BeTrue();
        }

        [Fact]
        public void SaveCommand_WhenPackageIdHasWhitespace_ShouldTrimAndSave()
        {
            var vm = new IncompatibilityRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);
            vm.PackageId = "  incompatible.mod  ";

            vm.SaveCommand.Execute(null);

            vm.PackageId.Should().Be("incompatible.mod");
        }

        [Fact]
        public void CancelCommand_ShouldCloseWithFalse()
        {
            var vm = new IncompatibilityRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);
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

            var vm = new IncompatibilityRuleEditorDialogViewModel("Title", _mockDialogService, _mockModService);

            vm.OpenModSelectorCommand.Execute(null);
            await Task.Delay(200);

            vm.PackageId.Should().Be("selected.mod");
            vm.DisplayName.Should().Be("Selected Mod");
        }
    }
}
