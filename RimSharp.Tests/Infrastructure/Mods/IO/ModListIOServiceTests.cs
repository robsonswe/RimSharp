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
using RimSharp.Infrastructure.Mods.IO;
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

        private ModListIOService CreateService()
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
                _mockSteamWorkshopQueueProcessor
            );
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
    }
}

