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
    }
}
