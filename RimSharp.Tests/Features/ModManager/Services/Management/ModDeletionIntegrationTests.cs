using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using FluentAssertions;
using NSubstitute;
using RimSharp.AppDir.Dialogs;
using RimSharp.Core.Commands.Base;
using RimSharp.Features.ModManager.Services.Management;
using RimSharp.Features.ModManager.ViewModels.Actions;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Infrastructure.Mods.IO;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Services.Management
{
    public class ModDeletionIntegrationTests : IDisposable
    {
        private readonly string _testTempDir;
        private readonly IModDataService _mockDataService;
        private readonly IModListManager _modListManager;
        private readonly IModDeletionService _deletionService;
        private readonly IDialogService _mockDialogService;
        private readonly ModActionsViewModel _viewModel;

        public ModDeletionIntegrationTests()
        {
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_DeletionIntegration_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);

            _mockDataService = Substitute.For<IModDataService>();
            _mockDialogService = Substitute.For<IDialogService>();
            _deletionService = new ModDeletionService(); // Use actual robust deletion service
            
            // We'll use a real ModListManager to test the memory state transition
            var mockDict = Substitute.For<IModDictionaryService>();
            _modListManager = new ModListManager(mockDict);

            _viewModel = new ModActionsViewModel(
                _mockDataService,
                Substitute.For<IModCommandService>(),
                Substitute.For<IModListIOService>(),
                _modListManager,
                Substitute.For<IModIncompatibilityService>(),
                Substitute.For<IModDuplicateService>(),
                _deletionService,
                _mockDialogService,
                Substitute.For<IPathService>(),
                Substitute.For<IModService>(),
                Substitute.For<IModReplacementService>(),
                Substitute.For<IDownloadQueueService>(),
                Substitute.For<ISteamApiClient>(),
                Substitute.For<IApplicationNavigationService>(),
                Substitute.For<ISteamWorkshopQueueProcessor>(),
                Substitute.For<IGitService>()
            );
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                new ModDeletionService().DeleteDirectoryRobustAsync(_testTempDir).Wait();
            }
        }

        [Fact]
        public async Task DeleteMod_WithDuplicates_ShouldOnlyDeleteTargetMod_AndNOTSaveConfig()
        {
            // --- Arrange ---
            var path1 = Path.Combine(_testTempDir, "ModV1");
            var path2 = Path.Combine(_testTempDir, "ModV2");
            Directory.CreateDirectory(path1);
            Directory.CreateDirectory(path2);
            File.WriteAllText(Path.Combine(path1, "About.xml"), "v1 content");
            File.WriteAllText(Path.Combine(path2, "About.xml"), "v2 content");

            var mod1 = new ModItem { Name = "My Mod (v1)", PackageId = "my.mod", Path = path1, ModType = ModType.WorkshopL };
            var mod2 = new ModItem { Name = "My Mod (v2)", PackageId = "my.mod", Path = path2, ModType = ModType.WorkshopL };

            _modListManager.Initialize(new[] { mod1, mod2 }, new[] { "my.mod" });

            // Ensure mod1 is active (it's first in init)
            // If mod2 got activated by default logic, switch to mod1 for the test
            if (_modListManager.IsModActive(mod2))
            {
                _modListManager.DeactivateMod(mod2);
                _modListManager.ActivateMod(mod1);
            }
            
            _modListManager.IsModActive(mod1).Should().BeTrue();
            _modListManager.IsModActive(mod2).Should().BeFalse();

            // Mock confirmation to return Yes
            _mockDialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
                .Returns(Task.FromResult(MessageDialogResult.Yes));

            // --- Act ---
            // Delete mod1 (the active one)
            await ((AsyncDelegateCommand<ModItem>)_viewModel.DeleteModCommand).ExecuteAsync(mod1);

            // --- Assert ---
            // 1. Check File System
            Directory.Exists(path1).Should().BeFalse("Mod V1 directory should be deleted");
            Directory.Exists(path2).Should().BeTrue("Mod V2 directory should NOT be deleted");

            // 2. Check ModListManager State (Memory)
            var allMods = _modListManager.GetAllMods().ToList();
            allMods.Should().NotContain(mod1, "Mod V1 should be removed from all lists");
            allMods.Should().Contain(mod2, "Mod V2 should still exist in memory");

            // 3. Check Active List Integrity (Handover)
            _modListManager.IsModActive(mod2).Should().BeTrue("The duplicate mod (v2) should have automatically taken over as active");
            
            // 4. Verify data was NOT saved 
            // Reason: The game loads mods by PackageId. Since we swapped to a duplicate with the same PackageId,
            // the config file (which stores IDs) is still valid and points to the remaining mod implicitly.
            // Saving is unnecessary and avoided to improve UX/Performance.
            _mockDataService.DidNotReceive().SaveActiveModIdsToConfig(Arg.Any<IEnumerable<string>>());
        }
    }
}
