using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Services.ModManagement;
using Xunit;

namespace RimSharp.Tests.Services.ModManagement
{
    public class SaveHeaderServiceTests : IDisposable
    {
        private readonly ILoggerService _logger = Substitute.For<ILoggerService>();
        private readonly IPathService _pathService = Substitute.For<IPathService>();
        private readonly string _testTempDir;
        private readonly string _configDir;
        private readonly string _savesDir;

        public SaveHeaderServiceTests()
        {
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharp_SaveHeaderTests_" + Guid.NewGuid().ToString("N"));
            _configDir = Path.Combine(_testTempDir, "Config");
            _savesDir = Path.Combine(_configDir, "Saves");
            Directory.CreateDirectory(_savesDir);

            _pathService.GetConfigPath().Returns(_configDir);
            _pathService.GetSavesPath().Returns(_savesDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        private SaveHeaderService CreateService()
        {
            return new SaveHeaderService(_pathService, _logger);
        }

        #region Fixtures

        /// <summary>
        /// The minimal verified 1.6+ head: a plain UTF-8 save with a BOM and the
        /// three parallel meta lists (modIds / modSteamIds / modNames).
        /// </summary>
        private const string PlainSaveWithSteamIds =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<savegame>\r\n" +
            "\t<meta>\r\n" +
            "\t\t<gameVersion>1.7.4990 rev1065</gameVersion>\r\n" +
            "\t\t<modIds>\r\n" +
            "\t\t\t<li>ludeon.rimworld</li>\r\n" +
            "\t\t\t<li>test.mod.alpha</li>\r\n" +
            "\t\t\t<li>test.mod.beta</li>\r\n" +
            "\t\t</modIds>\r\n" +
            "\t\t<modSteamIds>\r\n" +
            "\t\t\t<li>0</li>\r\n" +
            "\t\t\t<li>2000000001</li>\r\n" +
            "\t\t\t<li>2000000002</li>\r\n" +
            "\t\t</modSteamIds>\r\n" +
            "\t\t<modNames>\r\n" +
            "\t\t\t<li>RimWorld</li>\r\n" +
            "\t\t\t<li>Test Mod Alpha</li>\r\n" +
            "\t\t\t<li>Test Mod Beta</li>\r\n" +
            "\t\t</modNames>\r\n" +
            "\t</meta>\r\n" +
            "\t<game>\r\n" +
            "\t\t<currentMapIndex>0</currentMapIndex>\r\n" +
            "\t</game>\r\n" +
            "</savegame>";

        /// <summary>
        /// The 1.5-era head: the same save without the modSteamIds list.
        /// </summary>
        private const string PlainSaveWithoutSteamIds =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<savegame>\r\n" +
            "\t<meta>\r\n" +
            "\t\t<gameVersion>1.5.1234 rev678</gameVersion>\r\n" +
            "\t\t<modIds>\r\n" +
            "\t\t\t<li>ludeon.rimworld</li>\r\n" +
            "\t\t\t<li>test.mod.alpha</li>\r\n" +
            "\t\t</modIds>\r\n" +
            "\t\t<modNames>\r\n" +
            "\t\t\t<li>RimWorld</li>\r\n" +
            "\t\t\t<li>Test Mod Alpha</li>\r\n" +
            "\t\t</modNames>\r\n" +
            "\t</meta>\r\n" +
            "\t<game>\r\n" +
            "\t\t<currentMapIndex>0</currentMapIndex>\r\n" +
            "\t</game>\r\n" +
            "</savegame>";

        /// <summary>
        /// The head of the save up to (but excluding) the history block.
        /// </summary>
        private const string HeadPrefix =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<savegame>\r\n" +
            "\t<meta>\r\n" +
            "\t\t<gameVersion>1.7.4990 rev1065</gameVersion>\r\n" +
            "\t\t<modIds>\r\n" +
            "\t\t\t<li>ludeon.rimworld</li>\r\n" +
            "\t\t</modIds>\r\n" +
            "\t\t<modNames>\r\n" +
            "\t\t\t<li>RimWorld</li>\r\n" +
            "\t\t</modNames>\r\n" +
            "\t</meta>\r\n" +
            "\t<game>\r\n" +
            "\t\t<currentMapIndex>0</currentMapIndex>\r\n" +
            "\t\t<info>\r\n" +
            "\t\t\t<realPlayTimeInteracting>11.3429451</realPlayTimeInteracting>\r\n" +
            "\t\t\t<startingTile>43378,0</startingTile>\r\n" +
            "\t\t\t<startingAndOptionalPawns>\r\n" +
            "\t\t\t\t<li>Thing_Human136</li>\r\n" +
            "\t\t\t</startingAndOptionalPawns>\r\n" +
            "\t\t</info>\r\n" +
            "\t\t<rules>\r\n" +
            "\t\t\t<disallowedBuildings />\r\n" +
            "\t\t\t<disallowedDesignatorTypes />\r\n" +
            "\t\t</rules>\r\n" +
            "\t\t<scenario>\r\n" +
            "\t\t\t<name>Crashlanded</name>\r\n" +
            "\t\t\t<summary>Three crashlanded survivors.</summary>\r\n" +
            "\t\t\t<playerFaction>\r\n" +
            "\t\t\t\t<def>PlayerFaction</def>\r\n" +
            "\t\t\t\t<factionDef>PlayerColony</factionDef>\r\n" +
            "\t\t\t</playerFaction>\r\n" +
            "\t\t</scenario>\r\n" +
            "\t\t<tickManager>\r\n" +
            "\t\t\t<ticksGame>12674</ticksGame>\r\n" +
            "\t\t\t<gameStartAbsTick>1500000</gameStartAbsTick>\r\n" +
            "\t\t\t<startingYear>5500</startingYear>\r\n" +
            "\t\t</tickManager>\r\n" +
            "\t\t<storyteller>\r\n" +
            "\t\t\t<def>Cassandra</def>\r\n" +
            "\t\t\t<difficulty>\r\n" +
            "\t\t\t\t<def>Rough</def>\r\n" +
            "\t\t\t</difficulty>\r\n" +
            "\t\t\t<incidentQueue />\r\n" +
            "\t\t</storyteller>\r\n" +
            "\t\t<researchManager>\r\n" +
            "\t\t\t<progress>\r\n" +
            "\t\t\t\t<keys>\r\n" +
            "\t\t\t\t\t<li>Electricity</li>\r\n" +
            "\t\t\t\t\t<li>Microelectronics</li>\r\n" +
            "\t\t\t\t\t<li>Multi-analyzer</li>\r\n" +
            "\t\t\t\t</keys>\r\n" +
            "\t\t\t</progress>\r\n" +
            "\t\t</researchManager>\r\n";

        /// <summary>
        /// The history block with the auto recorder groups. The records are
        /// whitespace separated base64 tokens, each one a 4-byte little-endian
        /// float32 - the last token is the current value of the recorder.
        /// </summary>
        private const string HistoryBlock =
            "\t\t<history>\r\n" +
            "\t\t\t<autoRecorderGroups>\r\n" +
            "\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t<def>Wealth</def>\r\n" +
            "\t\t\t\t\t<defName>Wealth_Total</defName>\r\n" +
            "\t\t\t\t\t<records>AAAAAA== AAAAAA== pEh5Rg==</records>\r\n" +
            "\t\t\t\t</li>\r\n" +
            "\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t<def>Wealth</def>\r\n" +
            "\t\t\t\t\t<defName>Wealth_Items</defName>\r\n" +
            "\t\t\t\t\t<records>AAAAAA== hMcSRg==</records>\r\n" +
            "\t\t\t\t</li>\r\n" +
            "\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t<def>Wealth</def>\r\n" +
            "\t\t\t\t\t<defName>Wealth_Buildings</defName>\r\n" +
            "\t\t\t\t\t<records>fyQoRQ==</records>\r\n" +
            "\t\t\t\t</li>\r\n" +
            "\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t<def>Population</def>\r\n" +
            "\t\t\t\t\t<defName>FreeColonists</defName>\r\n" +
            "\t\t\t\t\t<records>AABAQA==</records>\r\n" +
            "\t\t\t\t</li>\r\n" +
            "\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t<def>Prisoners</def>\r\n" +
            "\t\t\t\t\t<defName>Prisoners</defName>\r\n" +
            "\t\t\t\t\t<records>AAAAAA==</records>\r\n" +
            "\t\t\t\t</li>\r\n" +
            "\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t<def>ColonistMood</def>\r\n" +
            "\t\t\t\t\t<defName>ColonistMood</defName>\r\n" +
            "\t\t\t\t\t<records>AAAAgA==</records>\r\n" +
            "\t\t\t\t</li>\r\n" +
            "\t\t\t</autoRecorderGroups>\r\n" +
            "\t\t</history>\r\n";

        /// <summary>
        /// The same history block with a broken base64 token in the wealth total group.
        /// </summary>
        private const string HistoryBlockWithBrokenRecord =
            "\t\t<history>\r\n" +
            "\t\t\t<autoRecorderGroups>\r\n" +
            "\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t<def>Wealth</def>\r\n" +
            "\t\t\t\t\t<defName>Wealth_Total</defName>\r\n" +
            "\t\t\t\t\t<records>AAAAAA== not-base64!!</records>\r\n" +
            "\t\t\t\t</li>\r\n" +
            "\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t<def>Wealth</def>\r\n" +
            "\t\t\t\t\t<defName>Wealth_Items</defName>\r\n" +
            "\t\t\t\t\t<records>hMcSRg==</records>\r\n" +
            "\t\t\t\t</li>\r\n" +
            "\t\t\t</autoRecorderGroups>\r\n" +
            "\t\t</history>\r\n";

        /// <summary>
        /// The tail of the head: the components block and the beginning of the world
        /// block (which is never parsed - the reader stops in front of it).
        /// </summary>
        private const string HeadSuffix =
            "\t\t<components>\r\n" +
            "\t\t\t<li Class=\"GameComponent_Anomaly\">\r\n" +
            "\t\t\t\t<li Class=\"GameComponent_Nested\">\r\n" +
            "\t\t\t\t\t<def>GameComponent_Nested</def>\r\n" +
            "\t\t\t\t</li>\r\n" +
            "\t\t\t</li>\r\n" +
            "\t\t\t<li Class=\"GameComponent_Ideology\" />\r\n" +
            "\t\t</components>\r\n" +
            "\t\t<world>\r\n" +
            "\t\t\t<info>\r\n" +
            "\t\t\t\t<name>Castor</name>\r\n" +
            "\t\t\t</info>\r\n" +
            "\t\t</world>\r\n" +
            "\t</game>\r\n" +
            "</savegame>";

        /// <summary>
        /// A realistic head: the verified two-tab structure of the game block with
        /// scenario, storyteller, tick manager, history, research and components.
        /// </summary>
        private const string RealisticHeadSave = HeadPrefix + HistoryBlock + HeadSuffix;

        /// <summary>
        /// The same head without the history block.
        /// </summary>
        private const string HeadWithoutHistorySave = HeadPrefix + HeadSuffix;

        /// <summary>
        /// A head that stops in the middle of the history block.
        /// </summary>
        private const string TruncatedHeadSave =
            HeadPrefix +
            "\t\t<history>\r\n" +
            "\t\t\t<autoRecorderGroups>\r\n" +
            "\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t<def>Wealth</def>\r\n";

        private string WriteSaveFile(string name, string content, bool withBom = true)
        {
            string path = Path.Combine(_savesDir, name);
            File.WriteAllText(path, content, new UTF8Encoding(withBom));
            return path;
        }

        private string WriteGzipSaveFile(string name, string content)
        {
            string path = Path.Combine(_savesDir, name);
            using (var file = File.Create(path))
            using (var gzip = new GZipStream(file, CompressionMode.Compress))
            using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
            {
                writer.Write(content);
            }

            return path;
        }

        #endregion

        #region ListSavesAsync

        [Fact]
        public async Task ListSavesAsync_ReturnsTopLevelRwsFilesNewestFirst()
        {
            // Arrange
            var service = CreateService();
            string oldest = WriteSaveFile("Colony.rws", PlainSaveWithSteamIds);
            string newest = WriteSaveFile("Settlement.rws", PlainSaveWithSteamIds);
            string middle = WriteSaveFile("Outpost.rws", PlainSaveWithSteamIds);
            File.SetLastWriteTimeUtc(oldest, new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(middle, new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newest, new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc));

            // Not a save file.
            File.WriteAllText(Path.Combine(_savesDir, "notes.txt"), "ignore me");

            // Not a top level file.
            string subFolder = Path.Combine(_savesDir, "backup");
            Directory.CreateDirectory(subFolder);
            string nested = Path.Combine(subFolder, "Nested.rws");
            File.WriteAllText(nested, PlainSaveWithSteamIds);

            // Act
            var saves = await service.ListSavesAsync(TestContext.Current.CancellationToken);

            // Assert
            saves.Should().HaveCount(3);
            saves.Select(save => save.Name).Should().Equal("Settlement", "Outpost", "Colony");
            saves.All(save => save.Path.EndsWith(".rws", StringComparison.Ordinal)).Should().BeTrue();
            saves.Should().NotContain(save => save.Path == nested);
            saves[0].SizeBytes.Should().Be(new FileInfo(newest).Length);
            saves[0].LastModifiedUtc.Should().Be(new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc));
        }

        [Fact]
        public async Task ListSavesAsync_MissingSavesFolder_ReturnsEmptyList()
        {
            // Arrange
            var service = CreateService();
            _pathService.GetSavesPath().Returns(Path.Combine(_configDir, "NoSuchFolder"));

            // Act
            var saves = await service.ListSavesAsync(TestContext.Current.CancellationToken);

            // Assert
            saves.Should().BeEmpty();
        }

        #endregion

        #region ReadHeaderAsync

        [Fact]
        public async Task ReadHeaderAsync_PlainSaveWithBom_ParsesVersionAndMods()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Colony.rws", PlainSaveWithSteamIds);

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().NotBeNull();
            header!.Name.Should().Be("Colony");
            header.Path.Should().Be(path);
            header.GameVersion.Should().Be("1.7.4990 rev1065");
            header.ModIds.Should().Equal("ludeon.rimworld", "test.mod.alpha", "test.mod.beta");
            header.ModNames.Should().Equal("RimWorld", "Test Mod Alpha", "Test Mod Beta");
        }

        [Fact]
        public async Task ReadHeaderAsync_ParallelSteamIds_KeepsAlignmentAndZeroEntries()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Colony.rws", PlainSaveWithSteamIds);

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().NotBeNull();
            header!.ModSteamIds.Should().Equal("0", "2000000001", "2000000002");
            header.ModSteamIds.Should().HaveCount(header.ModIds.Count);
            header.ModSteamIds[0].Should().Be("0");
        }

        [Fact]
        public async Task ReadHeaderAsync_GzipSave_ParsesWithoutSteamIds()
        {
            // Arrange
            var service = CreateService();
            string path = WriteGzipSaveFile("OldColony.rws", PlainSaveWithoutSteamIds);

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().NotBeNull();
            header!.GameVersion.Should().Be("1.5.1234 rev678");
            header.ModIds.Should().Equal("ludeon.rimworld", "test.mod.alpha");
            header.ModNames.Should().Equal("RimWorld", "Test Mod Alpha");
            header.ModSteamIds.Should().BeEmpty();
        }

        [Fact]
        public async Task ReadHeaderAsync_PlainSaveWithoutBom_Parses()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Colony.rws", PlainSaveWithSteamIds, withBom: false);

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().NotBeNull();
            header!.GameVersion.Should().Be("1.7.4990 rev1065");
            header.ModIds.Should().HaveCount(3);
        }

        [Fact]
        public async Task ReadHeaderAsync_UnequalArrayLengths_KeepsListsIndependent()
        {
            // Arrange
            var service = CreateService();
            const string unevenSave =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<savegame>\r\n" +
                "\t<meta>\r\n" +
                "\t\t<gameVersion>1.7.4990 rev1065</gameVersion>\r\n" +
                "\t\t<modIds>\r\n" +
                "\t\t\t<li>test.mod.alpha</li>\r\n" +
                "\t\t\t<li>test.mod.beta</li>\r\n" +
                "\t\t</modIds>\r\n" +
                "\t\t<modSteamIds>\r\n" +
                "\t\t\t<li>2000000001</li>\r\n" +
                "\t\t</modSteamIds>\r\n" +
                "\t\t<modNames>\r\n" +
                "\t\t\t<li>Test Mod Alpha</li>\r\n" +
                "\t\t\t<li>Test Mod Beta</li>\r\n" +
                "\t\t\t<li>Test Mod Gamma</li>\r\n" +
                "\t\t</modNames>\r\n" +
                "\t</meta>\r\n" +
                "\t<game />\r\n" +
                "</savegame>";
            string path = WriteSaveFile("Uneven.rws", unevenSave);

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().NotBeNull();
            header!.ModIds.Should().HaveCount(2);
            header.ModSteamIds.Should().HaveCount(1);
            header.ModNames.Should().HaveCount(3);
            header.ModSteamIds.Should().Equal("2000000001");
        }

        [Fact]
        public async Task ReadHeaderAsync_GarbageContent_ReturnsNull()
        {
            // Arrange
            var service = CreateService();
            string path = Path.Combine(_savesDir, "Garbage.rws");
            File.WriteAllBytes(path, new byte[] { 0x00, 0x01, 0x02, 0x7F, 0xFF, 0xFE, 0x42, 0x13 });

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().BeNull();
        }

        [Fact]
        public async Task ReadHeaderAsync_MissingFile_ReturnsNull()
        {
            // Arrange
            var service = CreateService();
            string path = Path.Combine(_savesDir, "DoesNotExist.rws");

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().BeNull();
        }

        [Fact]
        public async Task ReadHeaderAsync_TruncatedBeforeMetaEnd_ReturnsNull()
        {
            // Arrange
            var service = CreateService();
            const string truncatedSave =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<savegame>\r\n" +
                "\t<meta>\r\n" +
                "\t\t<gameVersion>1.7.4990 rev1065</gameVersion>\r\n" +
                "\t\t<modIds>\r\n" +
                "\t\t\t<li>test.mod.alpha</li>\r\n";
            string path = WriteSaveFile("Truncated.rws", truncatedSave);

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().BeNull();
        }

        [Fact]
        public async Task ReadHeaderAsync_MetaExceedingCap_ReturnsNull()
        {
            // Arrange
            var service = CreateService();
            service.TestMetaCapBytes = 256;
            string path = WriteSaveFile("Colony.rws", PlainSaveWithSteamIds);

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().BeNull();
        }

        [Fact]
        public async Task ReadHeaderAsync_LockedFile_ReturnsNull()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Skip("File locking semantics differ on this platform.");
            }

            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Locked.rws", PlainSaveWithSteamIds);

            // Act
            using var lockStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().BeNull();
        }

        [Fact]
        public async Task ReadHeaderAsync_PreCancelledToken_Throws()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Colony.rws", PlainSaveWithSteamIds);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            // Act
            Func<Task> act = async () => await service.ReadHeaderAsync(path, cancellation.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        #endregion

        #region Compare and Diff

        [Fact]
        public void Compare_EmptyModIds_ReturnsUnknown()
        {
            // Arrange
            var service = CreateService();
            var header = new SaveHeader("Colony", "C:\\saves\\Colony.rws", "1.7.4990",
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

            // Act
            var result = service.Compare(header, new[] { "test.mod.alpha" }, new[] { "test.mod.alpha" });

            // Assert
            result.Should().Be(SaveCompat.Unknown);
        }

        [Fact]
        public void Compare_SameMods_ReturnsCompatible()
        {
            // Arrange
            var service = CreateService();
            var header = new SaveHeader("Colony", "C:\\saves\\Colony.rws", "1.7.4990",
                new[] { "test.mod.alpha", "test.mod.beta" }, Array.Empty<string>(), Array.Empty<string>());

            // Act
            var result = service.Compare(header,
                new[] { "Test.Mod.Beta", "TEST.MOD.ALPHA" },
                new[] { "test.mod.alpha", "test.mod.beta", "test.mod.gamma" });

            // Assert
            result.Should().Be(SaveCompat.Compatible);
        }

        [Fact]
        public void Compare_DifferentMods_ReturnsChanged()
        {
            // Arrange
            var service = CreateService();
            var header = new SaveHeader("Colony", "C:\\saves\\Colony.rws", "1.7.4990",
                new[] { "test.mod.alpha" }, Array.Empty<string>(), Array.Empty<string>());

            // Act
            var result = service.Compare(header,
                new[] { "test.mod.alpha", "test.mod.beta" },
                new[] { "test.mod.alpha", "test.mod.beta" });

            // Assert
            result.Should().Be(SaveCompat.Changed);
        }

        [Fact]
        public void Compare_SaveModNotInstalled_ReturnsMissing()
        {
            // Arrange
            var service = CreateService();
            var header = new SaveHeader("Colony", "C:\\saves\\Colony.rws", "1.7.4990",
                new[] { "test.mod.alpha", "test.mod.removed" }, Array.Empty<string>(), Array.Empty<string>());

            // Act
            var result = service.Compare(header,
                new[] { "test.mod.alpha" },
                new[] { "test.mod.alpha" });

            // Assert
            result.Should().Be(SaveCompat.Missing);
        }

        [Fact]
        public void Diff_ReturnsSortedAddedAndRemoved()
        {
            // Arrange
            var service = CreateService();
            var header = new SaveHeader("Colony", "C:\\saves\\Colony.rws", "1.7.4990",
                new[] { "test.mod.alpha", "test.mod.beta" }, Array.Empty<string>(), Array.Empty<string>());

            // Act
            var (added, removed) = service.Diff(header, new[] { "test.mod.alpha", "test.mod.delta", "test.mod.gamma" });

            // Assert
            added.Should().Equal("test.mod.delta", "test.mod.gamma");
            removed.Should().Equal("test.mod.beta");
        }

        [Fact]
        public void Diff_IgnoresCase()
        {
            // Arrange
            var service = CreateService();
            var header = new SaveHeader("Colony", "C:\\saves\\Colony.rws", "1.7.4990",
                new[] { "Test.Mod.Alpha" }, Array.Empty<string>(), Array.Empty<string>());

            // Act
            var (added, removed) = service.Diff(header, new[] { "test.mod.alpha" });

            // Assert
            added.Should().BeEmpty();
            removed.Should().BeEmpty();
        }

        #endregion

        #region ReadDetailsAsync

        [Fact]
        public async Task ReadDetailsAsync_RealisticHead_ExtractsEveryField()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Realistic.rws", RealisticHeadSave);

            // Act
            var details = await service.ReadDetailsAsync(path, TestContext.Current.CancellationToken);

            // Assert
            details.Should().NotBeNull();
            details!.ScenarioName.Should().Be("Crashlanded");
            details.StorytellerDef.Should().Be("Cassandra");
            details.DifficultyDef.Should().Be("Rough");
            details.TicksGame.Should().Be(12674);
            details.GameStartAbsTick.Should().Be(1500000);
            details.StartingYear.Should().Be(5500);
            details.RealPlayTimeInteracting.Should().NotBeNull();
            details.RealPlayTimeInteracting!.Value.Should().BeApproximately(11.3429451, 0.0001);
            details.StartingTile.Should().Be("43378,0");
            details.WealthTotal.Should().NotBeNull();
            details.WealthTotal!.Value.Should().BeApproximately(15954.16, 0.01);
            details.WealthItems.Should().NotBeNull();
            details.WealthItems!.Value.Should().BeApproximately(9393.879, 0.01);
            details.WealthBuildings.Should().NotBeNull();
            details.WealthBuildings!.Value.Should().BeApproximately(2690.281, 0.01);
            details.FreeColonists.Should().Be(3);
            details.Prisoners.Should().Be(0);
            details.ResearchDefsCompleted.Should().Be(3);
            details.ActiveDlcClasses.Should().BeEquivalentTo(new[] { "Anomaly", "Ideology" });
        }

        [Fact]
        public async Task ReadDetailsAsync_HeadWithoutHistory_LeavesWealthFieldsNull()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("NoHistory.rws", HeadWithoutHistorySave);

            // Act
            var details = await service.ReadDetailsAsync(path, TestContext.Current.CancellationToken);

            // Assert
            details.Should().NotBeNull();
            details!.ScenarioName.Should().Be("Crashlanded");
            details.StorytellerDef.Should().Be("Cassandra");
            details.DifficultyDef.Should().Be("Rough");
            details.StartingTile.Should().Be("43378,0");
            details.ResearchDefsCompleted.Should().Be(3);
            details.WealthTotal.Should().BeNull();
            details.WealthItems.Should().BeNull();
            details.WealthBuildings.Should().BeNull();
            details.FreeColonists.Should().BeNull();
            details.Prisoners.Should().BeNull();
        }

        [Fact]
        public async Task ReadDetailsAsync_BrokenBase64Record_LeavesThatFieldNull()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("BrokenRecord.rws", HeadPrefix + HistoryBlockWithBrokenRecord + HeadSuffix);

            // Act
            var details = await service.ReadDetailsAsync(path, TestContext.Current.CancellationToken);

            // Assert
            details.Should().NotBeNull();
            details!.WealthTotal.Should().BeNull();
            details.WealthItems.Should().NotBeNull();
            details.WealthItems!.Value.Should().BeApproximately(9393.879, 0.01);
            details.ScenarioName.Should().Be("Crashlanded");
        }

        [Fact]
        public async Task ReadDetailsAsync_TruncatedFile_ReturnsNull()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Truncated.rws", TruncatedHeadSave);

            // Act
            var details = await service.ReadDetailsAsync(path, TestContext.Current.CancellationToken);

            // Assert
            details.Should().BeNull();
        }

        [Fact]
        public async Task ReadDetailsAsync_CapSmallerThanWorldPosition_ReturnsNull()
        {
            // Arrange
            var service = CreateService();
            service.TestDetailsCapBytes = 512;
            string path = WriteSaveFile("Realistic.rws", RealisticHeadSave);

            // Act
            var details = await service.ReadDetailsAsync(path, TestContext.Current.CancellationToken);

            // Assert
            details.Should().BeNull();
        }

        [Fact]
        public async Task ReadDetailsAsync_GarbageContent_ReturnsNull()
        {
            // Arrange
            var service = CreateService();
            string path = Path.Combine(_savesDir, "Garbage.rws");
            File.WriteAllBytes(path, new byte[] { 0x00, 0x01, 0x02, 0x7F, 0xFF, 0xFE, 0x42, 0x13 });

            // Act
            var details = await service.ReadDetailsAsync(path, TestContext.Current.CancellationToken);

            // Assert
            details.Should().BeNull();
        }

        #endregion

        #region FormatInGameDate

        [Fact]
        public void FormatInGameDate_FormatsTheDocumentedExample()
        {
            // Act
            string? formatted = ISaveHeaderService.FormatInGameDate(12674, 1500000, 5500);

            // Assert
            formatted.Should().Be("Year 5500, Summer, day 11, 05:00");
        }

        [Fact]
        public void FormatInGameDate_RollsOverIntoTheNextSeason()
        {
            // Act
            string? lastDayOfSpring = ISaveHeaderService.FormatInGameDate(899999, 0, 5500);
            string? firstDayOfSummer = ISaveHeaderService.FormatInGameDate(900000, 0, 5500);

            // Assert
            lastDayOfSpring.Should().Be("Year 5500, Spring, day 15, 23:00");
            firstDayOfSummer.Should().Be("Year 5500, Summer, day 1, 00:00");
        }

        [Fact]
        public void FormatInGameDate_SeasonWrapsWithYearCarry()
        {
            // Act - the first minute of the second year (Saves' seasons wrap per year)
            string? firstOfSecondYear = ISaveHeaderService.FormatInGameDate(3_600_000, 0, 5500);

            // and the last minute of the first year
            string? lastOfFirstYear = ISaveHeaderService.FormatInGameDate(3_599_999, 0, 5500);

            // Assert
            firstOfSecondYear.Should().Be("Year 5501, Spring, day 1, 00:00");
            lastOfFirstYear.Should().Be("Year 5500, Winter, day 15, 23:00");
        }

        [Fact]
        public void FormatInGameDate_MissingOrEmptyInput_ReturnsNull()
        {
            // Act
            string? missingTicks = ISaveHeaderService.FormatInGameDate(null, 1500000, 5500);
            string? missingStart = ISaveHeaderService.FormatInGameDate(12674, null, 5500);
            string? missingYear = ISaveHeaderService.FormatInGameDate(12674, 1500000, null);
            string? everythingZero = ISaveHeaderService.FormatInGameDate(0, 0, 0);

            // Assert
            missingTicks.Should().BeNull();
            missingStart.Should().BeNull();
            missingYear.Should().BeNull();
            everythingZero.Should().BeNull();
        }

        #endregion

        #region GetSavesPath

        [Fact]
        public void GetSavesPath_IsTheSavesFolderInsideTheConfigFolder()
        {
            // Arrange
            var configService = Substitute.For<IConfigService>();
            configService.GetConfigValue("config_folder").Returns(_configDir);
            var pathService = new PathService(configService);

            // Act
            string savesPath = pathService.GetSavesPath();

            // Assert
            savesPath.Should().Be(Path.Combine(_configDir, "Saves"));
        }

        #endregion
    }
}
