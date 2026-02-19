using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using NSubstitute;
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
            // Arrange
            var service = CreateService();
            var activeMods = new List<ModItem> { new ModItem { PackageId = "m1" } };
            string savePath = Path.Combine(_testTempDir, "Exported.xml");
            
            _mockDialogService.ShowSaveFileDialog(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns((true, savePath));

            var doc = new XDocument(new XElement("ModList", new XElement("m1")));
            _mockFileParser.Generate(Arg.Any<IEnumerable<string>>()).Returns(doc);

            // Act
            await service.ExportModListAsync(activeMods);

            // Assert
            File.Exists(savePath).Should().BeTrue();
            _mockDialogService.Received(1).ShowInformation(Arg.Is<string>(s => s.Contains("Export Successful")), Arg.Any<string>());
        }

        [Fact]
        public async Task ImportModListAsync_WhenSuccessful_ShouldInitializeManager()
        {
            // Arrange
            var service = CreateService();
            string importPath = Path.Combine(_testTempDir, "Import.xml");
            File.WriteAllText(importPath, "<xml/>");

            _mockDialogService.ShowOpenFileDialog(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns((true, importPath));

            var importedIds = new List<string> { "m1" };
            _mockFileParser.Parse(Arg.Any<XDocument>()).Returns(importedIds);

            var availableMods = new List<ModItem> { new ModItem { PackageId = "m1" } };
            _mockModListManager.GetAllMods().Returns(availableMods);

            // Act
            await service.ImportModListAsync();

            // Assert
            _mockModListManager.Received(1).Initialize(Arg.Any<IEnumerable<ModItem>>(), Arg.Is<IEnumerable<string>>(en => en.Contains("m1")));
        }
    }
}
