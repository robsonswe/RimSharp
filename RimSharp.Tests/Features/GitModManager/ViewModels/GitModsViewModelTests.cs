using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.GitModManager.ViewModels;
using RimSharp.Features.ModManager.Services.Management;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.GitModManager.ViewModels
{
    public class GitModsViewModelTests
    {
        private readonly IModService _mockModService;
        private readonly IModListManager _mockModListManager;
        private readonly IDialogService _mockDialogService;
        private readonly IGitService _mockGitService;
        private readonly GitModsViewModel _viewModel;

        public GitModsViewModelTests()
        {
            _mockModService = Substitute.For<IModService>();
            _mockModListManager = Substitute.For<IModListManager>();
            _mockDialogService = Substitute.For<IDialogService>();
            _mockGitService = Substitute.For<IGitService>();

            _viewModel = new GitModsViewModel(_mockModService, _mockModListManager, _mockDialogService, _mockGitService);
        }

        [Fact]
        public void LoadGitMods_ShouldOnlyIncludeGitMods()
        {
            // Arrange
            var mods = new List<ModItem>
            {
                new ModItem { Name = "Git Mod", ModType = ModType.Git },
                new ModItem { Name = "Workshop Mod", ModType = ModType.WorkshopL }
            };
            _mockModListManager.GetAllMods().Returns(mods);

            // Act - Re-instantiate so LoadGitMods (called in ctor) uses the mocked return
            var vm = new GitModsViewModel(_mockModService, _mockModListManager, _mockDialogService, _mockGitService);
            
            // Assert
            vm.GitMods.Should().HaveCount(1);
            vm.GitMods.First().ModItem.Name.Should().Be("Git Mod");
        }

        [Fact]
        public async Task CheckUpdatesCommand_WhenUpdatesFound_ShouldSetStatusAndSelect()
        {
            // Arrange
            var mod = new ModItem { Name = "Test", ModType = ModType.Git, Path = @"C:\path" };
            _mockModListManager.GetAllMods().Returns(new[] { mod });
            
            // Re-initialize to pick up the mod
            var vm = new GitModsViewModel(_mockModService, _mockModListManager, _mockDialogService, _mockGitService);
            
            _mockGitService.IsRepository(mod.Path).Returns(true);
            _mockGitService.GetDivergenceAsync(mod.Path, "origin", Arg.Any<CancellationToken>())
                .Returns(new GitDivergence { BehindBy = 2, AheadBy = 0 });

            // Act
            vm.CheckUpdatesCommand.Execute(null!);
            
            // Wait for command to finish (it sets IsBusy = true then false)
            for (int i = 0; i < 100 && !vm.IsBusy; i++) await Task.Delay(10);
            for (int i = 0; i < 500 && vm.IsBusy; i++) await Task.Delay(10);

            // Assert
            vm.GitMods.First().UpdateStatus.Should().Be("2 update(s)");
            vm.GitMods.First().IsSelected.Should().BeTrue();
        }

        [Fact]
        public async Task PullUpdatesCommand_WhenSuccessful_ShouldUpdateStatus()
        {
            // Arrange
            var mod = new ModItem { Name = "Test", ModType = ModType.Git, Path = @"C:\path" };
            _mockModListManager.GetAllMods().Returns(new[] { mod });
            var vm = new GitModsViewModel(_mockModService, _mockModListManager, _mockDialogService, _mockGitService);
            vm.GitMods.First().IsSelected = true;

            _mockGitService.PullAsync(mod.Path, "origin", Arg.Any<CancellationToken>())
                .Returns(new GitPullResult { Status = GitPullStatus.FastForward });

            // Act
            vm.PullUpdatesCommand.Execute(null!);
            
            // Wait for command to finish
            for (int i = 0; i < 100 && !vm.IsBusy; i++) await Task.Delay(10);
            for (int i = 0; i < 500 && vm.IsBusy; i++) await Task.Delay(10);

            // Assert
            vm.GitMods.First().UpdateStatus.Should().Be("Updated successfully");
            vm.GitMods.First().IsSelected.Should().BeFalse();
        }
    }
}
