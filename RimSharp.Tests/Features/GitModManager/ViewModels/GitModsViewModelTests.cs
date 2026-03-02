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

        public GitModsViewModelTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();
            _mockModService = Substitute.For<IModService>();
            _mockModListManager = Substitute.For<IModListManager>();
            _mockDialogService = Substitute.For<IDialogService>();
            _mockGitService = Substitute.For<IGitService>();
        }

        private GitModsViewModel CreateViewModel()
        {
            return new GitModsViewModel(_mockModService, _mockModListManager, _mockDialogService, _mockGitService);
        }

        [Fact]
        public async Task LoadGitMods_ShouldOnlyIncludeGitMods()
        {

            var mods = new List<ModItem>
            {
                new ModItem { Name = "Git Mod", ModType = ModType.Git },
                new ModItem { Name = "Workshop Mod", ModType = ModType.WorkshopL }
            };
            _mockModListManager.GetAllMods().Returns(mods);

            var vm = CreateViewModel();
            await WaitUntil(() => vm.GitMods.Any());

            vm.GitMods.Should().HaveCount(1);
            vm.GitMods.First().ModItem.Name.Should().Be("Git Mod");
        }

        [Fact]
        public async Task CheckUpdatesCommand_WhenUpdatesFound_ShouldSetStatusAndSelect()
        {

            var mod = new ModItem { Name = "Test", ModType = ModType.Git, Path = @"C:\path" };
            _mockModListManager.GetAllMods().Returns(new[] { mod });

            var vm = CreateViewModel();
            await WaitUntil(() => vm.GitMods != null && vm.GitMods.Any());

            _mockGitService.IsRepository(mod.Path).Returns(true);
            _mockGitService.GetDivergenceAsync(mod.Path, "origin", Arg.Any<CancellationToken>())
                .Returns(new GitDivergence { BehindBy = 2, AheadBy = 0 });

            vm.CheckUpdatesCommand.Execute(null!);
            await WaitUntil(() => vm.IsBusy);
            await WaitUntil(() => !vm.IsBusy);

            vm.GitMods.First().UpdateStatus.Should().Be("2 update(s)");
            vm.GitMods.First().IsSelected.Should().BeTrue();
        }

        [Fact]
        public async Task PullUpdatesCommand_WhenSuccessful_ShouldUpdateStatus()
        {

            var mod = new ModItem { Name = "Test", ModType = ModType.Git, Path = @"C:\path" };
            _mockModListManager.GetAllMods().Returns(new[] { mod });

            var vm = CreateViewModel();
            await WaitUntil(() => vm.GitMods != null && vm.GitMods.Any());

            vm.GitMods.First().IsSelected = true;

            _mockGitService.PullAsync(mod.Path, "origin", Arg.Any<CancellationToken>())
                .Returns(new GitPullResult { Status = GitPullStatus.FastForward });

            vm.PullUpdatesCommand.Execute(null!);
            await WaitUntil(() => vm.IsBusy);
            await WaitUntil(() => !vm.IsBusy);

            vm.GitMods.First().UpdateStatus.Should().Be("Updated successfully");
            vm.GitMods.First().IsSelected.Should().BeFalse();
        }

        private async Task WaitUntil(Func<bool> condition, int timeoutMs = 2000)
        {
            var start = DateTime.Now;
            while (!condition() && (DateTime.Now - start).TotalMilliseconds < timeoutMs)
            {
                await Task.Delay(10);
            }
        }
    }
}


