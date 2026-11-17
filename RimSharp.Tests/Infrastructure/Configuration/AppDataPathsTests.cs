using System;
using System.IO;
using FluentAssertions;
using RimSharp.Infrastructure.Configuration;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Configuration
{
    public class AppDataPathsTests : IDisposable
    {
        private const string XdgConfigHomeEnv = "XDG_CONFIG_HOME";
        private const string XdgDataHomeEnv = "XDG_DATA_HOME";

        private readonly string _tempRoot;
        private readonly string? _originalConfigHome;
        private readonly string? _originalDataHome;

        public AppDataPathsTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "RimSharp_AppDataPaths_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);

            _originalConfigHome = Environment.GetEnvironmentVariable(XdgConfigHomeEnv);
            _originalDataHome = Environment.GetEnvironmentVariable(XdgDataHomeEnv);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(XdgConfigHomeEnv, _originalConfigHome);
            Environment.SetEnvironmentVariable(XdgDataHomeEnv, _originalDataHome);

            try { Directory.Delete(_tempRoot, true); } catch { /* best effort */ }
        }

        [Fact]
        public void GetConfigFilePath_ShouldBeConfigDirectoryPlusConfigCfg()
        {
            AppDataPaths.GetConfigFilePath()
                .Should().Be(Path.Combine(AppDataPaths.ConfigDirectory, "config.cfg"));
        }

        [Fact]
        public void OnLinux_Directories_ShouldEndWithAppFolder()
        {
            if (OperatingSystem.IsWindows()) return; // Windows branch not exercised on this runner.

            AppDataPaths.ConfigDirectory.Should().EndWith("RimSharp");
            AppDataPaths.DataDirectory.Should().EndWith("RimSharp");
        }

        [Fact]
        public void OnLinux_ConfigDirectory_ShouldHonorXdgConfigHomeOverride()
        {
            if (OperatingSystem.IsWindows()) return;

            var xdgConfigHome = Path.Combine(_tempRoot, "xdg-config");
            Directory.CreateDirectory(xdgConfigHome);
            Environment.SetEnvironmentVariable(XdgConfigHomeEnv, xdgConfigHome);

            try
            {
                AppDataPaths.ConfigDirectory.Should().Be(Path.Combine(xdgConfigHome, "RimSharp"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(XdgConfigHomeEnv, _originalConfigHome);
            }
        }

        [Fact]
        public void OnLinux_DataDirectory_ShouldHonorXdgDataHomeOverride()
        {
            if (OperatingSystem.IsWindows()) return;

            var xdgDataHome = Path.Combine(_tempRoot, "xdg-data");
            Directory.CreateDirectory(xdgDataHome);
            Environment.SetEnvironmentVariable(XdgDataHomeEnv, xdgDataHome);

            try
            {
                AppDataPaths.DataDirectory.Should().Be(Path.Combine(xdgDataHome, "RimSharp"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(XdgDataHomeEnv, _originalDataHome);
            }
        }

        [Fact]
        public void OnLinux_WhenXdgEnvUnset_ShouldFallBackToUserDirectories()
        {
            if (OperatingSystem.IsWindows()) return;

            Environment.SetEnvironmentVariable(XdgConfigHomeEnv, null);
            Environment.SetEnvironmentVariable(XdgDataHomeEnv, null);

            try
            {
                var expectedConfig = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
                    "RimSharp");
                var expectedData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
                    "RimSharp");

                AppDataPaths.ConfigDirectory.Should().Be(expectedConfig);
                AppDataPaths.DataDirectory.Should().Be(expectedData);
            }
            finally
            {
                Environment.SetEnvironmentVariable(XdgConfigHomeEnv, _originalConfigHome);
                Environment.SetEnvironmentVariable(XdgDataHomeEnv, _originalDataHome);
            }
        }

        [Fact]
        public void OnLinux_RelativeXdgOverride_ShouldBeIgnored()
        {
            if (OperatingSystem.IsWindows()) return;

            // XDG spec: relative values are invalid and must be ignored.
            Environment.SetEnvironmentVariable(XdgConfigHomeEnv, "relative/path");
            Environment.SetEnvironmentVariable(XdgDataHomeEnv, "relative/path");

            try
            {
                var expectedConfig = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
                    "RimSharp");
                var expectedData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
                    "RimSharp");

                AppDataPaths.ConfigDirectory.Should().Be(expectedConfig);
                AppDataPaths.DataDirectory.Should().Be(expectedData);
            }
            finally
            {
                Environment.SetEnvironmentVariable(XdgConfigHomeEnv, _originalConfigHome);
                Environment.SetEnvironmentVariable(XdgDataHomeEnv, _originalDataHome);
            }
        }

        [Fact]
        public void OnWindows_Directories_ShouldEqualBaseDirectory()
        {
            if (!OperatingSystem.IsWindows()) return; // Skipped on Linux runners.

            AppDataPaths.ConfigDirectory.Should().Be(AppDataPaths.BaseDirectory);
            AppDataPaths.DataDirectory.Should().Be(AppDataPaths.BaseDirectory);
        }

        [Fact]
        public void EnsureDirectory_ShouldCreateMissingDirectory()
        {
            var target = Path.Combine(_tempRoot, "ensure", "nested");
            Directory.Exists(target).Should().BeFalse();

            AppDataPaths.EnsureDirectory(target).Should().Be(target);

            Directory.Exists(target).Should().BeTrue();
        }

        [Fact]
        public void EnsureDirectory_ShouldReturnExistingDirectory()
        {
            var target = Path.Combine(_tempRoot, "existing");
            Directory.CreateDirectory(target);

            AppDataPaths.EnsureDirectory(target).Should().Be(target);
        }

        [Fact]
        public void MigrateRenamedDataFolderCore_WhenSourceMissing_ShouldBeNoOp()
        {
            var source = Path.Combine(_tempRoot, "missing-rules");
            var target = Path.Combine(_tempRoot, "data");

            AppDataPaths.MigrateRenamedDataFolderCore(source, target);

            Directory.Exists(source).Should().BeFalse();
            Directory.Exists(target).Should().BeFalse();
        }

        [Fact]
        public void MigrateRenamedDataFolderCore_WhenTargetMissing_ShouldRenameFolder()
        {
            var source = Path.Combine(_tempRoot, "rules");
            var target = Path.Combine(_tempRoot, "data");
            Directory.CreateDirectory(Path.Combine(source, "db"));
            File.WriteAllText(Path.Combine(source, "db", "db.json"), "{}");

            AppDataPaths.MigrateRenamedDataFolderCore(source, target);

            Directory.Exists(source).Should().BeFalse();
            File.Exists(Path.Combine(target, "db", "db.json")).Should().BeTrue();
        }

        [Fact]
        public void MigrateRenamedDataFolderCore_WhenTargetExists_ShouldOverwritePlaceholdersAndRemoveSource()
        {
            var source = Path.Combine(_tempRoot, "rules");
            var target = Path.Combine(_tempRoot, "data");
            Directory.CreateDirectory(Path.Combine(source, "db"));
            File.WriteAllText(Path.Combine(source, "db", "db.json"), "user-data");
            Directory.CreateDirectory(Path.Combine(target, "db"));
            File.WriteAllText(Path.Combine(target, "db", "db.json"), "{}");

            AppDataPaths.MigrateRenamedDataFolderCore(source, target);

            File.ReadAllText(Path.Combine(target, "db", "db.json")).Should().Be("user-data");
            Directory.Exists(source).Should().BeFalse("a surviving stale source would revert live data on later startups");
        }

        [Fact]
        public void MigrateRenamedDataFolderCore_WhenTargetExists_ShouldMoveMissingSubdirsAndKeepExisting()
        {
            var source = Path.Combine(_tempRoot, "rules");
            var target = Path.Combine(_tempRoot, "data");
            Directory.CreateDirectory(Path.Combine(source, "Custom"));
            File.WriteAllText(Path.Combine(source, "Custom", "Mods.json"), "user-custom");
            Directory.CreateDirectory(Path.Combine(target, "Custom"));
            File.WriteAllText(Path.Combine(target, "Custom", "Keep.json"), "fresh");

            AppDataPaths.MigrateRenamedDataFolderCore(source, target);

            File.Exists(Path.Combine(target, "Custom", "Keep.json")).Should().BeTrue();
            File.Exists(Path.Combine(target, "Custom", "Mods.json")).Should().BeTrue();
            Directory.Exists(source).Should().BeFalse();
        }

        [Fact]
        public void MigrateRenamedDataFolderCore_ShouldCarryOverEmptySubdirectories()
        {
            var source = Path.Combine(_tempRoot, "rules");
            var target = Path.Combine(_tempRoot, "data");
            Directory.CreateDirectory(Path.Combine(source, "Custom", "some.mod"));

            AppDataPaths.MigrateRenamedDataFolderCore(source, target);

            Directory.Exists(Path.Combine(target, "Custom", "some.mod")).Should().BeTrue();
            Directory.Exists(source).Should().BeFalse();
        }

        [Fact]
        public void MigrateRenamedDataFolderCore_AfterMergeCompletes_ShouldBeIdempotent()
        {
            var source = Path.Combine(_tempRoot, "rules");
            var target = Path.Combine(_tempRoot, "data");
            Directory.CreateDirectory(Path.Combine(source, "db"));
            File.WriteAllText(Path.Combine(source, "db", "db.json"), "user-data");

            AppDataPaths.MigrateRenamedDataFolderCore(source, target);

            // Second run: source no longer exists, and newer live data in the
            // target (written after the first run) is left untouched.
            File.WriteAllText(Path.Combine(target, "db", "db.json"), "newer-live-data");
            AppDataPaths.MigrateRenamedDataFolderCore(source, target);

            File.ReadAllText(Path.Combine(target, "db", "db.json")).Should().Be("newer-live-data");
        }

        [Fact]
        public void MigrateCuratedDataLayoutCore_WhenDbDirMissing_ShouldBeNoOp()
        {
            var dataRoot = Path.Combine(_tempRoot, "data");

            AppDataPaths.MigrateCuratedDataLayoutCore(dataRoot);

            Directory.Exists(Path.Combine(dataRoot, "Rules")).Should().BeFalse();
            Directory.Exists(Path.Combine(dataRoot, "db")).Should().BeFalse();
        }

        [Fact]
        public void MigrateCuratedDataLayoutCore_WhenDbDirEmpty_ShouldBeNoOp()
        {
            var dataRoot = Path.Combine(_tempRoot, "data");
            Directory.CreateDirectory(Path.Combine(dataRoot, "db"));

            AppDataPaths.MigrateCuratedDataLayoutCore(dataRoot);

            Directory.Exists(Path.Combine(dataRoot, "Rules")).Should().BeFalse();
            Directory.Exists(Path.Combine(dataRoot, "db")).Should().BeFalse("an empty retired folder is pruned");
        }

        [Fact]
        public void MigrateCuratedDataLayoutCore_ShouldMoveCuratedFilesIntoPerCategoryLayout()
        {
            var dataRoot = Path.Combine(_tempRoot, "data");
            var dbDir = Path.Combine(dataRoot, "db");
            Directory.CreateDirectory(dbDir);
            File.WriteAllText(Path.Combine(dbDir, "rules.json"), @"{ ""rules"": {} }");
            File.WriteAllText(Path.Combine(dbDir, "replacements.json"), @"{ ""mods"": {} }");
            File.WriteAllText(Path.Combine(dbDir, "db.json"), @"{ ""mods"": {} }");

            AppDataPaths.MigrateCuratedDataLayoutCore(dataRoot);

            File.Exists(Path.Combine(dataRoot, "Rules", "RimSharp", "rules.json")).Should().BeTrue();
            File.ReadAllText(Path.Combine(dataRoot, "Rules", "RimSharp", "rules.json")).Should().Be(@"{ ""rules"": {} }");
            File.Exists(Path.Combine(dataRoot, "Replacements", "RimSharp", "replacements.json")).Should().BeTrue();
            File.Exists(Path.Combine(dataRoot, "ModInfo", "RimSharp", "db.json")).Should().BeTrue();
            Directory.Exists(dbDir).Should().BeFalse("Data/db is retired once every curated file moved out");
        }

        [Fact]
        public void MigrateCuratedDataLayoutCore_WhenTargetAlreadyExists_ShouldDeleteStaleSource()
        {
            var dataRoot = Path.Combine(_tempRoot, "data");
            var dbDir = Path.Combine(dataRoot, "db");
            Directory.CreateDirectory(dbDir);
            File.WriteAllText(Path.Combine(dbDir, "rules.json"), "stale");
            Directory.CreateDirectory(Path.Combine(dataRoot, "Rules", "RimSharp"));
            File.WriteAllText(Path.Combine(dataRoot, "Rules", "RimSharp", "rules.json"), "live");

            AppDataPaths.MigrateCuratedDataLayoutCore(dataRoot);

            File.ReadAllText(Path.Combine(dataRoot, "Rules", "RimSharp", "rules.json")).Should().Be("live");
            File.Exists(Path.Combine(dbDir, "rules.json")).Should().BeFalse("a surviving stale source would be re-migrated on every startup");
        }

        [Fact]
        public void MigrateCuratedDataLayoutCore_ShouldDeleteObsoleteManifest()
        {
            var dataRoot = Path.Combine(_tempRoot, "data");
            var dbDir = Path.Combine(dataRoot, "db");
            Directory.CreateDirectory(dbDir);
            File.WriteAllText(Path.Combine(dbDir, "manifest.json"), "{}");

            AppDataPaths.MigrateCuratedDataLayoutCore(dataRoot);

            File.Exists(Path.Combine(dbDir, "manifest.json")).Should().BeFalse();
            Directory.Exists(dbDir).Should().BeFalse();
        }

        [Fact]
        public void MigrateCuratedDataLayoutCore_ShouldKeepUnrelatedFilesInDbDir()
        {
            var dataRoot = Path.Combine(_tempRoot, "data");
            var dbDir = Path.Combine(dataRoot, "db");
            Directory.CreateDirectory(dbDir);
            File.WriteAllText(Path.Combine(dbDir, "rules.json"), "rules");
            File.WriteAllText(Path.Combine(dbDir, "user-note.txt"), "keep me");

            AppDataPaths.MigrateCuratedDataLayoutCore(dataRoot);

            File.Exists(Path.Combine(dbDir, "user-note.txt")).Should().BeTrue("only app-managed curated files are touched");
            Directory.Exists(dbDir).Should().BeTrue("a non-empty retired folder is left in place");
        }
    }
}
