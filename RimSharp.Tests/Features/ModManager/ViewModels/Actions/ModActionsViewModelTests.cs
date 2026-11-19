using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.ModManager.Dialogs.MissingMods;
using RimSharp.Features.ModManager.ViewModels.Actions;
using RimSharp.Infrastructure.Mods.ModInfo;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Tests.Base;
using Xunit;

using IModCommandService = RimSharp.Shared.Services.Contracts.IModCommandService;

namespace RimSharp.Tests.Features.ModManager.ViewModels.Actions
{
    public class ModActionsViewModelTests
    {
        private readonly IModDataService _dataService = Substitute.For<IModDataService>();
        private readonly IModCommandService _commandService = Substitute.For<IModCommandService>();
        private readonly IModListIOService _ioService = Substitute.For<IModListIOService>();
        private readonly IModListManager _modListManager = Substitute.For<IModListManager>();
        private readonly IModIncompatibilityService _incompatibilityService = Substitute.For<IModIncompatibilityService>();
        private readonly IModDuplicateService _duplicateService = Substitute.For<IModDuplicateService>();
        private readonly IModDeletionService _deletionService = Substitute.For<IModDeletionService>();
        private readonly IDialogService _dialogService = Substitute.For<IDialogService>();
        private readonly IPathService _pathService = Substitute.For<IPathService>();
        private readonly IModService _modService = Substitute.For<IModService>();
        private readonly IModReplacementService _replacementService = Substitute.For<IModReplacementService>();
        private readonly IDownloadQueueService _downloadQueueService = Substitute.For<IDownloadQueueService>();
        private readonly ISteamApiClient _steamApiClient = Substitute.For<ISteamApiClient>();
        private readonly IApplicationNavigationService _navigationService = Substitute.For<IApplicationNavigationService>();
        private readonly ISteamWorkshopQueueProcessor _steamWorkshopQueueProcessor = Substitute.For<ISteamWorkshopQueueProcessor>();
        private readonly IGitService _gitService = Substitute.For<IGitService>();
        private readonly IModInfoResolver _modInfoResolver = Substitute.For<IModInfoResolver>();

        public ModActionsViewModelTests()
        {
            TestApp.InitializeTestApp();
            _modInfoResolver.ResolveAsync(
                    Arg.Any<IEnumerable<string>>(),
                    Arg.Any<IEnumerable<string>>(),
                    Arg.Any<CancellationToken>())
                .Returns(new Dictionary<string, IReadOnlyList<ModInfoResolutionCandidate>>(StringComparer.OrdinalIgnoreCase));
            _modListManager.GetAllMods().Returns(Enumerable.Empty<ModItem>());
            _modListManager.VirtualActiveMods.Returns(new List<(ModItem Mod, int LoadOrder)>());
            
            // Setup default valid paths
            _pathService.GetGamePath().Returns("C:\\Game");
            _pathService.GetModsPath().Returns("C:\\Mods");
            _pathService.GetConfigPath().Returns("C:\\Config");
        }

        private ModActionsViewModel CreateViewModel()
        {
            var gameInstance = new GameInstance { IsSteamInstallation = false };
            return new ModActionsViewModel(
                _dataService, _commandService, _ioService, _modListManager,
                _incompatibilityService, _duplicateService, _deletionService,
                _dialogService, _pathService, _modService, _replacementService,
                _downloadQueueService, _steamApiClient, _navigationService,
                _steamWorkshopQueueProcessor, _gitService,
                Substitute.For<IAIncompatibilityAgentService>(),
                Substitute.For<IModCustomService>(),
                Substitute.For<ILoggerService>(),
                gameInstance,
                modInfoResolver: _modInfoResolver);
        }

        private static SteamApiResponse ApiResponse(params SteamPublishedFileDetails[] details) => new()
        {
            Response = new SteamApiResponseDetails
            {
                Result = 1,
                ResultCount = details.Length,
                PublishedFileDetails = details.ToList()
            }
        };

        private static List<(string displayName, string packageId, string steamUrl, List<string> requiredBy, IReadOnlyList<ModInfoResolutionCandidate> candidates)>
            SteamUrlRow(string steamUrl) => new()
            {
                ("missing.dep", "missing.dep", steamUrl, new List<string> { "Mod A" }, Array.Empty<ModInfoResolutionCandidate>())
            };

        [AvaloniaFact]
        public void DeleteModCommand_WhenNoModSelected_ShouldBeDisabled()
        {

            var vm = CreateViewModel();
            vm.SelectedMod = null;

            vm.DeleteModCommand.CanExecute(null).Should().BeFalse();
        }

        [AvaloniaFact]
        public void DeleteModCommand_WhenWorkshopModSelected_ShouldBeEnabled()
        {

            var vm = CreateViewModel();
            var mod = new ModItem { ModType = ModType.Workshop, Path = "C:\\Mods\\Mod1" };

            vm.SelectedMod = mod;

            vm.DeleteModCommand.CanExecute(null).Should().BeTrue();
        }

        [AvaloniaFact]
        public void InstallFromZipCommand_WhenPathsInvalid_ShouldBeDisabled()
        {

            _pathService.GetGamePath().Returns(""); // Make it invalid
            var vm = CreateViewModel();

            vm.InstallFromZipCommand.CanExecute(null).Should().BeFalse();
        }

        [AvaloniaFact]
        public void TotalActiveIssues_ShouldSumCorrectly()
        {

            _modListManager.ActiveMissingDependenciesCount.Returns(2);
            _modListManager.ActiveIncompatibilitiesCount.Returns(3);
            _modListManager.ActiveDuplicateIssuesCount.Returns(1);
            var vm = CreateViewModel();

            vm.TotalActiveIssues.Should().Be(6);
        }

        [AvaloniaFact]
        public void CheckIncompatibilitiesCommand_WhenNoActiveMods_ShouldBeDisabled()
        {

            _modListManager.VirtualActiveMods.Returns(new List<(ModItem Mod, int LoadOrder)>());
            var vm = CreateViewModel();

            vm.CheckIncompatibilitiesCommand.CanExecute(null).Should().BeFalse();
        }

        [AvaloniaFact]
        public void CheckIncompatibilitiesCommand_WhenActiveModsExist_ShouldBeEnabled()
        {

            _modListManager.VirtualActiveMods.Returns(new List<(ModItem Mod, int LoadOrder)> { (new ModItem(), 1) });
            var vm = CreateViewModel();

            vm.CheckIncompatibilitiesCommand.CanExecute(null).Should().BeTrue();
        }

        [AvaloniaFact]
        public void ResolveDependenciesCommand_WhenPathsValid_ShouldBeEnabled()
        {

            var vm = CreateViewModel();

            vm.ResolveDependenciesCommand.CanExecute(null).Should().BeTrue();
        }

        [AvaloniaFact]
        public void OpenLogViewerCommand_WhenNotLoading_ShouldBeEnabled()
        {

            var vm = CreateViewModel();

            vm.OpenLogViewerCommand.CanExecute(null).Should().BeTrue();
        }

        [AvaloniaFact]
        public void OpenLogViewerCommand_Execution_ShouldRequestLogViewerDialog()
        {

            var vm = CreateViewModel();

            vm.OpenLogViewerCommand.Execute(null);

            _dialogService.Received().ShowLogViewerAsync();
        }

        [AvaloniaFact]
        public async Task ResolveMissingDependencyUrlsAsync_NoUrlDep_ResolverFindsIt_BecomesSelectable()
        {
            var missing = new List<(string displayName, string packageId, string steamUrl, List<string> requiredBy, IReadOnlyList<ModInfoResolutionCandidate> candidates)>
            {
                ("missing.dep", "missing.dep", "", new List<string> { "Mod A" }, Array.Empty<ModInfoResolutionCandidate>())
            };
            var candidate = new ModInfoResolutionCandidate
            {
                SteamId = "12345",
                DisplayName = "Resolved Mod",
                Published = true
            };
            var resolved = new Dictionary<string, IReadOnlyList<ModInfoResolutionCandidate>>(StringComparer.OrdinalIgnoreCase)
            {
                ["missing.dep"] = new[] { candidate }
            };
            _modInfoResolver.ResolveAsync(
                    Arg.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "missing.dep" })),
                    Arg.Any<IEnumerable<string>>(),
                    Arg.Any<CancellationToken>())
                .Returns(resolved);

            var vm = CreateViewModel();
            var transformed = await vm.ResolveMissingDependencyUrlsAsync(missing, CancellationToken.None);

            transformed.Should().ContainSingle();
            // All candidates are passed through; the row is not flattened to a single URL.
            transformed[0].steamUrl.Should().BeEmpty();
            transformed[0].displayName.Should().Be("missing.dep");
            transformed[0].candidates.Should().HaveCount(1);
            transformed[0].candidates.Should().Contain(candidate);

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(transformed);
            var group = dialog.ModGroups.Should().ContainSingle().Subject;
            group.HasSelectableVariants.Should().BeTrue();
            group.SelectedVariant!.SteamId.Should().Be("12345");
            group.SelectedVariant.IsPublished.Should().BeTrue();
        }

        [AvaloniaFact]
        public async Task ResolveMissingDependencyUrlsAsync_MultipleCandidates_PassesAllThrough()
        {
            var missing = new List<(string displayName, string packageId, string steamUrl, List<string> requiredBy, IReadOnlyList<ModInfoResolutionCandidate> candidates)>
            {
                ("missing.dep", "missing.dep", "", new List<string> { "Mod A" }, Array.Empty<ModInfoResolutionCandidate>())
            };
            var candidates = new List<ModInfoResolutionCandidate>
            {
                new() { SteamId = "111", DisplayName = "First", Published = true },
                new() { SteamId = "222", DisplayName = "Second", Published = true },
            };
            var resolved = new Dictionary<string, IReadOnlyList<ModInfoResolutionCandidate>>(StringComparer.OrdinalIgnoreCase)
            {
                ["missing.dep"] = candidates
            };
            _modInfoResolver.ResolveAsync(
                    Arg.Any<IEnumerable<string>>(),
                    Arg.Any<IEnumerable<string>>(),
                    Arg.Any<CancellationToken>())
                .Returns(resolved);

            var vm = CreateViewModel();
            var transformed = await vm.ResolveMissingDependencyUrlsAsync(missing, CancellationToken.None);

            transformed.Should().ContainSingle();
            transformed[0].steamUrl.Should().BeEmpty();
            transformed[0].candidates.Should().HaveCount(2);

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(transformed);
            var group = dialog.ModGroups.Should().ContainSingle().Subject;
            group.HasSelectableVariants.Should().BeTrue();
            group.Variants.Should().HaveCount(2);
            // Default = first published candidate.
            group.SelectedVariant!.SteamId.Should().Be("111");
        }

        [AvaloniaFact]
        public async Task ResolveMissingDependencyUrlsAsync_ZeroCandidates_KeepsRowUnselectable()
        {
            var missing = new List<(string displayName, string packageId, string steamUrl, List<string> requiredBy, IReadOnlyList<ModInfoResolutionCandidate> candidates)>
            {
                ("missing.dep", "missing.dep", "", new List<string> { "Mod A" }, Array.Empty<ModInfoResolutionCandidate>())
            };
            var empty = new Dictionary<string, IReadOnlyList<ModInfoResolutionCandidate>>(StringComparer.OrdinalIgnoreCase)
            {
                ["missing.dep"] = Array.Empty<ModInfoResolutionCandidate>()
            };
            _modInfoResolver.ResolveAsync(
                    Arg.Any<IEnumerable<string>>(),
                    Arg.Any<IEnumerable<string>>(),
                    Arg.Any<CancellationToken>())
                .Returns(empty);

            var vm = CreateViewModel();
            var transformed = await vm.ResolveMissingDependencyUrlsAsync(missing, CancellationToken.None);

            transformed.Should().ContainSingle();
            transformed[0].steamUrl.Should().BeEmpty();
            transformed[0].displayName.Should().Be("missing.dep");
            transformed[0].candidates.Should().BeEmpty();

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(transformed);
            dialog.ModGroups.Should().BeEmpty();
            dialog.UnknownModIds.Should().Contain("missing.dep");
        }

        [AvaloniaFact]
        public async Task ResolveMissingDependencyUrlsAsync_DepWithUrl_IsLeftUntouched()
        {
            var missing = new List<(string displayName, string packageId, string steamUrl, List<string> requiredBy, IReadOnlyList<ModInfoResolutionCandidate> candidates)>
            {
                ("Existing", "with.url", "https://steamcommunity.com/workshop/filedetails/?id=999", new List<string>(), Array.Empty<ModInfoResolutionCandidate>())
            };

            var vm = CreateViewModel();
            var transformed = await vm.ResolveMissingDependencyUrlsAsync(missing, CancellationToken.None);

            transformed.Should().BeEquivalentTo(missing);
            await _modInfoResolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default!, default);
        }

        [AvaloniaFact]
        public async Task ResolveMissingDependencyUrlsAsync_SteamUrlDep_NoDbCandidate_LiveApiAddsCandidate()
        {
            const string url = "steam://url/CommunityFilePage/12345";
            var missing = SteamUrlRow(url);
            var empty = new Dictionary<string, IReadOnlyList<ModInfoResolutionCandidate>>(StringComparer.OrdinalIgnoreCase)
            {
                ["missing.dep"] = Array.Empty<ModInfoResolutionCandidate>()
            };
            _modInfoResolver.ResolveAsync(
                    Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(empty);
            _steamApiClient.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(ApiResponse(new SteamPublishedFileDetails
                {
                    PublishedFileId = "12345", Title = "Live Mod", Result = 1, Creator = "76561198000000009"
                }));
            _steamApiClient.GetPersonaNameAsync("76561198000000009", Arg.Any<CancellationToken>()).Returns("Live Author");

            var vm = CreateViewModel();
            var transformed = await vm.ResolveMissingDependencyUrlsAsync(missing, CancellationToken.None);

            // The steam:// packageId is included in the resolver batch.
            await _modInfoResolver.Received(1).ResolveAsync(
                Arg.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "missing.dep" })),
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<CancellationToken>());

            transformed.Should().ContainSingle();
            transformed[0].steamUrl.Should().Be(url); // raw URL preserved
            transformed[0].candidates.Should().ContainSingle();
            var candidate = transformed[0].candidates[0];
            candidate.SteamId.Should().Be("12345");
            candidate.DisplayName.Should().Be("Live Mod");
            candidate.Published.Should().BeTrue();
            candidate.ApiDetails.Should().NotBeNull();
            candidate.DictionaryEntry.PackageId.Should().Be("missing.dep");
            candidate.DictionaryEntry.SteamId.Should().Be("12345");
            candidate.DictionaryEntry.Published.Should().BeTrue();
            candidate.Author.Should().Be("Live Author");

            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(transformed);
            var group = dialog.ModGroups.Should().ContainSingle().Subject;
            group.Variants.Should().ContainSingle();
            group.Variants[0].Authors.Should().Be("Live Author");
            group.SelectedVariant!.SteamId.Should().Be("12345");
        }

        [AvaloniaFact]
        public async Task ResolveMissingDependencyUrlsAsync_LiveApiReturnsNull_YieldsNoCandidate()
        {
            var missing = SteamUrlRow("steam://url/CommunityFilePage/12345");
            var empty = new Dictionary<string, IReadOnlyList<ModInfoResolutionCandidate>>(StringComparer.OrdinalIgnoreCase)
            {
                ["missing.dep"] = Array.Empty<ModInfoResolutionCandidate>()
            };
            _modInfoResolver.ResolveAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(empty);
            _steamApiClient.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns((SteamApiResponse?)null);

            var vm = CreateViewModel();
            var transformed = await vm.ResolveMissingDependencyUrlsAsync(missing, CancellationToken.None);

            transformed[0].candidates.Should().BeEmpty();
            var dialog = ModActionsViewModel.BuildMissingModSelectionDialog(transformed);
            dialog.UnknownModIds.Should().Contain("missing.dep");
        }

        [AvaloniaFact]
        public async Task ResolveMissingDependencyUrlsAsync_LiveApiThrows_YieldsNoCandidate()
        {
            var missing = SteamUrlRow("steam://url/CommunityFilePage/12345");
            var empty = new Dictionary<string, IReadOnlyList<ModInfoResolutionCandidate>>(StringComparer.OrdinalIgnoreCase)
            {
                ["missing.dep"] = Array.Empty<ModInfoResolutionCandidate>()
            };
            _modInfoResolver.ResolveAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(empty);
            _steamApiClient.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns<Task<SteamApiResponse?>>(_ => throw new InvalidOperationException("offline"));

            var vm = CreateViewModel();
            var transformed = await vm.ResolveMissingDependencyUrlsAsync(missing, CancellationToken.None);

            transformed[0].candidates.Should().BeEmpty();
        }

        [AvaloniaFact]
        public async Task ResolveMissingDependencyUrlsAsync_LiveApiNonOk_YieldsNoCandidate()
        {
            var missing = SteamUrlRow("steam://url/CommunityFilePage/12345");
            var empty = new Dictionary<string, IReadOnlyList<ModInfoResolutionCandidate>>(StringComparer.OrdinalIgnoreCase)
            {
                ["missing.dep"] = Array.Empty<ModInfoResolutionCandidate>()
            };
            _modInfoResolver.ResolveAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(empty);
            _steamApiClient.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(ApiResponse(new SteamPublishedFileDetails
                {
                    PublishedFileId = "12345", Title = "Gone", Result = (int)SteamApiResultCode.FileNotFound
                }));

            var vm = CreateViewModel();
            var transformed = await vm.ResolveMissingDependencyUrlsAsync(missing, CancellationToken.None);

            transformed[0].candidates.Should().BeEmpty();
        }

        [AvaloniaFact]
        public async Task ResolveMissingDependencyUrlsAsync_DeclaredPfidAlreadyInDb_NoDuplicateCandidate()
        {
            var missing = SteamUrlRow("steam://url/CommunityFilePage/12345");
            var dbCandidate = new ModInfoResolutionCandidate
            {
                SteamId = "12345", DisplayName = "Db Mod", Published = true
            };
            var resolved = new Dictionary<string, IReadOnlyList<ModInfoResolutionCandidate>>(StringComparer.OrdinalIgnoreCase)
            {
                ["missing.dep"] = new[] { dbCandidate }
            };
            _modInfoResolver.ResolveAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(resolved);
            _steamApiClient.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(ApiResponse(new SteamPublishedFileDetails
                {
                    PublishedFileId = "12345", Title = "Live Mod", Result = 1
                }));

            var vm = CreateViewModel();
            var transformed = await vm.ResolveMissingDependencyUrlsAsync(missing, CancellationToken.None);

            transformed[0].candidates.Should().ContainSingle();
            transformed[0].candidates[0].Should().BeSameAs(dbCandidate);
            await _steamApiClient.DidNotReceiveWithAnyArgs().GetPublishedFileDetailsBatchAsync(default!, default);
        }
    }
}


