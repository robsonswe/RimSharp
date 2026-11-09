using System;
using System.IO;
using FluentAssertions;
using RimSharp.Services.ModManagement;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Services.ModManagement
{
    public class ModClassificationServiceTests
    {
        private static ModClassificationService CreateService(
            string localFolder,
            string? workshopFolder = null,
            string? gameFolder = null)
        {
            var instance = new GameInstance
            {
                GameFolder = gameFolder,
                LocalFolder = localFolder,
                WorkshopFolder = workshopFolder
            };
            return new ModClassificationService(instance);
        }

        private static string CreateMod(string root, string folderName, string packageId = "some.mod")
        {
            var modPath = Path.Combine(root, folderName);
            var aboutDir = Path.Combine(modPath, "About");
            Directory.CreateDirectory(aboutDir);
            File.WriteAllText(Path.Combine(aboutDir, "About.xml"),
                $"<?xml version=\"1.0\"?><ModMetaData><name>Test</name><packageId>{packageId}</packageId></ModMetaData>");
            return modPath;
        }

        [Fact]
        public void LocalMod_WithNoMarkers_IsUntracked_NotCore()
        {
            // The reported bug: a freshly parsed folder arrives with the enum
            // default (Core); outside the game's Data folder that must never
            // survive as a classification.
            using var root = new TempDirectory();
            var modPath = CreateMod(root.Path, "HandPlacedMod");

            var service = CreateService(root.Path);

            service.ClassifyMod(modPath, ModType.Core).Should().Be(ModType.Untracked);
            service.ClassifyMod(modPath).Should().Be(ModType.Untracked);
        }

        [Fact]
        public void LocalMod_WithPublishedFileIdMarker_IsTracked()
        {
            using var root = new TempDirectory();
            var modPath = CreateMod(root.Path, "3733484696");
            File.WriteAllText(Path.Combine(modPath, "About", "PublishedFileId.txt"), "3733484696");

            var service = CreateService(root.Path);

            service.ClassifyMod(modPath).Should().Be(ModType.Tracked);
        }

        [Fact]
        public void LocalMod_WithMismatchedPublishedFileId_IsNotTracked()
        {
            // Author-published marker whose id does not match the folder name
            // (a copied folder) must not claim update tracking.
            using var root = new TempDirectory();
            var modPath = CreateMod(root.Path, "SomeLocalMod");
            File.WriteAllText(Path.Combine(modPath, "About", "PublishedFileId.txt"), "3733484696");

            var service = CreateService(root.Path);

            service.ClassifyMod(modPath).Should().Be(ModType.Untracked);
        }

        [Fact]
        public void LocalMod_WithAppMetadata_IsTracked()
        {
            // The app's own download marker: RimSharp.json with a SteamId.
            using var root = new TempDirectory();
            var modPath = CreateMod(root.Path, "3733484696");
            File.WriteAllText(Path.Combine(modPath, "RimSharp.json"),
                "{\"SteamId\":\"3733484696\",\"Source\":\"browser\"}");

            var service = CreateService(root.Path);

            service.ClassifyMod(modPath).Should().Be(ModType.Tracked);
        }

        [Fact]
        public void LocalMod_WithAppTimestamps_IsTracked()
        {
            // DateStamp and About/timestamp.txt are written by the download
            // pipeline and nothing else.
            using var root = new TempDirectory();
            var modPath = CreateMod(root.Path, "3733484696");
            File.WriteAllText(Path.Combine(modPath, "DateStamp"), "31 Oct, 2026 @ 7:07PM");

            var service = CreateService(root.Path);

            service.ClassifyMod(modPath).Should().Be(ModType.Tracked);
        }

        [Fact]
        public void LocalMod_WithAboutTimestamp_IsTracked()
        {
            using var root = new TempDirectory();
            var modPath = CreateMod(root.Path, "3733484696");
            File.WriteAllText(Path.Combine(modPath, "About", "timestamp.txt"), "31/10/2026 19:07:00");

            var service = CreateService(root.Path);

            service.ClassifyMod(modPath).Should().Be(ModType.Tracked);
        }

        [Fact]
        public void LocalMod_WithAuthorPublishStampOnly_IsUntracked()
        {
            // About/PublishStamp.txt ships inside the published item from any
            // source; it is not a marker of an app download.
            using var root = new TempDirectory();
            var modPath = CreateMod(root.Path, "HandPlacedMod");
            File.WriteAllText(Path.Combine(modPath, "About", "PublishStamp.txt"), "verified ...");

            var service = CreateService(root.Path);

            service.ClassifyMod(modPath).Should().Be(ModType.Untracked);
        }

        [Fact]
        public void LocalMod_WithUnreadableAppMetadata_IsUntracked()
        {
            using var root = new TempDirectory();
            var modPath = CreateMod(root.Path, "HandPlacedMod");
            File.WriteAllText(Path.Combine(modPath, "RimSharp.json"), "not json");

            var service = CreateService(root.Path);

            service.ClassifyMod(modPath).Should().Be(ModType.Untracked);
        }

        [Fact]
        public void LocalMod_WithNumericFolderName_IsTracked()
        {
            // Install-dir convention: Workshop items land in folders named by
            // their id, so a number-only name is a workshop copy even with every
            // marker stripped.
            using var root = new TempDirectory();
            var modPath = CreateMod(root.Path, "3733484696");

            var service = CreateService(root.Path);

            service.ClassifyMod(modPath).Should().Be(ModType.Tracked);
        }

        [Fact]
        public void LocalMod_WithNumericFolderNameAndGit_IsTracked()
        {
            // Workshop items also ship .git directories; the install-dir
            // convention takes precedence, same as the reference documents.
            using var root = new TempDirectory();
            var modPath = CreateMod(root.Path, "3733484696");
            Directory.CreateDirectory(Path.Combine(modPath, ".git"));

            var service = CreateService(root.Path);

            service.ClassifyMod(modPath).Should().Be(ModType.Tracked);
        }

        [Fact]
        public void LocalMod_WithGitFolder_IsGit()
        {
            using var root = new TempDirectory();
            var modPath = CreateMod(root.Path, "MyGitMod");
            Directory.CreateDirectory(Path.Combine(modPath, ".git"));

            var service = CreateService(root.Path);

            service.ClassifyMod(modPath).Should().Be(ModType.Git);
        }

        [Fact]
        public void WorkshopMod_IsWorkshop()
        {
            using var root = new TempDirectory();
            var modPath = CreateMod(root.Path, "3733484696");

            var service = CreateService(root.Path, workshopFolder: root.Path);

            service.ClassifyMod(modPath).Should().Be(ModType.Workshop);
        }

        [Fact]
        public void DataMod_NamedCore_IsCore()
        {
            using var root = new TempDirectory();
            var corePath = Path.Combine(root.Path, "Data", "Core");
            Directory.CreateDirectory(corePath);
            File.WriteAllText(Path.Combine(corePath, "About.xml"),
                "<ModMetaData><packageId>Ludeon.RimWorld</packageId></ModMetaData>");

            var service = CreateService(Path.Combine(root.Path, "Mods"), gameFolder: root.Path);

            service.ClassifyMod(corePath).Should().Be(ModType.Core);
        }

        [Fact]
        public void DataMod_DlcPackageId_IsExpansion()
        {
            using var root = new TempDirectory();
            var dlcPath = Path.Combine(root.Path, "Data", "Royalty");
            Directory.CreateDirectory(Path.Combine(dlcPath, "About"));
            File.WriteAllText(Path.Combine(dlcPath, "About", "About.xml"),
                "<ModMetaData><packageId>Ludeon.RimWorld.Royalty</packageId></ModMetaData>");

            var service = CreateService(Path.Combine(root.Path, "Mods"), gameFolder: root.Path);

            service.ClassifyMod(dlcPath).Should().Be(ModType.Expansion);
        }

        [Fact]
        public void DataMod_UnknownPackageId_IsExpansionNeverCore()
        {
            using var root = new TempDirectory();
            var modPath = Path.Combine(root.Path, "Data", "Mystery");
            Directory.CreateDirectory(Path.Combine(modPath, "About"));
            File.WriteAllText(Path.Combine(modPath, "About", "About.xml"),
                "<ModMetaData><packageId>someone.else</packageId></ModMetaData>");

            var service = CreateService(Path.Combine(root.Path, "Mods"), gameFolder: root.Path);

            service.ClassifyMod(modPath).Should().Be(ModType.Expansion);
        }

        [Fact]
        public void MissingDirectory_ReturnsExistingType()
        {
            using var root = new TempDirectory();
            var service = CreateService(root.Path);

            service.ClassifyMod(Path.Combine(root.Path, "nope"), ModType.Git).Should().Be(ModType.Git);
        }

        [Fact]
        public void DataMod_BundleWithContentsData_ShouldClassifyAsExpansion()
        {
            // Arrange: synthetic .app bundle with Contents/Data
            using var root = new TempDirectory();
            var bundlePath = Path.Combine(root.Path, "RimWorld.app");
            var contentsData = Path.Combine(bundlePath, "Contents", "Data", "Royalty");
            Directory.CreateDirectory(Path.Combine(contentsData, "About"));
            File.WriteAllText(Path.Combine(contentsData, "About", "About.xml"),
                "<ModMetaData><packageId>Ludeon.RimWorld.Royalty</packageId></ModMetaData>");

            var service = CreateService(Path.Combine(root.Path, "Mods"), gameFolder: bundlePath);

            // Act & Assert: mod inside Contents/Data should be classified as Expansion
            service.ClassifyMod(contentsData).Should().Be(ModType.Expansion);
        }

        [Fact]
        public void DataMod_BundleWithContentsResourcesData_ShouldClassifyAsExpansion()
        {
            // Arrange: synthetic .app bundle with Contents/Resources/Data
            using var root = new TempDirectory();
            var bundlePath = Path.Combine(root.Path, "RimWorld.app");
            var contentsResourcesData = Path.Combine(bundlePath, "Contents", "Resources", "Data", "Royalty");
            Directory.CreateDirectory(Path.Combine(contentsResourcesData, "About"));
            File.WriteAllText(Path.Combine(contentsResourcesData, "About", "About.xml"),
                "<ModMetaData><packageId>Ludeon.RimWorld.Royalty</packageId></ModMetaData>");

            var service = CreateService(Path.Combine(root.Path, "Mods"), gameFolder: bundlePath);

            // Act & Assert: mod inside Contents/Resources/Data should be classified as Expansion
            service.ClassifyMod(contentsResourcesData).Should().Be(ModType.Expansion);
        }

        [Fact]
        public void DataMod_BundleWithRootData_ShouldClassifyAsExpansion()
        {
            // Arrange: synthetic .app bundle with bundle-root Data
            using var root = new TempDirectory();
            var bundlePath = Path.Combine(root.Path, "RimWorld.app");
            var rootData = Path.Combine(bundlePath, "Data", "Royalty");
            Directory.CreateDirectory(Path.Combine(rootData, "About"));
            File.WriteAllText(Path.Combine(rootData, "About", "About.xml"),
                "<ModMetaData><packageId>Ludeon.RimWorld.Royalty</packageId></ModMetaData>");

            var service = CreateService(Path.Combine(root.Path, "Mods"), gameFolder: bundlePath);

            // Act & Assert: mod inside bundle-root Data should be classified as Expansion
            service.ClassifyMod(rootData).Should().Be(ModType.Expansion);
        }

        [Fact]
        public void DataMod_BundleCoreInContentsData_ShouldClassifyAsCore()
        {
            // Arrange: synthetic .app bundle with Core in Contents/Data
            using var root = new TempDirectory();
            var bundlePath = Path.Combine(root.Path, "RimWorld.app");
            var corePath = Path.Combine(bundlePath, "Contents", "Data", "Core");
            Directory.CreateDirectory(corePath);
            File.WriteAllText(Path.Combine(corePath, "About.xml"),
                "<ModMetaData><packageId>Ludeon.RimWorld</packageId></ModMetaData>");

            var service = CreateService(Path.Combine(root.Path, "Mods"), gameFolder: bundlePath);

            // Act & Assert: Core in Contents/Data should be classified as Core
            service.ClassifyMod(corePath).Should().Be(ModType.Core);
        }

        [Fact]
        public void DataMod_BundleModInContentsResourcesData_NotMisclassifiedAsWorkshop()
        {
            // Arrange: synthetic .app bundle with a mod in Contents/Resources/Data
            // This mod should NOT be misclassified as Workshop just because it's in a bundle
            using var root = new TempDirectory();
            var bundlePath = Path.Combine(root.Path, "RimWorld.app");
            var modPath = Path.Combine(bundlePath, "Contents", "Resources", "Data", "SomeMod");
            Directory.CreateDirectory(Path.Combine(modPath, "About"));
            File.WriteAllText(Path.Combine(modPath, "About", "About.xml"),
                "<ModMetaData><packageId>some.mod</packageId></ModMetaData>");

            var service = CreateService(Path.Combine(root.Path, "Mods"), gameFolder: bundlePath);

            // Act & Assert: should be Expansion (data mod), not Workshop
            service.ClassifyMod(modPath).Should().Be(ModType.Expansion);
        }

        private sealed class TempDirectory : IDisposable
        {
            public string Path { get; }

            public TempDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rimsharp_cls_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Path, true);
                }
                catch (IOException)
                {
                    // Best-effort cleanup.
                }
            }
        }
    }
}
