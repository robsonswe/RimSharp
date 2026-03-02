using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.MissingMods;
using RimSharp.Shared.Models;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs.MissingMods
{
    public class MissingModSelectionDialogViewModelTests
    {
        public MissingModSelectionDialogViewModelTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaFact]
        public void DownloadCommand_WhenSelectionsMissing_ShouldBeDisabled()
        {

            var group = new MissingModGroupViewModel("mod.a");
            var variant = new MissingModVariantViewModel(new ModDictionaryEntry { Published = true, SteamId = "123" });
            group.Variants.Add(variant);

var vm = new MissingModSelectionDialogViewModel(new List<MissingModGroupViewModel> { group }, new List<string>());

            vm.DownloadCommand.CanExecute(null).Should().BeFalse();
        }

        [AvaloniaFact]
        public void DownloadCommand_WhenSelectionsMade_ShouldBeEnabled()
        {

            var group = new MissingModGroupViewModel("mod.a");
            var variant = new MissingModVariantViewModel(new ModDictionaryEntry { Published = true, SteamId = "123" });
            group.Variants.Add(variant);
            
            var vm = new MissingModSelectionDialogViewModel(new List<MissingModGroupViewModel> { group }, new List<string>());

            group.SelectedVariant = variant;

            vm.DownloadCommand.CanExecute(null).Should().BeTrue();
        }

        [AvaloniaFact]
        public void ExecuteDownload_ShouldReturnCorrectOutput()
        {

            var group = new MissingModGroupViewModel("mod.a");
            var variant = new MissingModVariantViewModel(new ModDictionaryEntry { Published = true, SteamId = "123" });
            group.Variants.Add(variant);
            group.SelectedVariant = variant;

            var vm = new MissingModSelectionDialogViewModel(new List<MissingModGroupViewModel> { group }, new List<string>());
            
            bool closeRequested = false;
            vm.RequestCloseDialog += (s, e) => closeRequested = true;

            vm.DownloadCommand.Execute(null);

            vm.DialogResult.Should().NotBeNull();
            vm.DialogResult.Result.Should().Be(MissingModSelectionResult.Download);
            vm.DialogResult.SelectedSteamIds.Should().Contain("123");
            closeRequested.Should().BeTrue();
        }
    }
}


