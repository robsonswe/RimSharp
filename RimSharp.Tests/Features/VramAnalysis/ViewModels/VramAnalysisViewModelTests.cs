using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.AppDir.Dialogs;
using RimSharp.Core.Converters.Utility;
using RimSharp.Core.Services;
using RimSharp.Features.VramAnalysis.ViewModels;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Features.VramAnalysis.ViewModels
{
    public class VramAnalysisViewModelTests
    {
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService;
        private readonly ILoggerService _logger;
        private readonly IPathService _pathService;
        private readonly ISystemInfoService _systemInfoService;

        public VramAnalysisViewModelTests()
        {
            _modListManager = Substitute.For<IModListManager>();
            _dialogService = Substitute.For<IDialogService>();
            _logger = Substitute.For<ILoggerService>();
            _pathService = Substitute.For<IPathService>();
            _systemInfoService = Substitute.For<ISystemInfoService>();
        }

        private VramAnalysisViewModel CreateViewModel()
        {
            return new VramAnalysisViewModel(_modListManager, _dialogService, _logger, _pathService, _systemInfoService);
        }

        [Fact]
        public async Task Constructor_ShouldLoadMods_InBackground()
        {

            var mods = new List<ModItem>
            {
                new ModItem { Name = "Mod A", ModType = ModType.Workshop, Textures = true },
                new ModItem { Name = "Mod B", ModType = ModType.Git, Textures = true }
            };
            _modListManager.GetAllMods().Returns(mods);

            var vm = CreateViewModel();

            bool loaded = await Task.Run(() => SpinWait.SpinUntil(() => vm.VramMods.Count == 2, 5000));
            loaded.Should().BeTrue("Mods should be loaded within timeout");

            vm.VramMods.Should().HaveCount(2);
            vm.VramMods.Select(m => m.Mod.Name).Should().Contain(new[] { "Mod A", "Mod B" });
        }

        [Fact]
        public async Task CalculateVramCommand_ShouldShowProgressDialog_AndExecute()
        {

            var mods = new List<ModItem>
            {
                new ModItem { Name = "Mod A", ModType = ModType.Workshop, Textures = true, Path = @"C:\Mods\ModA", PackageId = "ModA" }
            };
            _modListManager.GetAllMods().Returns(mods);
            _modListManager.VirtualActiveMods.Returns(new List<(ModItem, int)>());

            var vm = CreateViewModel();
            await Task.Run(() => SpinWait.SpinUntil(() => vm.VramMods.Count == 1, 2000));

            if (vm.CalculateVramCommand.CanExecute(null))
            {
                await vm.CalculateVramCommand.ExecuteAsync(null);
            }

            _dialogService.Received().ShowProgressDialog(Arg.Any<string>(), Arg.Any<string>(), true, false, Arg.Any<CancellationTokenSource>());
        }

        [Fact]
        public async Task ToggleShowMaxVram_ShouldNotReorder_ButUpdateVisibility()
        {

            var mods = new List<ModItem>
            {
                new ModItem { Name = "Mod A", ModType = ModType.Workshop, Textures = true }
            };
            _modListManager.GetAllMods().Returns(mods);
            var vm = CreateViewModel();
            await Task.Run(() => SpinWait.SpinUntil(() => vm.VramMods.Count == 1, 2000));

            var initialCollection = vm.VramMods;
            vm.ShowMaxVram = !vm.ShowMaxVram;

            vm.ShowMaxVram.Should().BeTrue();

            vm.VramMods.Should().BeSameAs(initialCollection);
        }

        [Fact]
        public async Task SortCommand_ShouldSortList()
        {

            var mods = new List<ModItem>
            {
                new ModItem { Name = "Z Mod", ModType = ModType.Workshop, Textures = true },
                new ModItem { Name = "A Mod", ModType = ModType.Workshop, Textures = true }
            };
            _modListManager.GetAllMods().Returns(mods);
            var vm = CreateViewModel();
            bool loaded = await Task.Run(() => SpinWait.SpinUntil(() => vm.VramMods.Count == 2, 5000));
            loaded.Should().BeTrue("Mods should be loaded within timeout");

            // Initial State: Ascending ("A Mod", "Z Mod")
            vm.VramMods.First().Mod.Name.Should().Be("A Mod");

            vm.SortCommand.Execute("Mod.Name"); 

            // Assert - Descending ("Z Mod", "A Mod")
            vm.VramMods.First().Mod.Name.Should().Be("Z Mod");

            vm.SortCommand.Execute("Mod.Name");

            // Assert - Ascending ("A Mod", "Z Mod")
            vm.VramMods.First().Mod.Name.Should().Be("A Mod");
        }

        [Fact]
        public async Task Filter_ShouldShowOnlyActiveMods()
        {

            var mods = new List<ModItem>
            {
                new ModItem { Name = "Mod Active", ModType = ModType.Workshop, Textures = true, IsActive = true },
                new ModItem { Name = "Mod Inactive", ModType = ModType.Workshop, Textures = true, IsActive = false }
            };
            _modListManager.GetAllMods().Returns(mods);
            var vm = CreateViewModel();
            await Task.Run(() => SpinWait.SpinUntil(() => vm.VramMods.Count == 2, 2000));

            vm.ShowOnlyActive = true;

            vm.VramMods.Should().HaveCount(1);
            vm.VramMods.First().Mod.Name.Should().Be("Mod Active");
        }

        [Fact]
        public async Task Calculate_ShouldIncludeGameBaseline_InTotals()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "RimSharpVmTest_" + Guid.NewGuid());
            var gameRoot = Path.Combine(tempRoot, "Game");
            var tmpCore = Path.Combine(gameRoot, "Data", "Core");
            var tmpDlc = Path.Combine(gameRoot, "Data", "Royalty");
            var tmpRegular = Path.Combine(tempRoot, "Regular");
            try
            {
                Directory.CreateDirectory(tmpCore);
                Directory.CreateDirectory(tmpDlc);
                CreateAssetsZip(Path.Combine(gameRoot, "Assets.zip"), new[] { ("core.dds", 4096) });
                CreateAssetsZip(Path.Combine(tmpDlc, "Assets.zip"), new[] { ("royalty.dds", 8192) });
                CreateTextureModDirectory(tmpRegular);

                var coreMod = new ModItem { Name = "Core", ModType = ModType.Core, Path = tmpCore, IsActive = true, PackageId = "ludeon.rimworld" };
                var inactiveDlc = new ModItem { Name = "Royalty", ModType = ModType.Expansion, Path = tmpDlc, IsActive = false, PackageId = "ludeon.rimworld.royalty" };
                var regularMod = new ModItem { Name = "Regular", ModType = ModType.Workshop, Textures = true, Path = tmpRegular, IsActive = true, PackageId = "regular" };

                _modListManager.GetAllMods().Returns(new List<ModItem> { coreMod, inactiveDlc, regularMod });
                _modListManager.VirtualActiveMods.Returns(new List<(ModItem, int)> { (regularMod, 1) });
                _pathService.GetMajorGameVersion().Returns("1.5");

                var vm = CreateViewModel();
                var loaded = await Task.Run(() => SpinWait.SpinUntil(() => vm.VramMods.Count == 1, 5000));
                loaded.Should().BeTrue("The regular mod should be loaded within timeout");
                var baselineDone = await Task.Run(() => SpinWait.SpinUntil(() => vm.CoreVramCompressed > 0, 5000));
                baselineDone.Should().BeTrue("The game baseline should be computed automatically within timeout");

                if (vm.CalculateVramCommand.CanExecute(null))
                {
                    await vm.CalculateVramCommand.ExecuteAsync(null);
                }

                var regularWrapper = vm.VramMods.Single(m => m.Mod.PackageId == "regular");

                vm.CoreVramCompressed.Should().Be(4096);
                vm.CoreTextureCount.Should().Be(1);
                vm.ExpansionsVramCompressed.Should().Be(0);
                vm.ExpansionsTextureCount.Should().Be(0);
                vm.TotalVramWithGameCompressed.Should().Be(4096 + regularWrapper.EstimatedVramCompressed);
            }
            finally
            {
                CleanupDirectory(tempRoot);
            }
        }

        [Fact]
        public async Task Calculate_ShouldIncludeActiveExpansion_InTotals()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "RimSharpVmTest_" + Guid.NewGuid());
            var gameRoot = Path.Combine(tempRoot, "Game");
            var tmpCore = Path.Combine(gameRoot, "Data", "Core");
            var tmpDlc = Path.Combine(gameRoot, "Data", "Royalty");
            var tmpRegular = Path.Combine(tempRoot, "Regular");
            try
            {
                Directory.CreateDirectory(tmpCore);
                Directory.CreateDirectory(tmpDlc);
                CreateAssetsZip(Path.Combine(gameRoot, "Assets.zip"), new[] { ("core.dds", 4096) });
                CreateAssetsZip(Path.Combine(tmpDlc, "Assets.zip"), new[] { ("royalty.dds", 8192) });
                CreateTextureModDirectory(tmpRegular);

                var coreMod = new ModItem { Name = "Core", ModType = ModType.Core, Path = tmpCore, IsActive = true, PackageId = "ludeon.rimworld" };
                var activeDlc = new ModItem { Name = "Royalty", ModType = ModType.Expansion, Path = tmpDlc, IsActive = true, PackageId = "ludeon.rimworld.royalty" };
                var regularMod = new ModItem { Name = "Regular", ModType = ModType.Workshop, Textures = true, Path = tmpRegular, IsActive = true, PackageId = "regular" };

                _modListManager.GetAllMods().Returns(new List<ModItem> { coreMod, activeDlc, regularMod });
                _modListManager.VirtualActiveMods.Returns(new List<(ModItem, int)> { (regularMod, 1), (activeDlc, 2) });
                _pathService.GetMajorGameVersion().Returns("1.5");

                var vm = CreateViewModel();
                var loaded = await Task.Run(() => SpinWait.SpinUntil(() => vm.VramMods.Count == 1, 5000));
                loaded.Should().BeTrue("The regular mod should be loaded within timeout");
                var baselineDone = await Task.Run(() => SpinWait.SpinUntil(() => vm.CoreVramCompressed > 0, 5000));
                baselineDone.Should().BeTrue("The game baseline should be computed automatically within timeout");

                if (vm.CalculateVramCommand.CanExecute(null))
                {
                    await vm.CalculateVramCommand.ExecuteAsync(null);
                }

                vm.ExpansionsVramCompressed.Should().Be(8192);
                vm.ExpansionsTextureCount.Should().Be(1);
            }
            finally
            {
                CleanupDirectory(tempRoot);
            }
        }

        [Fact]
        public async Task Calculate_ShouldFlagOverBudget_WhenTotalExceedsUserVram()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "RimSharpVmTest_" + Guid.NewGuid());
            var gameRoot = Path.Combine(tempRoot, "Game");
            var tmpCore = Path.Combine(gameRoot, "Data", "Core");
            var tmpRegular = Path.Combine(tempRoot, "Regular");
            try
            {
                Directory.CreateDirectory(tmpCore);
                CreateAssetsZip(Path.Combine(gameRoot, "Assets.zip"), new[] { ("core.dds", 4096) });
                CreateTextureModDirectory(tmpRegular);

                var coreMod = new ModItem { Name = "Core", ModType = ModType.Core, Path = tmpCore, IsActive = true, PackageId = "ludeon.rimworld" };
                var regularMod = new ModItem { Name = "Regular", ModType = ModType.Workshop, Textures = true, Path = tmpRegular, IsActive = true, PackageId = "regular" };

                _modListManager.GetAllMods().Returns(new List<ModItem> { coreMod, regularMod });
                _modListManager.VirtualActiveMods.Returns(new List<(ModItem, int)> { (regularMod, 1) });
                _pathService.GetMajorGameVersion().Returns("1.5");
                _systemInfoService.GetPrimaryGpuVram().Returns(1L);

                var vm = CreateViewModel();
                var loaded = await Task.Run(() => SpinWait.SpinUntil(() => vm.VramMods.Count == 1, 5000));
                loaded.Should().BeTrue("The regular mod should be loaded within timeout");
                var baselineDone = await Task.Run(() => SpinWait.SpinUntil(() => vm.CoreVramCompressed > 0, 5000));
                baselineDone.Should().BeTrue("The game baseline should be computed automatically within timeout");
                await Task.Run(() => SpinWait.SpinUntil(() => vm.IsUserVramAvailable, 5000));

                if (vm.CalculateVramCommand.CanExecute(null))
                {
                    await vm.CalculateVramCommand.ExecuteAsync(null);
                }

                vm.IsOverBudget.Should().BeTrue();
            }
            finally
            {
                CleanupDirectory(tempRoot);
            }
        }

        [Fact]
        public async Task Baseline_ShouldComputeAutomatically_OnConstruction()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "RimSharpVmTest_" + Guid.NewGuid());
            var gameRoot = Path.Combine(tempRoot, "Game");
            var tmpCore = Path.Combine(gameRoot, "Data", "Core");
            try
            {
                Directory.CreateDirectory(tmpCore);
                CreateAssetsZip(Path.Combine(gameRoot, "Assets.zip"), new[] { ("core.dds", 4096) });

                var coreMod = new ModItem { Name = "Core", ModType = ModType.Core, Path = tmpCore, IsActive = true, PackageId = "ludeon.rimworld" };

                _modListManager.GetAllMods().Returns(new List<ModItem> { coreMod });
                _modListManager.VirtualActiveMods.Returns(new List<(ModItem, int)>());
                _pathService.GetMajorGameVersion().Returns("1.5");

                var vm = CreateViewModel();

                var baselineDone = await Task.Run(() => SpinWait.SpinUntil(() => vm.CoreVramCompressed > 0, 5000));
                baselineDone.Should().BeTrue("The game baseline should be computed automatically on construction");

                vm.CoreVramCompressed.Should().Be(4096);
                vm.CoreVramUncompressed.Should().NotBeNull();
                vm.CoreTextureCount.Should().NotBeNull();
                vm.CoreInAtlasCount.Should().NotBeNull();
                vm.ExpansionsVramCompressed.Should().NotBeNull();
                vm.TotalVramCompressedActiveMods.Should().BeNull("mods have not been calculated yet");
                vm.DisplayedModsVram.Should().BeNull("mods have not been calculated yet");
                vm.TotalVramWithGameCompressed.Should().Be(vm.CoreVramCompressed);
            }
            finally
            {
                CleanupDirectory(tempRoot);
            }
        }

        [Fact]
        public async Task Calculate_WithoutTextureContent_ShouldSetModsTotalsToZero()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "RimSharpVmTest_" + Guid.NewGuid());
            var gameRoot = Path.Combine(tempRoot, "Game");
            var tmpCore = Path.Combine(gameRoot, "Data", "Core");
            var tmpRegular = Path.Combine(tempRoot, "Regular");
            try
            {
                Directory.CreateDirectory(tmpCore);
                Directory.CreateDirectory(tmpRegular);
                CreateAssetsZip(Path.Combine(gameRoot, "Assets.zip"), new[] { ("core.dds", 4096) });

                var coreMod = new ModItem { Name = "Core", ModType = ModType.Core, Path = tmpCore, IsActive = true, PackageId = "ludeon.rimworld" };
                var regularMod = new ModItem { Name = "Regular", ModType = ModType.Workshop, Textures = true, Path = tmpRegular, IsActive = true, PackageId = "regular" };

                _modListManager.GetAllMods().Returns(new List<ModItem> { coreMod, regularMod });
                _modListManager.VirtualActiveMods.Returns(new List<(ModItem, int)> { (regularMod, 1) });
                _pathService.GetMajorGameVersion().Returns("1.5");

                var vm = CreateViewModel();
                var loaded = await Task.Run(() => SpinWait.SpinUntil(() => vm.VramMods.Count == 1, 5000));
                loaded.Should().BeTrue("The regular mod should be loaded within timeout");
                var baselineDone = await Task.Run(() => SpinWait.SpinUntil(() => vm.CoreVramCompressed > 0, 5000));
                baselineDone.Should().BeTrue("The game baseline should be computed automatically within timeout");

                vm.TotalVramCompressedActiveMods.Should().BeNull("mods have not been calculated yet");

                if (vm.CalculateVramCommand.CanExecute(null))
                {
                    await vm.CalculateVramCommand.ExecuteAsync(null);
                }

                vm.TotalVramCompressedActiveMods.Should().Be(0, "the calculation ran and produced a zero total");
                vm.TotalTextureCountActiveMods.Should().Be(0);
                new FileSizeConverter().Convert(vm.DisplayedModsVram, typeof(string), null, CultureInfo.InvariantCulture).Should().Be("0 Bytes");
            }
            finally
            {
                CleanupDirectory(tempRoot);
            }
        }

        [Fact]
        public async Task BudgetUsagePercentText_ShouldBeDash_WhenVramUnknown()
        {
            var mods = new List<ModItem>
            {
                new ModItem { Name = "Mod A", ModType = ModType.Workshop, Textures = true }
            };
            _modListManager.GetAllMods().Returns(mods);

            var vm = CreateViewModel();
            await Task.Run(() => SpinWait.SpinUntil(() => vm.VramMods.Count == 1, 5000));

            vm.IsUserVramAvailable.Should().BeFalse("no VRAM size was detected");
            vm.BudgetUsagePercentText.Should().Be("–");
        }

        private static void CreateAssetsZip(string zipPath, IEnumerable<(string Name, int Size)> entries)
        {
            using var fs = File.Create(zipPath);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
            foreach (var (name, size) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                entryStream.Write(new byte[size], 0, size);
            }
        }

        private static void CreateTextureModDirectory(string modPath)
        {
            var texturesDirectory = Path.Combine(modPath, "Textures");
            Directory.CreateDirectory(texturesDirectory);
            CreatePngFile(Path.Combine(texturesDirectory, "texture.png"), 256, 256);
        }

        private static void CreatePngFile(string path, int width, int height)
        {
            using var fs = File.Create(path);
            using var writer = new BinaryWriter(fs);
            writer.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            writer.Write(ReverseBytes(13));
            writer.Write(Encoding.ASCII.GetBytes("IHDR"));
            writer.Write(ReverseBytes(width));
            writer.Write(ReverseBytes(height));
            writer.Write(new byte[] { 8, 6, 0, 0, 0 });
            writer.Write(new byte[] { 0, 0, 0, 0 });
            writer.Write(ReverseBytes(0));
            writer.Write(Encoding.ASCII.GetBytes("IDAT"));
            writer.Write(new byte[] { 0, 0, 0, 0 });
            writer.Write(ReverseBytes(0));
            writer.Write(Encoding.ASCII.GetBytes("IEND"));
            writer.Write(new byte[] { 0, 0, 0, 0 });
        }

        private static int ReverseBytes(int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }

        private static void CleanupDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                try { Directory.Delete(path, true); } catch { }
            }
        }
    }
}


