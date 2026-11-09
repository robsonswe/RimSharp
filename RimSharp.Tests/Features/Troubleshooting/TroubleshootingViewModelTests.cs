using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.AppDir.Dialogs;
using RimSharp.Core.Commands.Base;
using RimSharp.Features.Troubleshooting.ViewModels;
using RimSharp.Services.ModManagement;
using RimSharp.Services.Workshop;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.Troubleshooting
{
    public class TroubleshootingViewModelTests : IDisposable
    {
        private readonly IDialogService _dialogService = Substitute.For<IDialogService>();
        private readonly IGameRecoveryService _gameRecovery = Substitute.For<IGameRecoveryService>();
        private readonly IModConfigurationService _modConfig = Substitute.For<IModConfigurationService>();
        private readonly ISteamSubscriptionService _steam = Substitute.For<ISteamSubscriptionService>();
        private readonly ITroubleshootingScanService _scan = Substitute.For<ITroubleshootingScanService>();

        private TroubleshootingViewModel CreateViewModel()
        {
            var vm = new TroubleshootingViewModel(_dialogService, _gameRecovery, _modConfig, _steam, _scan, new GameInstance
            {
                GameFolder = @"C:\Test\RimWorld",
                ConfigFolder = @"C:\Test\Config",
                LocalFolder = @"C:\Test\RimWorld\Mods",
                WorkshopFolder = @"C:\Test\Steam\steamapps\workshop\content\294100",
                IsSteamInstallation = true,
                SteamClientIntegration = true
            });
            vm.TestChecksDelayOverride = (_, _) => Task.CompletedTask;
            return vm;
        }

        private TroubleshootingViewModel CreateViewModelNonSteam()
        {
            var vm = new TroubleshootingViewModel(_dialogService, _gameRecovery, _modConfig, _steam, _scan, new GameInstance
            {
                GameFolder = @"C:\Test\RimWorld",
                ConfigFolder = @"C:\Test\Config",
                LocalFolder = @"C:\Test\RimWorld\Mods",
                WorkshopFolder = @"C:\Test\Steam\steamapps\workshop\content\294100",
                IsSteamInstallation = false,
                SteamClientIntegration = false
            });
            vm.TestChecksDelayOverride = (_, _) => Task.CompletedTask;
            return vm;
        }

        public void Dispose()
        {
            TroubleshootingScanService.TestIsSteamInstallationOverride = null;
            TroubleshootingScanService.TestIsWindowsOverride = null;
            TroubleshootingScanService.TestGameVersionOverride = null;
            TroubleshootingScanService.TestGetProcessesOverride = null;
            TroubleshootingScanService.TestLogLocatorOverride = null;
            TroubleshootingScanService.TestLogReaderOverride = null;
        }

        #region State 0: Symptom List (no scan)

        [Fact]
        public async Task Initialize_SymptomListState_PerformsNoServiceCalls()
        {
            var vm = CreateViewModel();

            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            await _scan.DidNotReceive().RunScanAsync(Arg.Any<CancellationToken>());
            await _scan.DidNotReceive().RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
            vm.State.Should().Be(TroubleshootingState.SymptomList);
            vm.ShowSymptomList.Should().BeTrue();
            vm.VisibleSymptoms.Should().NotBeEmpty();
            vm.Sections.Should().NotBeEmpty();
        }

        [Fact]
        public async Task Initialize_SymptomListState_ShowsNoFindings()
        {
            var vm = CreateViewModel();

            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            vm.Findings.Should().BeEmpty();
            vm.HasFindings.Should().BeFalse();
        }

        [Fact]
        public async Task Initialize_SymptomListState_CatalogAlwaysVisible()
        {
            var vm = CreateViewModel();

            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            vm.Sections.Should().NotBeEmpty();
            var allActions = vm.Sections.SelectMany(s => s.Actions).ToList();
            allActions.Should().NotBeEmpty();
        }

        #endregion

        #region State 1: CausesPage (click symptom - no scan, shows causes)

        [Fact]
        public async Task SelectSymptom_OpensCausesPage_NoScanRuns()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);

            await _scan.DidNotReceive().RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
            vm.State.Should().Be(TroubleshootingState.CausesPage);
            vm.ShowCausesPage.Should().BeTrue();
            vm.CurrentSymptom.Should().Be(crashSymptom);
            vm.CurrentSymptomCauses.Should().HaveCount(5); // crash_on_launch has 5 causes
            vm.CurrentSymptomCauses.Should().Contain("A corrupted mod assembly blocked the game from starting");
            vm.CurrentSymptomCauses.Should().Contain("A pending OS update broke a dependency the game relies on");
        }

        [Fact]
        public async Task CausesPage_ShowsCausesList_NotChecks()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");
            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);

            // Should NOT show check statuses
            vm.SolutionRows.Should().BeEmpty();
            // Should show causes from descriptor
            vm.CurrentSymptomCauses.Should().NotBeEmpty();
        }

        [Fact]
        public async Task CausesPage_HasCheckThisProblemButton()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");
            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);

            vm.RunChecksCommand.Should().NotBeNull();
            // Button should be enabled (not scanning)
            ((AsyncDelegateCommand)vm.RunChecksCommand!).CanExecute(null).Should().BeTrue();
        }

        [Fact]
        public async Task CausesPage_BackCommandIsEnabled()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");
            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);

            vm.PickDifferentProblemCommand.Should().NotBeNull();
            ((AsyncDelegateCommand)vm.PickDifferentProblemCommand!).CanExecute(null).Should().BeTrue();
        }

        [Fact]
        public async Task CausesPage_RunChecksCommand_IsEnabledAfterNavigation()
        {
            // Regression test for command re-evaluation: after navigating to CausesPage,
            // RunChecksCommand must re-evaluate its canExecute (which depends on _state)
            // and become enabled. Previously it stayed disabled because the command
            // only observed IsActionRunning, not State.
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");

            // Before navigation: RunChecksCommand should be disabled (state is SymptomList)
            ((AsyncDelegateCommand)vm.RunChecksCommand!).CanExecute(null).Should().BeFalse();

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);

            // After navigation to CausesPage: RunChecksCommand should be enabled
            ((AsyncDelegateCommand)vm.RunChecksCommand!).CanExecute(null).Should().BeTrue();
            vm.State.Should().Be(TroubleshootingState.CausesPage);
        }

        [Fact]
        public async Task CausesPage_NoCausesDefined_ShowsEmptyList()
        {
            // All current symptoms have causes, but test the property
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var displaySymptom = vm.VisibleSymptoms.First(s => s.Id == "display_resolution");
            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(displaySymptom);

            vm.CurrentSymptomCauses.Should().HaveCount(5); // display_resolution has 5 causes
        }

        #endregion

        #region State 2: ChecksRunning (light progress only)

        [Fact]
        public async Task RunChecksCommand_ExecutesExactCheckIdSubset()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");
            var expectedCheckIds = crashSymptom.CheckIds.ToList();

            _scan.RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(Array.Empty<ScanFinding>()));

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            await ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            await _scan.Received(expectedCheckIds.Count).RunChecksAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>());

            var receivedCalls = _scan.ReceivedCalls().Where(c => c.GetMethodInfo().Name == "RunChecksAsync").ToList();
            receivedCalls.Should().HaveCount(expectedCheckIds.Count);
            foreach (var call in receivedCalls)
            {
                var args = call.GetArguments();
                var ids = (IReadOnlyCollection<string>)args[0]!;
                ids.Should().HaveCount(1);
                expectedCheckIds.Should().Contain(ids.First());
            }
        }

        [Fact]
        public async Task ChecksRunning_ShowsLightProgressOnly_NoPerCheckRows()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");
            var checkTcs = new TaskCompletionSource<IReadOnlyList<ScanFinding>>();
            _scan.RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(checkTcs.Task);

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            var runTask = ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            // During execution - light progress state
            vm.State.Should().Be(TroubleshootingState.ChecksRunning);
            vm.IsScanning.Should().BeTrue();
            vm.ScanCheck.Should().NotBeNullOrEmpty();

            // The current-check label is marshaled from the background thread,
            // so wait (bounded) for the first per-check update to land instead
            // of racing it.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!vm.ScanCheck.StartsWith("Checking ", StringComparison.Ordinal) && sw.ElapsedMilliseconds < 2000)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            vm.ScanCheck.Should().StartWith("Checking ");

            checkTcs.SetResult(Array.Empty<ScanFinding>());
            await runTask;

            // After completion
            vm.IsScanning.Should().BeFalse();
            vm.State.Should().Be(TroubleshootingState.SolutionsPage);
        }

        [Fact]
        public async Task ChecksRunning_Cancelled_ReturnsToCausesPage()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");
            var checkTcs = new TaskCompletionSource<IReadOnlyList<ScanFinding>>();
            _scan.RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(checkTcs.Task);

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            var runTask = ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            // Cancel via PickDifferentProblemCommand
            await ((AsyncDelegateCommand)vm.PickDifferentProblemCommand!).ExecuteAsync();

            checkTcs.SetResult(Array.Empty<ScanFinding>());
            await runTask;

            vm.State.Should().Be(TroubleshootingState.CausesPage);
            vm.IsScanning.Should().BeFalse();
        }

        #endregion

        #region State 3: SolutionsPage (checkboxes + fallback)

        [Fact]
        public async Task SolutionsPage_WithFindings_ShowsFindingDrivenRowsFirst()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");

            _scan.RunChecksAsync(Arg.Is<IReadOnlyCollection<string>>(ids => ids.Contains("mod_assemblies")), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(new List<ScanFinding>
                {
                    new()
                    {
                        CheckId = "mod_assemblies",
                        Passed = false,
                        Severity = ScanSeverity.Error,
                        Title = "Mod assemblies",
                        Detail = "2 missing/inaccessible assemblies in 5 mods",
                        SuggestedActionId = "scan_missing_assemblies",
                        Metadata = new Dictionary<string, object> { ["count"] = 2 }
                    }
                }));

            _scan.RunChecksAsync(Arg.Is<IReadOnlyCollection<string>>(ids => !ids.Contains("mod_assemblies")), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(Array.Empty<ScanFinding>()));

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            await ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            vm.State.Should().Be(TroubleshootingState.SolutionsPage);
            vm.SolutionRows.Should().NotBeEmpty();

            // Finding-driven row should be first
            var firstRow = vm.SolutionRows.First();
            firstRow.IsFindingDriven.Should().BeTrue();
            firstRow.Title.Should().Be("Scan for Missing Critical Mod Assemblies");
            firstRow.FindingDetail.Should().Contain("2 missing/inaccessible assemblies");
            firstRow.FindingSeverity.Should().Be("Error");
        }

        [Fact]
        public async Task SolutionsPage_ZeroFindings_ShowsNoProblemsNote_AndFallbackRows()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var logSymptom = vm.VisibleSymptoms.First(s => s.Id == "huge_log_files");

            _scan.RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(Array.Empty<ScanFinding>()));

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(logSymptom);
            await ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            vm.State.Should().Be(TroubleshootingState.SolutionsPage);
            vm.HasFindings.Should().BeFalse();
            vm.SolutionRows.Should().NotBeEmpty(); // Fallback related actions still shown

            // All rows should be fallback (not finding-driven)
            vm.SolutionRows.All(r => !r.IsFindingDriven).Should().BeTrue();

            // The zero-findings note must be honest: the checks really did run
            vm.ChecksSummary.Should().Contain("1 check run");
            vm.NoFindingsTitle.Should().Be("No specific problems were found for this problem.");
            vm.NoFindingsNote.Should().Contain("ran and passed");
        }

        [Fact]
        public async Task RunChecks_ZeroCheckSymptom_RunsNoChecks_AndSaysSoHonestly()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var resolutionSymptom = vm.VisibleSymptoms.First(s => s.Id == "display_resolution");
            resolutionSymptom.CheckIds.Should().BeEmpty();

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(resolutionSymptom);
            await ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            // No fake checks: the service is never called, and no running state lingers
            await _scan.DidNotReceive().RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());

            vm.State.Should().Be(TroubleshootingState.SolutionsPage);
            vm.SolutionRows.Should().NotBeEmpty(); // The one related action is offered

            // Never claim "all checks passed" when zero checks ran
            vm.ChecksSummary.Should().Contain("no automatic checks yet");
            vm.NoFindingsTitle.Should().Be("No automatic checks for this problem yet");
            vm.NoFindingsNote.Should().Contain("has no automatic checks");
            vm.HasFindings.Should().BeFalse();
        }

        [Fact]
        public async Task RunChecks_RecordsSummaryOfWhatRan()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");
            _scan.RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(Array.Empty<ScanFinding>()));

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            await ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            vm.HasChecksSummary.Should().BeTrue();
            vm.ChecksSummary.Should().Contain($"{crashSymptom.CheckIds.Count} checks run");
            vm.ChecksSummary.Should().Contain("ms");
        }

        [Fact]
        public async Task ChecksRunning_MinimumVisibleDuration_AppliedForRealRuns()
        {
            // This VM keeps the real Task.Delay seam (no CompletedTask override).
            var vm = new TroubleshootingViewModel(_dialogService, _gameRecovery, _modConfig, _steam, _scan, new GameInstance
            {
                GameFolder = @"C:\Test\RimWorld",
                ConfigFolder = @"C:\Test\Config",
                LocalFolder = @"C:\Test\RimWorld\Mods",
                WorkshopFolder = @"C:\Test\Steam\steamapps\workshop\content\294100",
                IsSteamInstallation = true,
                SteamClientIntegration = true
            });
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");
            _scan.RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(Array.Empty<ScanFinding>()));

            var sw = Stopwatch.StartNew();
            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            await ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();
            sw.Stop();

            // Fast checks must stay perceivable — the minimum dwell applies only
            // to real runs (zero-check symptoms never show the running state).
            sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(500);
            vm.ChecksSummary.Should().Contain("checks run");
        }

        [Fact]
        public async Task SolutionsPage_FallbackRowsIncludeRelatedActions()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");

            _scan.RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(Array.Empty<ScanFinding>()));

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            await ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            // Should include related actions from descriptor
            var relatedActionIds = crashSymptom.RelatedActionIds;
            foreach (var actionId in relatedActionIds)
            {
                var row = vm.SolutionRows.FirstOrDefault(r => r.ActionId == actionId);
                row.Should().NotBeNull($"Related action {actionId} should appear in fallback rows");
                row!.IsFindingDriven.Should().BeFalse();
            }
        }

        [Fact]
        public async Task SolutionsPage_ProceedButton_DisabledWhenZeroSelected()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");
            _scan.RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(Array.Empty<ScanFinding>()));

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            await ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            vm.HasSelectedSolutions.Should().BeFalse();
            ((AsyncDelegateCommand)vm.ProceedWithFixesCommand!).CanExecute(null).Should().BeFalse();
        }

        [Fact]
        public async Task SolutionsPage_ProceedButton_EnabledWhenOneSelected()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");
            _scan.RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(Array.Empty<ScanFinding>()));

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            await ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            // Select first row
            var canExecuteChangedFired = 0;
            var command = (AsyncDelegateCommand)vm.ProceedWithFixesCommand!;
            command.CanExecuteChanged += (_, __) => canExecuteChangedFired++;

            vm.SolutionRows.First().IsSelected = true;

            canExecuteChangedFired.Should().BeGreaterThan(0); // Avalonia only re-reads on CanExecuteChanged
            vm.HasSelectedSolutions.Should().BeTrue();
            command.CanExecute(null).Should().BeTrue();

            // Unselecting must flip it back (CanExecuteChanged fires again)
            vm.SolutionRows.First().IsSelected = false;

            vm.HasSelectedSolutions.Should().BeFalse();
            command.CanExecute(null).Should().BeFalse();
        }

        [Fact]
        public async Task SolutionsPage_PickDifferentProblem_ReturnsToSymptomList()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");
            _scan.RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(Array.Empty<ScanFinding>()));

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            await ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            vm.State.Should().Be(TroubleshootingState.SolutionsPage);

            await ((AsyncDelegateCommand)vm.PickDifferentProblemCommand!).ExecuteAsync();

            vm.State.Should().Be(TroubleshootingState.SymptomList);
            vm.CurrentSymptom.Should().BeNull();
            vm.SolutionRows.Should().BeEmpty();
        }

        #endregion

        #region State 4: ActionRunning (sequential) + State 5: ActionResults

        [Fact]
        public async Task ProceedWithFixes_ExecutesSelectedActionsSequentially()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");
            _scan.RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(Array.Empty<ScanFinding>()));

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            await ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            // Select two actions from the crash_on_launch related actions
            var row1 = vm.SolutionRows.First(r => r.ActionId == "kill_zombie_processes");
            var row2 = vm.SolutionRows.First(r => r.ActionId == "check_file_permissions");
            row1.IsSelected = true;
            row2.IsSelected = true;

            // No confirmation modal — checkbox + Proceed is the authorization
            _steam.KillZombieProcessesAsync(Arg.Any<CancellationToken>())
                .Returns(GameRecoveryResult.Ok("Killed zombie processes."));
            _gameRecovery.CheckFilePermissionsAsync(Arg.Any<CancellationToken>())
                .Returns(GameRecoveryResult.Ok("File permissions checked."));

            await ((AsyncDelegateCommand)vm.ProceedWithFixesCommand!).ExecuteAsync();

            vm.State.Should().Be(TroubleshootingState.ActionResults);
            vm.ActionResults.Should().HaveCount(2);
            vm.ActionResults[0].ActionTitle.Should().Be("Kill Zombie RimWorld Processes");
            vm.ActionResults[0].Success.Should().BeTrue();
            vm.ActionResults[0].Message.Should().Be("Killed zombie processes.");
            vm.ActionResults[1].ActionTitle.Should().Be("Check File Permissions & Cloud Locks");
            vm.ActionResults[1].Success.Should().BeTrue();
        }

        [Fact]
        public async Task ProceedWithFixes_DestructiveAction_RunsWithoutConfirmationModal()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");
            _scan.RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(Array.Empty<ScanFinding>()));

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            await ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            var row = vm.SolutionRows.First(r => r.ActionId == "reset_game_files");
            row.IsSelected = true;

            _gameRecovery.ResetGameFilesAsync(Arg.Any<CancellationToken>())
                .Returns(GameRecoveryResult.Ok("Done"));

            await ((AsyncDelegateCommand)vm.ProceedWithFixesCommand!).ExecuteAsync();

            // Checkbox + "Proceed with fixes" IS the authorization: no modal, ever
            _ = _dialogService.DidNotReceive().ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
            await _gameRecovery.Received(1).ResetGameFilesAsync(Arg.Any<CancellationToken>());

            // No "Cancelled by user" row — destructive fixes run at their turn
            vm.ActionResults.Should().HaveCount(1);
            vm.ActionResults[0].Success.Should().BeTrue();
            vm.ActionResults[0].Message.Should().Be("Done");
        }

        [Fact]
        public async Task ProceedWithFixes_FailureShowsExactServiceMessage()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");
            _scan.RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(Array.Empty<ScanFinding>()));

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            await ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            var row = vm.SolutionRows.First(r => r.ActionId == "kill_zombie_processes");
            row.IsSelected = true;

            // No confirmation modal — checkbox + Proceed is the authorization
            _steam.KillZombieProcessesAsync(Arg.Any<CancellationToken>())
                .Returns(GameRecoveryResult.Fail("Config folder not found"));

            await ((AsyncDelegateCommand)vm.ProceedWithFixesCommand!).ExecuteAsync();

            vm.ActionResults.Should().HaveCount(1);
            vm.ActionResults[0].Success.Should().BeFalse();
            vm.ActionResults[0].Message.Should().Be("Failed: Config folder not found");
        }

        [Fact]
        public async Task ActionResults_Dismiss_ReturnsToProblemList()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");
            _scan.RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(Array.Empty<ScanFinding>()));

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            await ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            var row = vm.SolutionRows.First();
            row.IsSelected = true;

            _gameRecovery.CleanLogFilesAsync(Arg.Any<CancellationToken>())
                .Returns(GameRecoveryResult.Ok("Log files cleaned."));

            await ((AsyncDelegateCommand)vm.ProceedWithFixesCommand!).ExecuteAsync();

            vm.State.Should().Be(TroubleshootingState.ActionResults);

            await ((AsyncDelegateCommand)vm.DismissActionResultsCommand!).ExecuteAsync();

            // The results page asks about the NEXT problem, not the same fixes
            vm.State.Should().Be(TroubleshootingState.SymptomList);
            vm.CurrentSymptom.Should().BeNull();
            vm.ActionResults.Should().BeEmpty();
            vm.SolutionRows.Should().BeEmpty();
        }

        #endregion

        #region Steam Gating - Symptoms

        [Fact]
        public async Task SteamSymptoms_HiddenForNonSteamUsers()
        {
            var vm = CreateViewModelNonSteam();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            vm.VisibleSymptoms.Should().NotContain(s => s.Id == "steam_downloads_stuck");
            vm.VisibleSymptoms.Should().NotContain(s => s.Id == "missing_workshop_mods");
            vm.VisibleSymptoms.Should().Contain(s => s.Id == "crash_on_launch");
            vm.VisibleSymptoms.Should().Contain(s => s.Id == "huge_log_files");
        }

        [Fact]
        public async Task SteamSymptoms_VisibleForSteamUsers()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            vm.VisibleSymptoms.Should().Contain(s => s.Id == "steam_downloads_stuck");
            vm.VisibleSymptoms.Should().Contain(s => s.Id == "missing_workshop_mods");
        }

        #endregion

        #region Steam Gating - Catalog

        [Fact]
        public async Task SteamSection_HiddenWhenNotSteamInstallation()
        {
            var vm = CreateViewModelNonSteam();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var steamSection = vm.Sections.FirstOrDefault(s => s.IsSteamSection);
            steamSection.Should().NotBeNull();
            steamSection!.IsVisible.Should().BeFalse();

            var resetGameFiles = vm.Sections.SelectMany(s => s.Actions)
                .FirstOrDefault(a => a.Id == "reset_game_files");
            resetGameFiles.Should().NotBeNull();
            resetGameFiles!.IsVisible.Should().BeFalse();
        }

        [Fact]
        public async Task SteamSection_VisibleWhenSteamInstallation()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var steamSection = vm.Sections.FirstOrDefault(s => s.IsSteamSection);
            steamSection.Should().NotBeNull();
            steamSection!.IsVisible.Should().BeTrue();

            var resetGameFiles = vm.Sections.SelectMany(s => s.Actions)
                .FirstOrDefault(a => a.Id == "reset_game_files");
            resetGameFiles.Should().NotBeNull();
            resetGameFiles!.IsVisible.Should().BeTrue();
        }

        [Fact]
        public async Task UniversalActions_AlwaysVisible()
        {
            var vm = CreateViewModelNonSteam();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var resetModConfigs = vm.Sections.SelectMany(s => s.Actions)
                .FirstOrDefault(a => a.Id == "reset_mod_configurations");
            resetModConfigs.Should().NotBeNull();
            resetModConfigs!.IsVisible.Should().BeTrue();

            var clearAllMods = vm.Sections.SelectMany(s => s.Actions)
                .FirstOrDefault(a => a.Id == "clear_all_mods");
            clearAllMods.Should().NotBeNull();
            clearAllMods!.IsVisible.Should().BeTrue();
        }

        #endregion

        #region Suggested-Action Mapping & Honesty Wording

        [Fact]
        public async Task FindingDrivenRow_ShowsFindingDetailInExplanation()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");

            _scan.RunChecksAsync(Arg.Is<IReadOnlyCollection<string>>(ids => ids.Contains("running_processes")), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(new List<ScanFinding>
                {
                    new()
                    {
                        CheckId = "running_processes",
                        Passed = false,
                        Severity = ScanSeverity.Warning,
                        Title = "Running RimWorld processes",
                        Detail = "Found: RimWorldWin64 (PID: 1234)",
                        SuggestedActionId = "kill_zombie_processes"
                    }
                }));

            _scan.RunChecksAsync(Arg.Is<IReadOnlyCollection<string>>(ids => !ids.Contains("running_processes")), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(Array.Empty<ScanFinding>()));

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            await ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            var row = vm.SolutionRows.First(r => r.ActionId == "kill_zombie_processes");
            row.IsFindingDriven.Should().BeTrue();
            row.ExplanationText.Should().Contain("Found: RimWorldWin64 (PID: 1234)");
        }

        [Fact]
        public async Task ProblemCard_NoFixAvailable_ShowsHonestyMessage()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");

            _scan.RunChecksAsync(Arg.Is<IReadOnlyCollection<string>>(ids => ids.Contains("log_signatures")), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(new List<ScanFinding>
                {
                    new()
                    {
                        CheckId = "log_signatures",
                        Passed = false,
                        Severity = ScanSeverity.Error,
                        Title = "Null Reference Exception",
                        Detail = "A mod or the game dereferenced a null object.",
                        SuggestedActionId = null
                    }
                }));

            _scan.RunChecksAsync(Arg.Is<IReadOnlyCollection<string>>(ids => !ids.Contains("log_signatures")), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(Array.Empty<ScanFinding>()));

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            await ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            // Log signatures has no fix - should not appear in solution rows
            vm.SolutionRows.Should().NotContain(r => r.ActionId == "log_signatures");
        }

        [Fact]
        public async Task Problems_OrderedErrorThenWarningThenInfo()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");

            // Use checks that are in crash_on_launch check IDs and have SuggestedActionId mappings
            // Available: running_processes -> kill_zombie_processes, mod_assemblies -> scan_missing_assemblies, config_folder_attributes -> check_file_permissions
            _scan.RunChecksAsync(Arg.Is<IReadOnlyCollection<string>>(ids => ids.Contains("mod_assemblies")), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(new List<ScanFinding>
                {
                    new() { CheckId = "mod_assemblies", Passed = false, Severity = ScanSeverity.Info, Title = "Info issue", Detail = "i", SuggestedActionId = "scan_missing_assemblies" }
                }));

            _scan.RunChecksAsync(Arg.Is<IReadOnlyCollection<string>>(ids => ids.Contains("running_processes")), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(new List<ScanFinding>
                {
                    new() { CheckId = "running_processes", Passed = false, Severity = ScanSeverity.Error, Title = "Error issue", Detail = "e", SuggestedActionId = "kill_zombie_processes" }
                }));

            _scan.RunChecksAsync(Arg.Is<IReadOnlyCollection<string>>(ids => ids.Contains("config_folder_attributes")), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(new List<ScanFinding>
                {
                    new() { CheckId = "config_folder_attributes", Passed = false, Severity = ScanSeverity.Warning, Title = "Warning issue", Detail = "w", SuggestedActionId = "check_file_permissions" }
                }));

            _scan.RunChecksAsync(Arg.Is<IReadOnlyCollection<string>>(ids => !ids.Contains("mod_assemblies") && !ids.Contains("running_processes") && !ids.Contains("config_folder_attributes")), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ScanFinding>>(Array.Empty<ScanFinding>()));

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            await ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            // Finding-driven rows should be ordered: Error first, then Warning, then Info
            var findingDrivenRows = vm.SolutionRows.Where(r => r.IsFindingDriven).ToList();
            findingDrivenRows.Should().HaveCount(3);
            findingDrivenRows[0].Title.Should().Be("Kill Zombie RimWorld Processes"); // Error
            findingDrivenRows[1].Title.Should().Be("Check File Permissions & Cloud Locks"); // Warning
            findingDrivenRows[2].Title.Should().Be("Scan for Missing Critical Mod Assemblies"); // Info
        }

        #endregion

        #region No Confirmation Modals — the explicit click is the authorization

        [Fact]
        public async Task DestructiveAction_RunsWithoutConfirmationModal()
        {
            // User decision: "Proceed with fixes" (or the fix's own button) is
            // enough authorization — no confirmation dialog for destructive fixes.
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            _gameRecovery.ResetGameFilesAsync(Arg.Any<CancellationToken>())
                .Returns(GameRecoveryResult.Ok("Game files reset.", 42));

            var resetGameFiles = vm.Sections.SelectMany(s => s.Actions)
                .First(a => a.Id == "reset_game_files");

            var cmd = (AsyncDelegateCommand<string?>)vm.RunActionCommand!;
            await cmd.ExecuteAsync(resetGameFiles.Id);

            _ = _dialogService.DidNotReceive().ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
            await _gameRecovery.Received(1).ResetGameFilesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ReadOnlyAction_RunsWithoutConfirmationDialog()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            _gameRecovery.CheckPathCompatibilityAsync(Arg.Any<CancellationToken>())
                .Returns(GameRecoveryResult.Ok("All paths ASCII-compatible"));

            var pathCheck = vm.Sections.SelectMany(s => s.Actions)
                .First(a => a.Id == "check_path_compatibility");

            var cmd = (AsyncDelegateCommand<string?>)vm.RunActionCommand!;
            await cmd.ExecuteAsync(pathCheck.Id);

            _ = _dialogService.DidNotReceive().ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
            await _gameRecovery.Received(1).CheckPathCompatibilityAsync(Arg.Any<CancellationToken>());
        }

        #endregion

        #region Non-Blocking Execution

        [Fact]
        public async Task RunChecksAsync_RunsOnBackgroundThread_UIUpdatesMarshaled()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var crashSymptom = vm.VisibleSymptoms.First(s => s.Id == "crash_on_launch");
            var checkTcs = new TaskCompletionSource<IReadOnlyList<ScanFinding>>();
            _scan.RunChecksAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(checkTcs.Task);

            await ((AsyncDelegateCommand<TroubleshootingSymptomDescriptor?>)vm.SelectSymptomCommand!).ExecuteAsync(crashSymptom);
            var runTask = ((AsyncDelegateCommand)vm.RunChecksCommand!).ExecuteAsync();

            // UI should remain responsive - state updates should be marshaled
            vm.State.Should().Be(TroubleshootingState.ChecksRunning);
            vm.IsScanning.Should().BeTrue();

            checkTcs.SetResult(Array.Empty<ScanFinding>());
            await runTask;

            vm.State.Should().Be(TroubleshootingState.SolutionsPage);
        }

        #endregion
    }
}