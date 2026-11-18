using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.AppDir.Dialogs;
using RimSharp.Features.ModManager.Dialogs.MissingMods;
using RimSharp.Infrastructure.Mods.IO;
using RimSharp.Infrastructure.Mods.ModInfo;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Features.WorkshopDownloader.Services;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Mods.IO
{
    public class ModListIOServiceTests : IDisposable
    {
        private readonly IPathService _mockPathService;
        private readonly IModListManager _mockModListManager;
        private readonly IDialogService _mockDialogService;
        private readonly IModListFileParser _mockFileParser;
        private readonly IModDictionaryService _mockModDictionaryService;
        private readonly ISteamApiClient _mockSteamApiClient;
        private readonly IDownloadQueueService _mockDownloadQueueService;
        private readonly IApplicationNavigationService _mockNavigationService;
        private readonly ILoggerService _mockLogger;
        private readonly ISteamWorkshopQueueProcessor _mockSteamWorkshopQueueProcessor;
        private readonly IModInfoResolver _mockModInfoResolver;
        private readonly string _testTempDir;

        public ModListIOServiceTests()
        {
            _mockPathService = Substitute.For<IPathService>();
            _mockModListManager = Substitute.For<IModListManager>();
            _mockDialogService = Substitute.For<IDialogService>();
            _mockFileParser = Substitute.For<IModListFileParser>();
            _mockModDictionaryService = Substitute.For<IModDictionaryService>();
            _mockSteamApiClient = Substitute.For<ISteamApiClient>();
            _mockDownloadQueueService = Substitute.For<IDownloadQueueService>();
            _mockNavigationService = Substitute.For<IApplicationNavigationService>();
            _mockLogger = Substitute.For<ILoggerService>();
            _mockSteamWorkshopQueueProcessor = Substitute.For<ISteamWorkshopQueueProcessor>();
            _mockModInfoResolver = Substitute.For<IModInfoResolver>();

            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharp_IO_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        private ModListIOService CreateService(IModInfoResolver? resolver = null)
        {
            return new ModListIOService(
                _mockPathService,
                _mockModListManager,
                _mockDialogService,
                _mockFileParser,
                _mockModDictionaryService,
                _mockSteamApiClient,
                _mockDownloadQueueService,
                _mockNavigationService,
                _mockLogger,
                _mockSteamWorkshopQueueProcessor,
                resolver
            );
        }

        /// <summary>
        /// Wires the confirmation + progress + picker dialogs so the import
        /// reaches HandleMissingModDownloadRequestAsync. Returns a getter for
        /// the picker VM the service built, so tests can assert its contents.
        /// </summary>
        private Func<MissingModSelectionDialogViewModel?> SetupMissingModDownloadFlow(
            MissingModSelectionResult result = MissingModSelectionResult.Cancel)
        {
            _mockDialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
                .Returns(MessageDialogResult.OK);

            _mockDialogService.ShowProgressDialog(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(),
                    Arg.Any<CancellationTokenSource>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(_ => new ProgressDialogViewModel("Preparing Download Options", "Loading mod dictionary..."));

            MissingModSelectionDialogViewModel? captured = null;
            _mockDialogService.ShowMissingModSelectionDialogAsync(Arg.Any<MissingModSelectionDialogViewModel>())
                .Returns(callInfo =>
                {
                    captured = callInfo.Arg<MissingModSelectionDialogViewModel>();
                    return Task.FromResult(new MissingModSelectionDialogOutput
                    {
                        Result = result,
                        SelectedSteamIds = new List<string>()
                    });
                });

            return () => captured;
        }

        private static ModDictionaryEntry Entry(
            string packageId, string steamId, string name, bool published = true)
        {
            return new ModDictionaryEntry
            {
                PackageId = packageId,
                SteamId = steamId,
                Name = name,
                Published = published
            };
        }

        [Fact]
        public async Task ExportModListAsync_WhenSuccessful_ShouldSaveFile()
        {

            var service = CreateService();
            var activeMods = new List<ModItem> { new ModItem { PackageId = "m1" } };
            string savePath = Path.Combine(_testTempDir, "Exported.xml");
            
            _mockDialogService.ShowSaveFileDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromResult((true, (string?)savePath)));

            var doc = new XDocument(new XElement("ModList", new XElement("m1")));
            _mockFileParser.Generate(Arg.Any<IEnumerable<string>>()).Returns(doc);

            await service.ExportModListAsync(activeMods);

            File.Exists(savePath).Should().BeTrue();
            await _mockDialogService.Received(1).ShowInformation(Arg.Is<string>(s => s.Contains("Export Successful")), Arg.Any<string>());
        }

        [Fact]
        public async Task ImportModListAsync_WhenSuccessful_ShouldInitializeManager()
        {

            var service = CreateService();
            string importPath = Path.Combine(_testTempDir, "Import.xml");
            File.WriteAllText(importPath, "<xml/>");

            _mockDialogService.ShowOpenFileDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromResult((true, (string?)importPath)));

            var importedIds = new List<string> { "m1" };
            _mockFileParser.Parse(Arg.Any<XDocument>()).Returns(importedIds);

            var availableMods = new List<ModItem> { new ModItem { PackageId = "m1" } };
            _mockModListManager.GetAllMods().Returns(availableMods);

            await service.ImportModListAsync();

            _mockModListManager.Received(1).Initialize(
                Arg.Any<IEnumerable<ModItem>>(),
                Arg.Is<IEnumerable<string>>(en => en.Contains("m1")),
                Arg.Any<bool>());
        }

        [Fact]
        public async Task ImportModListAsync_WithAKnownList_AppliesItWithoutAFileDialog()
        {
            var service = CreateService();

            var availableMods = new List<ModItem> { new ModItem { PackageId = "m1" }, new ModItem { PackageId = "m2" } };
            _mockModListManager.GetAllMods().Returns(availableMods);

            await service.ImportModListAsync(new List<string> { "m1", "m3" }, "Colony");

            // No picker: the list is already known.
            await _mockDialogService.DidNotReceive().ShowOpenFileDialogAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());

            // The installed mods are applied, in the order they were given.
            _mockModListManager.Received(1).Initialize(
                Arg.Any<IEnumerable<ModItem>>(),
                Arg.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "m1" })),
                // The list came from outside the app - it is a change the user
                // still has to save.
                Arg.Is<bool>(activeListModified => activeListModified));

            // The mod that is not installed is offered for download, not
            // dropped silently - the same offer the file import makes.
            await _mockDialogService.Received(1).ShowConfirmationAsync(
                Arg.Is<string>(title => title.Contains("Import Partially Successful")),
                Arg.Is<string>(message => message.Contains("m3")),
                Arg.Any<bool>());
        }

        [Fact]
        public async Task ImportModListAsync_WithAKnownList_ReportsTheSuccess_WhenNothingIsMissing()
        {
            var service = CreateService();

            var availableMods = new List<ModItem> { new ModItem { PackageId = "m1" } };
            _mockModListManager.GetAllMods().Returns(availableMods);

            await service.ImportModListAsync(new List<string> { "m1" }, "Colony");

            await _mockDialogService.Received(1).ShowInformation(
                Arg.Is<string>(title => title.Contains("Import Successful")),
                Arg.Is<string>(message => message.Contains("Colony") && message.Contains("1 active mods")));

            // Nothing is missing, so nothing is offered for download.
            await _mockDialogService.DidNotReceive().ShowConfirmationAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        }

        [Fact]
        public async Task ImportModListAsync_WithMissingMods_ReportsWhatStayedOut_WhenTheDownloadIsDeclined()
        {
            var service = CreateService();

            var availableMods = new List<ModItem> { new ModItem { PackageId = "m1" } };
            _mockModListManager.GetAllMods().Returns(availableMods);
            _mockDialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
                .Returns(MessageDialogResult.Cancel);

            await service.ImportModListAsync(new List<string> { "m1", "m3" }, "Colony");

            // The decline must not be dressed up as a success: the applied
            // list is incomplete and the message says exactly by how much.
            await _mockDialogService.Received(1).ShowInformation(
                Arg.Is<string>(title => title.Contains("Imported with missing mods")),
                Arg.Is<string>(message => message.Contains("1 of the 2 mods of Colony") && message.Contains("stay inactive")));

            // No false success on top of it.
            await _mockDialogService.DidNotReceive().ShowInformation(
                Arg.Is<string>(title => title.Contains("Import Successful")),
                Arg.Any<string>());
        }

        [Fact]
        public async Task ImportModListAsync_WithAnEmptyList_DoesNothing()
        {
            var service = CreateService();

            await service.ImportModListAsync(Array.Empty<string>(), "Colony");

            _mockModListManager.DidNotReceive().Initialize(
                Arg.Any<IEnumerable<ModItem>>(), Arg.Any<IEnumerable<string>>(), Arg.Any<bool>());
            await _mockDialogService.DidNotReceive().ShowInformation(Arg.Any<string>(), Arg.Any<string>());
        }

        [Fact]
        public async Task MissingModDownload_ResolvesEachIdViaReverseIndex_WithoutFullTableScan()
        {
            var service = CreateService();
            var getPicker = SetupMissingModDownloadFlow();

            _mockModListManager.GetAllMods().Returns(new List<ModItem>());

            // Two dictionary candidates for mod.a (one published, one not) and
            // none for mod.b. The old code would have scanned the whole table
            // for each id; the new code looks each id up directly.
            _mockModDictionaryService.GetAllEntriesByPackageId("mod.a").Returns(new List<ModDictionaryEntry>
            {
                Entry("mod.a", "101", "Zeta", published: true),
                Entry("mod.a", "102", "Alpha", published: false)
            });
            _mockModDictionaryService.GetAllEntriesByPackageId("mod.b")
                .Returns(new List<ModDictionaryEntry>());

            await service.ImportModListAsync(new List<string> { "mod.a", "mod.b" }, "Colony");

            // Per-id reverse-index lookups, never the full-table scan.
            _mockModDictionaryService.Received(1).GetAllEntriesByPackageId("mod.a");
            _mockModDictionaryService.Received(1).GetAllEntriesByPackageId("mod.b");
            _mockModDictionaryService.DidNotReceive().GetAllEntries();

            var picker = getPicker();
            picker.Should().NotBeNull();
            picker!.ModGroups.Should().ContainSingle();
            var group = picker.ModGroups.Single();
            group.PackageId.Should().Be("mod.a");
            // Variants ordered by dictionary Name, default = first published.
            group.Variants.Select(v => v.Name).Should().Equal("Alpha", "Zeta");
            group.SelectedVariant.Should().NotBeNull();
            group.SelectedVariant!.SteamId.Should().Be("101");
            group.SelectedVariant.IsPublished.Should().BeTrue();

            picker.UnknownModIds.Should().ContainSingle().Which.Should().Be("mod.b");
        }

        [Fact]
        public async Task MissingModDownload_ResolverEnrichesVariantName_WithOnePass()
        {
            // Inject the resolver explicitly so enrichment is exercised.
            var service = CreateService(_mockModInfoResolver);
            var getPicker = SetupMissingModDownloadFlow();

            _mockModListManager.GetAllMods().Returns(new List<ModItem>());
            _mockModDictionaryService.GetAllEntriesByPackageId("mod.a").Returns(new List<ModDictionaryEntry>
            {
                Entry("mod.a", "101", "Dictionary Name", published: true)
            });

            _mockModInfoResolver.ResolveAsync(
                    Arg.Any<IEnumerable<string>>(),
                    Arg.Any<IEnumerable<string>>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<ModInfoResolutionCandidate>>>(
                    new Dictionary<string, IReadOnlyList<ModInfoResolutionCandidate>>
                    {
                        ["mod.a"] = new List<ModInfoResolutionCandidate>
                        {
                            new ModInfoResolutionCandidate
                            {
                                SteamId = "101",
                                DisplayName = "Live Steam Title"
                            }
                        }
                    }));

            await service.ImportModListAsync(new List<string> { "mod.a" }, "Colony");

            // Exactly one resolver pass for all candidate ids.
            await _mockModInfoResolver.Received(1).ResolveAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<CancellationToken>());

            var picker = getPicker();
            picker.Should().NotBeNull();
            var variant = picker!.ModGroups.Single().Variants.Single();
            variant.Name.Should().Be("Live Steam Title");
            // Enrichment must not touch selection/ordering semantics.
            variant.SteamId.Should().Be("101");
            variant.IsPublished.Should().BeTrue();
            picker.ModGroups.Single().SelectedVariant.Should().BeSameAs(variant);
        }

        [Fact]
        public async Task MissingModDownload_ResolverFailure_KeepsDictionaryNames_AndReachesPicker()
        {
            var service = CreateService(_mockModInfoResolver);
            var getPicker = SetupMissingModDownloadFlow();

            _mockModListManager.GetAllMods().Returns(new List<ModItem>());
            _mockModDictionaryService.GetAllEntriesByPackageId("mod.a").Returns(new List<ModDictionaryEntry>
            {
                Entry("mod.a", "101", "Dictionary Name", published: true)
            });

            _mockModInfoResolver.ResolveAsync(
                    Arg.Any<IEnumerable<string>>(),
                    Arg.Any<IEnumerable<string>>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromException<IReadOnlyDictionary<string, IReadOnlyList<ModInfoResolutionCandidate>>>(
                    new InvalidOperationException("Steam API down")));

            await service.ImportModListAsync(new List<string> { "mod.a" }, "Colony");

            // The failure is swallowed; the picker still shows the group with
            // the exact dictionary-driven name.
            var picker = getPicker();
            picker.Should().NotBeNull();
            var group = picker!.ModGroups.Single();
            group.Variants.Single().Name.Should().Be("Dictionary Name");
            group.SelectedVariant.Should().NotBeNull();
            group.SelectedVariant!.SteamId.Should().Be("101");
            await _mockDialogService.Received(1).ShowMissingModSelectionDialogAsync(
                Arg.Any<MissingModSelectionDialogViewModel>());
        }

        [Fact]
        public async Task MissingModDownload_NothingInstalledNothingKnown_ReportsSameUnknownIds()
        {
            var service = CreateService();
            var getPicker = SetupMissingModDownloadFlow();

            _mockModListManager.GetAllMods().Returns(new List<ModItem>());
            _mockModDictionaryService.GetAllEntriesByPackageId("unknown.1")
                .Returns(new List<ModDictionaryEntry>());
            _mockModDictionaryService.GetAllEntriesByPackageId("unknown.2")
                .Returns(new List<ModDictionaryEntry>());

            await service.ImportModListAsync(new List<string> { "unknown.1", "unknown.2" }, "Colony");

            var picker = getPicker();
            picker.Should().NotBeNull();
            picker!.ModGroups.Should().BeEmpty();
            picker.UnknownModIds.Should().Equal("unknown.1", "unknown.2");
            await _mockDialogService.Received(1).ShowMissingModSelectionDialogAsync(
                Arg.Any<MissingModSelectionDialogViewModel>());
        }
    }
}

