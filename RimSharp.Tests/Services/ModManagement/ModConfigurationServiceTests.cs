using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Services.ModManagement;
using Xunit;

namespace RimSharp.Tests.Services.ModManagement
{
    public class ModConfigurationServiceTests : IDisposable
    {
        private readonly ILoggerService _logger = Substitute.For<ILoggerService>();
        private readonly IPathService _pathService = Substitute.For<IPathService>();
        private readonly IFileDialogService _fileDialogService = Substitute.For<IFileDialogService>();
        private readonly GameInstance _gameInstance = new GameInstance
        {
            GameFolder = @"C:\Test\RimWorld",
            ConfigFolder = @"C:\Test\Config",
            LocalFolder = @"C:\Test\RimWorld\Mods",
            WorkshopFolder = @"C:\Test\Steam\steamapps\workshop\content\294100",
            IsSteamInstallation = true,
            SteamClientIntegration = true
        };
        private readonly string _testTempDir;

        public ModConfigurationServiceTests()
        {
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharp_ModConfigTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);

            _gameInstance.GameFolder = Path.Combine(_testTempDir, "RimWorld");
            _gameInstance.ConfigFolder = Path.Combine(_testTempDir, "Config");
            _gameInstance.LocalFolder = Path.Combine(_testTempDir, "RimWorld", "Mods");
            _gameInstance.WorkshopFolder = Path.Combine(_testTempDir, "Steam", "steamapps", "workshop", "content", "294100");

            Directory.CreateDirectory(_gameInstance.GameFolder);
            Directory.CreateDirectory(_gameInstance.ConfigFolder);
            Directory.CreateDirectory(_gameInstance.LocalFolder);
            Directory.CreateDirectory(_gameInstance.WorkshopFolder);
            Directory.CreateDirectory(Path.Combine(_gameInstance.LocalFolder, "ModA", "About"));
            Directory.CreateDirectory(Path.Combine(_gameInstance.LocalFolder, "ModB", "About"));
            Directory.CreateDirectory(Path.Combine(_gameInstance.WorkshopFolder, "123456789", "About"));

            _pathService.GetConfigPath().Returns(_gameInstance.ConfigFolder);
            _pathService.GetSavesPath().Returns(Path.Combine(_gameInstance.ConfigFolder!, "Saves"));
            _pathService.GetGamePath().Returns(_gameInstance.GameFolder);
            _pathService.GetModsPath().Returns(_gameInstance.LocalFolder);

            // Setup About.xml for local mods
            File.WriteAllText(Path.Combine(_gameInstance.LocalFolder, "ModA", "About", "About.xml"),
                @"<?xml version=""1.0""?><ModMetaData><packageId>test.moda</packageId><name>Mod A</name></ModMetaData>");
            File.WriteAllText(Path.Combine(_gameInstance.LocalFolder, "ModB", "About", "About.xml"),
                @"<?xml version=""1.0""?><ModMetaData><packageId>test.modb</packageId><name>Mod B</name></ModMetaData>");

            // Setup About.xml for workshop mod
            File.WriteAllText(Path.Combine(_gameInstance.WorkshopFolder, "123456789", "About", "About.xml"),
                @"<?xml version=""1.0""?><ModMetaData><packageId>test.moda</packageId><name>Mod A Workshop</name></ModMetaData>");
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        private ModConfigurationService CreateService()
        {
            return new ModConfigurationService(_gameInstance, _logger, _pathService, _fileDialogService);
        }

        #region ExportModListAsync Tests

        [Fact]
        public async Task ExportModListAsync_RoundTrip_PreservesData()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            var modsConfigPath = Path.Combine(configDir, "ModsConfig.xml");

            var originalDoc = new XDocument(
                new XElement("ModsConfigData",
                    new XElement("version", "1.4"),
                    new XElement("activeMods",
                        new XElement("li", "ludeon.rimworld"),
                        new XElement("li", "test.moda"),
                        new XElement("li", "test.modb")
                    ),
                    new XElement("knownExpansions",
                        new XElement("li", "ludeon.rimworld.royalty")
                    )
                ));
            originalDoc.Save(modsConfigPath);

            var exportPath = Path.Combine(_testTempDir, "ExportedModList.xml");
            _fileDialogService.ShowSaveFileDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromResult((true, (string?)exportPath)));

            // Act
            var result = await service.ExportModListAsync(null, TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();
            File.Exists(exportPath).Should().BeTrue();

            var exportedDoc = XDocument.Load(exportPath);
            var root = exportedDoc.Root!;
            root.Element("version")?.Value.Should().Be("1.4");
            root.Element("activeMods")?.Elements("li").Select(x => x.Value).Should().BeEquivalentTo("ludeon.rimworld", "test.moda", "test.modb");
            root.Element("knownExpansions")?.Elements("li").Select(x => x.Value).Should().BeEquivalentTo("ludeon.rimworld.royalty");
        }

        [Fact]
        public async Task ExportModListAsync_MissingVersion_DefaultsTo14()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            var modsConfigPath = Path.Combine(configDir, "ModsConfig.xml");

            var originalDoc = new XDocument(
                new XElement("ModsConfigData",
                    new XElement("activeMods",
                        new XElement("li", "ludeon.rimworld")
                    )
                ));
            originalDoc.Save(modsConfigPath);

            var exportPath = Path.Combine(_testTempDir, "ExportedModList.xml");
            _fileDialogService.ShowSaveFileDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromResult((true, (string?)exportPath)));

            // Act
            var result = await service.ExportModListAsync(null, TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();
            var exportedDoc = XDocument.Load(exportPath);
            exportedDoc.Root!.Element("version")?.Value.Should().Be("1.4");
        }

        [Fact]
        public async Task ExportModListAsync_MissingKnownExpansions_AddsEmptyElement()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            var modsConfigPath = Path.Combine(configDir, "ModsConfig.xml");

            var originalDoc = new XDocument(
                new XElement("ModsConfigData",
                    new XElement("version", "1.4"),
                    new XElement("activeMods",
                        new XElement("li", "ludeon.rimworld")
                    )
                ));
            originalDoc.Save(modsConfigPath);

            var exportPath = Path.Combine(_testTempDir, "ExportedModList.xml");
            _fileDialogService.ShowSaveFileDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromResult((true, (string?)exportPath)));

            // Act
            var result = await service.ExportModListAsync(null, TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();
            var exportedDoc = XDocument.Load(exportPath);
            exportedDoc.Root!.Element("knownExpansions").Should().NotBeNull();
        }

        #endregion

        #region ImportModListAsync Tests

        [Fact]
        public async Task ImportModListAsync_WritesFileAndCreatesBackup()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            var modsConfigPath = Path.Combine(configDir, "ModsConfig.xml");

            // Original config
            var originalDoc = new XDocument(
                new XElement("ModsConfigData",
                    new XElement("version", "1.4"),
                    new XElement("activeMods",
                        new XElement("li", "ludeon.rimworld")
                    )
                ));
            originalDoc.Save(modsConfigPath);

            // Import file
            var importPath = Path.Combine(_testTempDir, "ImportModList.xml");
            var importDoc = new XDocument(
                new XElement("ModsConfigData",
                    new XElement("version", "1.4"),
                    new XElement("activeMods",
                        new XElement("li", "ludeon.rimworld"),
                        new XElement("li", "test.moda")
                    ),
                    new XElement("knownExpansions",
                        new XElement("li", "ludeon.rimworld.royalty")
                    )
                ));
            importDoc.Save(importPath);

            _fileDialogService.ShowOpenFileDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromResult((true, (string?)importPath)));

            // Act
            var result = await service.ImportModListAsync(null, TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();

            // Check imported content
            var importedDoc = XDocument.Load(modsConfigPath);
            importedDoc.Root!.Element("activeMods")?.Elements("li").Select(x => x.Value).Should().BeEquivalentTo("ludeon.rimworld", "test.moda");

            // Check rolling backup was created
            var backups = await service.ListModConfigBackupsAsync(TestContext.Current.CancellationToken);
            backups.Should().ContainSingle(b => b.IsRollingBackup);
        }

        [Fact]
        public async Task ImportModListAsync_InvalidXml_ReturnsFail()
        {
            // Arrange
            var service = CreateService();
            var importPath = Path.Combine(_testTempDir, "Invalid.xml");
            File.WriteAllText(importPath, "not valid xml");

            _fileDialogService.ShowOpenFileDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromResult((true, (string?)importPath)));

            // Act
            var result = await service.ImportModListAsync(null, TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Invalid XML");
        }

        [Fact]
        public async Task ImportModListAsync_NotModsConfigFormat_ReturnsFail()
        {
            // Arrange
            var service = CreateService();
            var importPath = Path.Combine(_testTempDir, "NotModsConfig.xml");
            var doc = new XDocument(new XElement("OtherRoot"));
            doc.Save(importPath);

            _fileDialogService.ShowOpenFileDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromResult((true, (string?)importPath)));

            // Act
            var result = await service.ImportModListAsync(null, TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Not a valid ModsConfig.xml format");
        }

        #endregion

        #region ClearAllModsAsync Tests

        [Fact]
        public async Task ClearAllModsAsync_CreatesBackup_ResetsToVanilla_EmptiesModsFolder()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            var modsConfigPath = Path.Combine(configDir, "ModsConfig.xml");
            var modsDir = _gameInstance.LocalFolder!;

            // Original config with mods
            var originalDoc = new XDocument(
                new XElement("ModsConfigData",
                    new XElement("version", "1.4"),
                    new XElement("activeMods",
                        new XElement("li", "ludeon.rimworld"),
                        new XElement("li", "test.moda"),
                        new XElement("li", "test.modb")
                    ),
                    new XElement("knownExpansions",
                        new XElement("li", "ludeon.rimworld.royalty")
                    )
                ));
            originalDoc.Save(modsConfigPath);

            // Add some files to Mods folder
            File.WriteAllText(Path.Combine(modsDir, "ModA", "About", "About.xml"), "<Mod/>");
            File.WriteAllText(Path.Combine(modsDir, "ModB", "About", "About.xml"), "<Mod/>");
            File.WriteAllText(Path.Combine(modsDir, "SomeFile.txt"), "file");

            // Act
            var result = await service.ClearAllModsAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();
            result.Count.Should().BeGreaterThan(0);

            // Check ModsConfig.xml reset to vanilla
            var resetDoc = XDocument.Load(modsConfigPath);
            resetDoc.Root!.Element("version")?.Value.Should().Be("1.4");
            resetDoc.Root!.Element("activeMods")?.Elements("li").Select(x => x.Value).Should().BeEquivalentTo("ludeon.rimworld");
            resetDoc.Root!.Element("knownExpansions")?.Elements("li").Should().BeEmpty();

            // Check Mods folder emptied (but folder exists)
            Directory.Exists(modsDir).Should().BeTrue();
            new DirectoryInfo(modsDir).GetFileSystemInfos().Should().BeEmpty();

            // Check simple backup created
            File.Exists(modsConfigPath + ".xml.backup").Should().BeTrue();

            // Check rolling backup created
            var backups = await service.ListModConfigBackupsAsync(TestContext.Current.CancellationToken);
            backups.Should().ContainSingle(b => b.IsRollingBackup);
        }

        #endregion

        #region SanitizeCoreDlcOrderAsync Tests

        [Fact]
        public async Task SanitizeCoreDlcOrderAsync_ReordersViolation_EnforcesHierarchy()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            var modsConfigPath = Path.Combine(configDir, "ModsConfig.xml");

            // Wrong order: DLC before Core, mod between Core and Royalty
            var originalDoc = new XDocument(
                new XElement("ModsConfigData",
                    new XElement("version", "1.4"),
                    new XElement("activeMods",
                        new XElement("li", "ludeon.rimworld.royalty"),
                        new XElement("li", "ludeon.rimworld"),
                        new XElement("li", "some.mod"),
                        new XElement("li", "ludeon.rimworld.ideology")
                    ),
                    new XElement("knownExpansions")
                ));
            originalDoc.Save(modsConfigPath);

            // Act
            var result = await service.SanitizeCoreDlcOrderAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();

            var sanitizedDoc = XDocument.Load(modsConfigPath);
            var activeMods = sanitizedDoc.Root!.Element("activeMods")!.Elements("li").Select(x => x.Value).ToList();

            // Core should be first
            activeMods[0].Should().Be("ludeon.rimworld");
            // Royalty should be second
            activeMods[1].Should().Be("ludeon.rimworld.royalty");
            // Ideology should be third
            activeMods[2].Should().Be("ludeon.rimworld.ideology");
            // Other mods after DLCs
            activeMods.Should().Contain("some.mod");
            activeMods.IndexOf("some.mod").Should().BeGreaterThan(activeMods.IndexOf("ludeon.rimworld.ideology"));
        }

        [Fact]
        public async Task SanitizeCoreDlcOrderAsync_AddsMissingDlcsToKnownExpansions()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            var modsConfigPath = Path.Combine(configDir, "ModsConfig.xml");

            var originalDoc = new XDocument(
                new XElement("ModsConfigData",
                    new XElement("version", "1.4"),
                    new XElement("activeMods",
                        new XElement("li", "ludeon.rimworld"),
                        new XElement("li", "ludeon.rimworld.royalty"),
                        new XElement("li", "ludeon.rimworld.ideology")
                    ),
                    new XElement("knownExpansions") // Empty
                ));
            originalDoc.Save(modsConfigPath);

            // Act
            var result = await service.SanitizeCoreDlcOrderAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();

            var sanitizedDoc = XDocument.Load(modsConfigPath);
            var knownExpansions = sanitizedDoc.Root!.Element("knownExpansions")!.Elements("li").Select(x => x.Value).ToList();

            knownExpansions.Should().Contain("ludeon.rimworld.royalty");
            knownExpansions.Should().Contain("ludeon.rimworld.ideology");
        }

        #endregion

        #region DetectDuplicatePackageIdsAsync Tests

        [Fact]
        public async Task DetectDuplicatePackageIdsAsync_FindsLocalVsWorkshopDuplicate()
        {
            // Arrange
            var service = CreateService();

            // ModA (local) and workshop/123456789 both have packageId "test.moda"

            // Act
            var result = await service.DetectDuplicatePackageIdsAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();
            result.Count.Should().Be(1);

            var duplicates = result.Details!["duplicates"] as List<ModConfigurationService.DuplicateModInfo>;
            duplicates.Should().NotBeNull();
            duplicates![0].PackageId.Should().Be("test.moda");
            duplicates[0].LocalPaths.Should().Contain("Mods/ModA");
            duplicates[0].WorkshopPaths.Should().Contain("workshop/content/294100/123456789");
        }

        #endregion

        #region Rolling Backup Tests

        [Fact]
        public async Task RollingBackup_KeepsLast10()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            var modsConfigPath = Path.Combine(configDir, "ModsConfig.xml");

            var baseDoc = new XDocument(
                new XElement("ModsConfigData",
                    new XElement("version", "1.4"),
                    new XElement("activeMods", new XElement("li", "ludeon.rimworld"))
                ));

            // Use test seam to control timestamps
            var originalUtcNow = ModConfigurationService.TestUtcNowOverride;
            var baseTime = DateTime.UtcNow;
            try
            {
                // Create 12 backups by importing 12 times with incrementing timestamps
                for (int i = 0; i < 12; i++)
                {
                    ModConfigurationService.TestUtcNowOverride = () => baseTime.AddSeconds(i);
                    baseDoc.Save(modsConfigPath);
                    var importPath = Path.Combine(_testTempDir, $"Import{i}.xml");
                    var importDoc = new XDocument(
                        new XElement("ModsConfigData",
                            new XElement("version", "1.4"),
                            new XElement("activeMods",
                                new XElement("li", "ludeon.rimworld"),
                                new XElement("li", $"mod{i}")
                            )
                        ));
                    importDoc.Save(importPath);

                    _fileDialogService.ShowOpenFileDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                        .Returns(Task.FromResult((true, (string?)importPath)));

                    await service.ImportModListAsync(null, TestContext.Current.CancellationToken);
                }
            }
            finally
            {
                ModConfigurationService.TestUtcNowOverride = originalUtcNow;
            }

            // Act
            var backups = await service.ListModConfigBackupsAsync(TestContext.Current.CancellationToken);

            // Assert
            var rollingBackups = backups.Where(b => b.IsRollingBackup).ToList();
            rollingBackups.Count.Should().Be(10); // Only last 10 kept
        }

        #endregion

        #region AnalyzeSavesQuotaAsync Tests

        [Fact]
        public async Task AnalyzeSavesQuotaAsync_ReportsSize()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            var savesDir = Path.Combine(configDir, "Saves");
            Directory.CreateDirectory(savesDir);

            // Create some save files
            File.WriteAllText(Path.Combine(savesDir, "save1.rws"), new string('x', 1024 * 1024)); // 1MB
            File.WriteAllText(Path.Combine(savesDir, "save2.rws"), new string('x', 2 * 1024 * 1024)); // 2MB

            // Act
            var result = await service.AnalyzeSavesQuotaAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();
            result.Count.Should().Be(2);
            ((long)result.Details!["sizeMB"]).Should().Be(3);
            ((bool)result.Details!["overThreshold"]).Should().BeFalse();
        }

        [Fact]
        public async Task AnalyzeSavesQuotaAsync_OverThreshold_Warns()
        {
            // Arrange
            var service = CreateService();
            var configDir = _gameInstance.ConfigFolder!;
            var savesDir = Path.Combine(configDir, "Saves");
            Directory.CreateDirectory(savesDir);

            // Create a large save file (600MB)
            var largeSave = Path.Combine(savesDir, "large.rws");
            using (var fs = new FileStream(largeSave, FileMode.Create))
            {
                var buffer = new byte[1024 * 1024];
                for (int i = 0; i < 600; i++)
                {
                    await fs.WriteAsync(buffer, TestContext.Current.CancellationToken);
                }
            }

            // Act
            var result = await service.AnalyzeSavesQuotaAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Success.Should().BeTrue();
            ((bool)result.Details!["overThreshold"]).Should().BeTrue();
            ((long)result.Details!["sizeMB"]).Should().BeGreaterOrEqualTo(500);
            result.Message.Should().Contain("EXCEEDS 500 MB");
        }

        #endregion
    }
}